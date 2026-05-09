using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.Maui.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using StorageChangeOperation = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeOperation;
using StorageChangeRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeRecord;
using StorageConflictRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictRecord;
using StorageConflictResolution = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictResolution;
using StorageConflictType = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictType;
using StorageSyncSession = Honua.Mobile.FieldCollection.Services.Storage.Models.SyncSession;
using StorageSyncSessionStatus = Honua.Mobile.FieldCollection.Services.Storage.Models.SyncSessionStatus;
using FieldPoint = Honua.Mobile.FieldCollection.Models.Point;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.FieldCollection.Services.Sync;

/// <summary>
/// Uploads local field collection changes to the remote sync service.
/// </summary>
public interface IFieldCollectionChangeUploader
{
    Task<bool> UploadChangeAsync(StorageChangeRecord change, CancellationToken cancellationToken = default);
}

/// <summary>
/// Downloads remote field collection changes and server sync state.
/// </summary>
public interface IFieldCollectionChangePuller
{
    Task<IReadOnlyList<ServerChange>> GetChangesAsync(long sinceGeneration, CancellationToken cancellationToken = default);
    Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default);
    Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Real implementation of sync service with GeoPackage-based delta sync
/// Implements last-write-wins conflict resolution with manual merge support
/// </summary>
public partial class GeoPackageSyncService : ObservableObject, ISyncService, IDisposable
{
    private static readonly JsonSerializerOptions ConflictJsonOptions = CreateConflictJsonOptions();

    private readonly GeoPackageStorageService _storage;
    private readonly IAuthenticationService _authService;
    private readonly IConnectivityService _connectivityService;
    private readonly IFieldCollectionChangeUploader _changeUploader;
    private readonly IFieldCollectionChangePuller _changePuller;
    private readonly IMobileExceptionReporter _exceptionReporter;
    private readonly ILogger<GeoPackageSyncService>? _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly CancellationTokenSource _pendingChangesCancellation = new();
    private readonly Task _pendingChangesTask;
    private CancellationTokenSource? _syncCancellation;
    private bool _disposed;

    [ObservableProperty]
    private bool isSyncing;

    [ObservableProperty]
    private SyncStatus status = SyncStatus.Idle;

    [ObservableProperty]
    private int pendingChangesCount;

    [ObservableProperty]
    private DateTime? lastSyncTime;

    [ObservableProperty]
    private double syncProgress;

    [ObservableProperty]
    private string? syncMessage;

    public GeoPackageSyncService(
        GeoPackageStorageService storage,
        IAuthenticationService authService,
        IConnectivityService connectivityService,
        IFieldCollectionChangeUploader? changeUploader = null,
        IFieldCollectionChangePuller? changePuller = null,
        ILogger<GeoPackageSyncService>? logger = null,
        IMobileExceptionReporter? exceptionReporter = null)
    {
        _storage = storage;
        _authService = authService;
        _connectivityService = connectivityService;
        _changeUploader = changeUploader ?? new UnconfiguredFieldCollectionChangeUploader();
        _changePuller = changePuller ?? new UnconfiguredFieldCollectionChangePuller();
        _exceptionReporter = exceptionReporter ?? new NoOpMobileExceptionReporter();
        _logger = logger;

        // Update pending changes count periodically
        _pendingChangesTask = Task.Run(() => UpdatePendingChangesAsync(_pendingChangesCancellation.Token));
    }

    private async Task UpdatePendingChangesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var changes = await _storage.GetPendingChangesAsync();
                PendingChangesCount = changes.Count;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to update pending field collection change count");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    #region Full Sync

    public async Task<SyncResult> SyncAsync()
    {
        if (!await CanSyncAsync())
        {
            return new SyncResult
            {
                IsSuccess = false,
                ErrorMessage = "Cannot sync - check authentication and connectivity"
            };
        }

        await _syncLock.WaitAsync();
        try
        {
            _syncCancellation = new CancellationTokenSource();
            var sessionId = Guid.NewGuid().ToString();
            var startTime = DateTime.UtcNow;

            IsSyncing = true;
            Status = SyncStatus.Syncing;
            SyncProgress = 0;
            SyncMessage = "Starting sync session...";

            StorageSyncSession? session = null;
            try
            {
                session = await CreateSyncSessionAsync(sessionId, includeRemoteGeneration: true);

                // Phase 1: Pull changes from server
                Status = SyncStatus.PullingChanges;
                SyncMessage = "Downloading changes from server...";
                await PullChangesInternalAsync(session, _syncCancellation.Token);

                SyncProgress = 0.5;

                // Phase 2: Push local changes
                Status = SyncStatus.PushingChanges;
                SyncMessage = "Uploading changes to server...";
                await PushChangesInternalAsync(session, _syncCancellation.Token);

                SyncProgress = 0.8;

                // Phase 3: Resolve conflicts if any
                if (session.ConflictsDetected > 0)
                {
                    Status = SyncStatus.ResolvingConflicts;
                    SyncMessage = "Resolving conflicts...";
                    await AutoResolveConflictsAsync(session);
                }

                SyncProgress = 1.0;
                await CompleteSyncSessionAsync(session, StorageSyncSessionStatus.Completed);

                LastSyncTime = DateTime.UtcNow;
                var duration = DateTime.UtcNow - startTime;

                return new SyncResult
                {
                    IsSuccess = true,
                    CompletedAt = DateTime.UtcNow,
                    Duration = duration,
                    ChangesPulled = session.ChangesPulled,
                    ChangesPushed = session.ChangesPushed,
                    ConflictsDetected = session.ConflictsDetected
                };
            }
            catch (OperationCanceledException)
            {
                Status = SyncStatus.Cancelled;
                if (session != null)
                {
                    await CompleteSyncSessionAsync(session, StorageSyncSessionStatus.Cancelled);
                }

                return new SyncResult
                {
                    IsSuccess = false,
                    ErrorMessage = "Sync was cancelled",
                    CompletedAt = DateTime.UtcNow,
                    Duration = DateTime.UtcNow - startTime
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Field collection sync failed");
                Status = SyncStatus.Error;
                if (session != null)
                {
                    await CompleteSyncSessionAsync(session, StorageSyncSessionStatus.Failed, ex.Message);
                }

                await ReportSyncFailureAsync(ex, "sync.full", sessionId, session);

                return new SyncResult
                {
                    IsSuccess = false,
                    ErrorMessage = ex.Message,
                    CompletedAt = DateTime.UtcNow,
                    Duration = DateTime.UtcNow - startTime
                };
            }
        }
        finally
        {
            IsSyncing = false;
            Status = SyncStatus.Idle;
            SyncProgress = 0;
            SyncMessage = null;
            _syncCancellation?.Dispose();
            _syncCancellation = null;
            _syncLock.Release();
        }
    }

    #endregion

    #region Pull Changes

    public async Task<SyncResult> PullChangesAsync()
    {
        if (!await CanSyncAsync())
        {
            return new SyncResult
            {
                IsSuccess = false,
                ErrorMessage = "Cannot pull changes - check authentication and connectivity"
            };
        }

        await _syncLock.WaitAsync();
        string? sessionId = null;
        StorageSyncSession? session = null;
        try
        {
            _syncCancellation = new CancellationTokenSource();
            sessionId = Guid.NewGuid().ToString();
            session = await CreateSyncSessionAsync(sessionId, includeRemoteGeneration: true);

            IsSyncing = true;
            Status = SyncStatus.PullingChanges;

            await PullChangesInternalAsync(session, _syncCancellation.Token);
            await CompleteSyncSessionAsync(session, StorageSyncSessionStatus.Completed);

            LastSyncTime = DateTime.UtcNow;

            return new SyncResult
            {
                IsSuccess = true,
                CompletedAt = DateTime.UtcNow,
                ChangesPulled = session.ChangesPulled
            };
        }
        catch (OperationCanceledException)
        {
            Status = SyncStatus.Cancelled;
            return new SyncResult
            {
                IsSuccess = false,
                ErrorMessage = "Pull was cancelled",
                CompletedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Field collection pull failed");
            await ReportSyncFailureAsync(ex, "sync.pull", sessionId, session);
            return new SyncResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            IsSyncing = false;
            Status = SyncStatus.Idle;
            _syncCancellation?.Dispose();
            _syncCancellation = null;
            _syncLock.Release();
        }
    }

    private async Task<bool> PullChangesInternalAsync(StorageSyncSession session, CancellationToken cancellationToken)
    {
        try
        {
            var changesSinceLastSync = await _changePuller.GetChangesAsync(
                session.LocalGeneration,
                cancellationToken);

            foreach (var serverChange in changesSinceLastSync)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await ApplyServerChangeAsync(serverChange, session))
                {
                    session.ChangesPulled++;
                }
            }

            session.ServerGeneration = await _changePuller.GetLatestServerGenerationAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw;
        }
    }

    #endregion

    #region Push Changes

    public async Task<SyncResult> PushChangesAsync()
    {
        if (!await CanSyncAsync())
        {
            return new SyncResult
            {
                IsSuccess = false,
                ErrorMessage = "Cannot push changes - check authentication and connectivity"
            };
        }

        await _syncLock.WaitAsync();
        string? sessionId = null;
        StorageSyncSession? session = null;
        try
        {
            var pendingChanges = await _storage.GetPendingChangesAsync();
            if (pendingChanges.Count == 0)
            {
                return new SyncResult
                {
                    IsSuccess = true,
                    CompletedAt = DateTime.UtcNow,
                    ChangesPushed = 0
                };
            }

            _syncCancellation = new CancellationTokenSource();
            sessionId = Guid.NewGuid().ToString();
            session = await CreateSyncSessionAsync(sessionId, includeRemoteGeneration: false);

            IsSyncing = true;
            Status = SyncStatus.PushingChanges;

            await PushChangesInternalAsync(session, _syncCancellation.Token);
            await CompleteSyncSessionAsync(session, StorageSyncSessionStatus.Completed);

            return new SyncResult
            {
                IsSuccess = true,
                CompletedAt = DateTime.UtcNow,
                ChangesPushed = session.ChangesPushed
            };
        }
        catch (OperationCanceledException)
        {
            Status = SyncStatus.Cancelled;
            return new SyncResult
            {
                IsSuccess = false,
                ErrorMessage = "Push was cancelled",
                CompletedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Field collection push failed");
            await ReportSyncFailureAsync(ex, "sync.push", sessionId, session);
            return new SyncResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            IsSyncing = false;
            Status = SyncStatus.Idle;
            _syncCancellation?.Dispose();
            _syncCancellation = null;
            _syncLock.Release();
        }
    }

    private async Task<bool> PushChangesInternalAsync(StorageSyncSession session, CancellationToken cancellationToken)
    {
        try
        {
            var pendingChanges = await _storage.GetPendingChangesAsync();
            var successfulChanges = new List<string>();
            var failedChanges = new List<string>();

            foreach (var change in pendingChanges)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var success = await _changeUploader.UploadChangeAsync(change, cancellationToken);

                if (success)
                {
                    successfulChanges.Add(change.Id);
                    session.ChangesPushed++;
                }
                else
                {
                    failedChanges.Add(change.Id);
                }
            }

            // Mark successful changes as synced
            if (successfulChanges.Any())
            {
                await _storage.MarkChangesAsSynced(successfulChanges);
            }

            if (failedChanges.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{failedChanges.Count} pending change(s) failed to upload and remain unsynced.");
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw;
        }
    }

    #endregion

    #region Conflict Resolution

    public async Task<IEnumerable<ConflictInfo>> GetConflictsAsync()
    {
        var conflicts = await _storage.GetUnresolvedConflictsAsync();
        return conflicts.Select(conflict => new ConflictInfo
        {
            Id = conflict.Id,
            OperationId = conflict.Id,
            FeatureId = conflict.FeatureId,
            SourceId = conflict.LayerId.ToString(),
            LayerName = $"Layer {conflict.LayerId}",
            Type = MapConflictType(conflict.ConflictType),
            DetectedAt = conflict.CreatedAt,
            LocalVersion = conflict.LocalData,
            ServerVersion = conflict.ServerData,
            FailureReason = $"Local v{conflict.LocalVersion} conflicts with server v{conflict.ServerVersion}.",
            RedactedLocalVersion = DiagnosticRedactor.RedactJson(conflict.LocalData),
            RedactedServerVersion = DiagnosticRedactor.RedactJson(conflict.ServerData)
        });
    }

    public async Task<bool> ResolveConflictAsync(string conflictId, ConflictResolution resolution)
    {
        var conflict = await _storage.GetConflictAsync(conflictId);
        if (conflict == null || conflict.ResolvedAt != null)
        {
            return false;
        }

        switch (resolution)
        {
            case ConflictResolution.AcceptLocal:
                await _storage.MarkConflictResolvedAsync(
                    conflictId,
                    StorageConflictResolution.AcceptLocal,
                    conflict.LocalData);
                return true;

            case ConflictResolution.AcceptServer:
                var serverChange = JsonSerializer.Deserialize<ServerChange>(
                    conflict.ServerData,
                    ConflictJsonOptions);
                if (serverChange == null)
                {
                    return false;
                }

                await ApplyResolvedServerChangeAsync(serverChange);
                await _storage.MarkConflictResolvedAsync(
                    conflictId,
                    StorageConflictResolution.AcceptServer,
                    conflict.ServerData);
                return true;

            case ConflictResolution.Merge:
            case ConflictResolution.Manual:
                _logger?.LogWarning(
                    "Manual conflict resolution for {ConflictId} requires resolved feature data",
                    conflictId);
                return false;

            default:
                return false;
        }
    }

    public async Task<bool> DeferConflictAsync(string conflictId)
    {
        var conflict = await _storage.GetConflictAsync(conflictId);
        if (conflict == null || conflict.ResolvedAt != null)
        {
            return false;
        }

        await _storage.MarkConflictDeferredAsync(
            conflictId,
            "Deferred for manual review from sync center.");
        return true;
    }

    private Task AutoResolveConflictsAsync(StorageSyncSession session)
    {
        _logger?.LogInformation(
            "{ConflictCount} sync conflict(s) require manual review",
            session.ConflictsDetected);
        return Task.CompletedTask;
    }

    private async Task ReportSyncFailureAsync(
        Exception exception,
        string operation,
        string? sessionId,
        StorageSyncSession? session)
    {
        var properties = new Dictionary<string, object?>
        {
            ["status"] = Status.ToString(),
            ["pendingChangesCount"] = PendingChangesCount,
        };

        if (session is not null)
        {
            properties["sessionStatus"] = session.Status.ToString();
            properties["changesPulled"] = session.ChangesPulled;
            properties["changesPushed"] = session.ChangesPushed;
            properties["conflictsDetected"] = session.ConflictsDetected;
        }

        try
        {
            await _exceptionReporter.ReportAsync(
                exception,
                new MobileExceptionReportContext
                {
                    Source = "FieldCollection.Sync",
                    Operation = operation,
                    CorrelationId = sessionId,
                    Severity = MobileExceptionSeverity.Error,
                    Properties = properties,
                });
        }
        catch (Exception reportException)
        {
            _logger?.LogWarning(reportException, "Failed to report field collection sync exception");
        }
    }

    private async Task<bool> ApplyServerChangeAsync(ServerChange serverChange, StorageSyncSession session)
    {
        try
        {
            // Check for conflicts
            var localFeature = await _storage.GetFeatureAsync(serverChange.FeatureId, serverChange.LayerId);

            if (localFeature != null && localFeature.Version > serverChange.Version)
            {
                // Conflict detected - local is newer
                await CreateConflictRecordAsync(serverChange, localFeature, session);
                session.ConflictsDetected++;
                return false;
            }

            await ApplyResolvedServerChangeAsync(serverChange);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to apply server change {Operation} for feature {FeatureId} in layer {LayerId}",
                serverChange.Operation,
                serverChange.FeatureId,
                serverChange.LayerId);
            throw;
        }
    }

    private async Task ApplyResolvedServerChangeAsync(ServerChange serverChange)
    {
        switch (serverChange.Operation)
        {
            case StorageChangeOperation.Insert:
            case StorageChangeOperation.Update:
                serverChange.Feature.Id = string.IsNullOrWhiteSpace(serverChange.Feature.Id)
                    ? serverChange.FeatureId
                    : serverChange.Feature.Id;
                serverChange.Feature.LayerId = serverChange.LayerId;
                await _storage.ApplyRemoteFeatureAsync(serverChange.Feature);
                break;
            case StorageChangeOperation.Delete:
                await _storage.ApplyRemoteDeleteAsync(serverChange.FeatureId, serverChange.LayerId);
                break;
        }
    }

    private Task CreateConflictRecordAsync(ServerChange serverChange, Feature localFeature, StorageSyncSession session)
    {
        var conflict = new StorageConflictRecord
        {
            Id = Guid.NewGuid().ToString(),
            FeatureId = serverChange.FeatureId,
            LayerId = serverChange.LayerId,
            ConflictType = serverChange.Operation == StorageChangeOperation.Delete
                ? StorageConflictType.UpdateDelete
                : StorageConflictType.UpdateUpdate,
            LocalVersion = localFeature.Version,
            ServerVersion = serverChange.Version,
            LocalData = JsonSerializer.Serialize(localFeature, ConflictJsonOptions),
            ServerData = JsonSerializer.Serialize(serverChange, ConflictJsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        return _storage.StoreConflictAsync(conflict);
    }

    private static ConflictType MapConflictType(StorageConflictType conflictType)
    {
        return conflictType switch
        {
            StorageConflictType.UpdateDelete => ConflictType.UpdateDelete,
            StorageConflictType.DeleteUpdate => ConflictType.DeleteUpdate,
            _ => ConflictType.UpdateUpdate
        };
    }

    private static JsonSerializerOptions CreateConflictJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new GeometryJsonConverter());
        return options;
    }

    private sealed class GeometryJsonConverter : JsonConverter<Geometry>
    {
        public override Geometry? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            if (!TryGetString(document.RootElement, "type", out var geometryType))
            {
                return null;
            }

            return geometryType switch
            {
                "Point" => new FieldPoint(
                    GetDouble(document.RootElement, "latitude"),
                    GetDouble(document.RootElement, "longitude"),
                    TryGetDouble(document.RootElement, "altitude", out var altitude) ? altitude : null)
                {
                    SRID = GetInt(document.RootElement, "srid", 4326)
                },
                "LineString" => new LineString
                {
                    SRID = GetInt(document.RootElement, "srid", 4326),
                    Coordinates = ReadPointArray(document.RootElement, "coordinates")
                },
                "Polygon" => new Polygon
                {
                    SRID = GetInt(document.RootElement, "srid", 4326),
                    Coordinates = ReadPointRings(document.RootElement, "coordinates")
                },
                _ => null
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            Geometry value,
            JsonSerializerOptions options)
        {
            switch (value)
            {
                case FieldPoint point:
                    WritePoint(writer, point);
                    break;
                case LineString line:
                    writer.WriteStartObject();
                    writer.WriteString("type", line.Type);
                    writer.WriteNumber("srid", line.SRID);
                    writer.WritePropertyName("coordinates");
                    writer.WriteStartArray();
                    foreach (var point in line.Coordinates)
                    {
                        WritePoint(writer, point);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    break;
                case Polygon polygon:
                    writer.WriteStartObject();
                    writer.WriteString("type", polygon.Type);
                    writer.WriteNumber("srid", polygon.SRID);
                    writer.WritePropertyName("coordinates");
                    writer.WriteStartArray();
                    foreach (var ring in polygon.Coordinates)
                    {
                        writer.WriteStartArray();
                        foreach (var point in ring)
                        {
                            WritePoint(writer, point);
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }

        private static void WritePoint(Utf8JsonWriter writer, FieldPoint point)
        {
            writer.WriteStartObject();
            writer.WriteString("type", point.Type);
            writer.WriteNumber("srid", point.SRID);
            writer.WriteNumber("latitude", point.Latitude);
            writer.WriteNumber("longitude", point.Longitude);
            if (point.Altitude.HasValue)
            {
                writer.WriteNumber("altitude", point.Altitude.Value);
            }

            writer.WriteEndObject();
        }

        private static List<FieldPoint> ReadPointArray(JsonElement element, string propertyName)
        {
            if (!TryGetProperty(element, propertyName, out var coordinates) ||
                coordinates.ValueKind != JsonValueKind.Array)
            {
                return new List<FieldPoint>();
            }

            return coordinates.EnumerateArray()
                .Select(ReadPoint)
                .ToList();
        }

        private static List<List<FieldPoint>> ReadPointRings(JsonElement element, string propertyName)
        {
            if (!TryGetProperty(element, propertyName, out var rings) ||
                rings.ValueKind != JsonValueKind.Array)
            {
                return new List<List<FieldPoint>>();
            }

            return rings.EnumerateArray()
                .Where(ring => ring.ValueKind == JsonValueKind.Array)
                .Select(ring => ring.EnumerateArray().Select(ReadPoint).ToList())
                .ToList();
        }

        private static FieldPoint ReadPoint(JsonElement element)
        {
            return new FieldPoint(
                GetDouble(element, "latitude"),
                GetDouble(element, "longitude"),
                TryGetDouble(element, "altitude", out var altitude) ? altitude : null)
            {
                SRID = GetInt(element, "srid", 4326)
            };
        }

        private static bool TryGetString(JsonElement element, string propertyName, out string value)
        {
            value = string.Empty;
            if (!TryGetProperty(element, propertyName, out var property) ||
                property.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            value = property.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static double GetDouble(JsonElement element, string propertyName)
        {
            return TryGetDouble(element, propertyName, out var value) ? value : 0;
        }

        private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
        {
            value = 0;
            return TryGetProperty(element, propertyName, out var property) &&
                property.ValueKind == JsonValueKind.Number &&
                property.TryGetDouble(out value);
        }

        private static int GetInt(JsonElement element, string propertyName, int defaultValue)
        {
            return TryGetProperty(element, propertyName, out var property) &&
                property.ValueKind == JsonValueKind.Number &&
                property.TryGetInt32(out var value)
                ? value
                : defaultValue;
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
        {
            return element.TryGetProperty(propertyName, out property) ||
                element.TryGetProperty(char.ToUpperInvariant(propertyName[0]) + propertyName[1..], out property);
        }
    }

    #endregion

    #region Sync Session Management

    private async Task<StorageSyncSession> CreateSyncSessionAsync(string sessionId, bool includeRemoteGeneration)
    {
        var session = new StorageSyncSession
        {
            Id = sessionId,
            StartTime = DateTime.UtcNow,
            Status = StorageSyncSessionStatus.Active,
            ServerGeneration = includeRemoteGeneration
                ? await _changePuller.GetLatestServerGenerationAsync()
                : 0,
            LocalGeneration = includeRemoteGeneration
                ? await _changePuller.GetLastSyncedGenerationAsync()
                : 0
        };

        // Store in database
        // await _storage.StoreSyncSessionAsync(session);

        return session;
    }

    private Task CompleteSyncSessionAsync(StorageSyncSession session, StorageSyncSessionStatus status, string? errorMessage = null)
    {
        session.EndTime = DateTime.UtcNow;
        session.Status = status;
        session.ErrorMessage = errorMessage;

        // Update in database
        // await _storage.UpdateSyncSessionAsync(session);
        return Task.CompletedTask;
    }

    #endregion

    #region Helper Methods

    private Task<bool> CanSyncAsync()
    {
        return Task.FromResult(_connectivityService.IsConnected && _authService.IsAuthenticated);
    }

    public Task CancelSyncAsync()
    {
        _syncCancellation?.Cancel();
        Status = SyncStatus.Cancelled;
        return Task.CompletedTask;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pendingChangesCancellation.Cancel();
        _syncCancellation?.Cancel();

        try
        {
            _pendingChangesTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Background status refresh has been cancelled; preserve disposal flow.
        }
        finally
        {
            _pendingChangesCancellation.Dispose();
            _syncCancellation?.Dispose();
            _syncLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    #endregion
}

internal sealed class UnconfiguredFieldCollectionChangeUploader : IFieldCollectionChangeUploader
{
    public Task<bool> UploadChangeAsync(StorageChangeRecord change, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "Field collection change upload is not configured. Pending local changes were left unsynced.");
    }
}

internal sealed class UnconfiguredFieldCollectionChangePuller : IFieldCollectionChangePuller
{
    public Task<IReadOnlyList<ServerChange>> GetChangesAsync(
        long sinceGeneration,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "Field collection server pull is not configured. Remote changes were not downloaded.");
    }

    public Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "Field collection server generation lookup is not configured.");
    }

    public Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "Field collection local sync generation lookup is not configured.");
    }
}

/// <summary>
/// Represents a change from the server during pull operations
/// </summary>
public class ServerChange
{
    public string FeatureId { get; set; } = string.Empty;
    public int LayerId { get; set; }
    public StorageChangeOperation Operation { get; set; }
    public long Version { get; set; }
    public Feature Feature { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
