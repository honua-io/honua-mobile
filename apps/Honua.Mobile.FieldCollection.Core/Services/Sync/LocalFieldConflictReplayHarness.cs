using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Storage;
using StorageChangeOperation = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeOperation;
using StorageChangeRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeRecord;

namespace Honua.Mobile.FieldCollection.Services.Sync;

public sealed class LocalFieldConflictReplayPlan
{
    public string RunId { get; init; } = $"local-conflict-replay-{Guid.NewGuid():N}";
    public LayerInfo Layer { get; init; } = new()
    {
        Id = 1,
        ServiceId = "local-conflict-replay",
        SourceId = "local-conflict-replay/FeatureServer/1",
        Name = "Local Conflict Replay",
        GeometryType = GeometryType.Point,
        IsEditable = true
    };

    public string FeatureId { get; init; } = "asset-conflict";
    public long LocalVersion { get; init; } = 2;
    public long ServerVersion { get; init; } = 1;
    public Dictionary<string, object?> LocalAttributes { get; init; } = new(StringComparer.Ordinal)
    {
        ["name"] = "local edit",
        ["status"] = "field-updated"
    };

    public Dictionary<string, object?> ServerAttributes { get; init; } = new(StringComparer.Ordinal)
    {
        ["name"] = "server edit",
        ["status"] = "office-updated"
    };

    public ConflictResolution Resolution { get; init; } = ConflictResolution.AcceptServer;
}

public sealed class LocalFieldConflictReplayResult
{
    public string RunId { get; init; } = string.Empty;
    public string? EvidencePath { get; init; }
    public SyncResult PullResult { get; init; } = new();
    public bool ResolutionApplied { get; init; }
    public ConflictInfo? Conflict { get; init; }
    public Feature? FinalFeature { get; init; }
    public OfflineCacheDiagnostics Diagnostics { get; init; } = new();
}

public sealed class LocalReplayFieldSyncPeer :
    IFieldCollectionChangeUploader,
    IFieldCollectionChangePuller,
    IFieldCollectionAttachmentSynchronizer,
    IFieldCollectionRemoteSyncCapability
{
    private readonly IReadOnlyList<ServerChange> _serverChanges;
    private readonly long _latestServerGeneration;

    public LocalReplayFieldSyncPeer(
        IReadOnlyList<ServerChange>? serverChanges = null,
        long latestServerGeneration = 1)
    {
        _serverChanges = serverChanges ?? Array.Empty<ServerChange>();
        _latestServerGeneration = latestServerGeneration;
    }

    public bool IsRemoteSyncConfigured => true;
    public List<StorageChangeRecord> UploadedChanges { get; } = [];
    public bool RejectUploads { get; set; }
    public AttachmentSyncResult PushAttachmentResult { get; set; } = new();
    public AttachmentSyncResult PullAttachmentResult { get; set; } = new();
    public long? CommittedGeneration { get; private set; }

    public Task<bool> UploadChangeAsync(
        StorageChangeRecord change,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        UploadedChanges.Add(change);
        return Task.FromResult(!RejectUploads);
    }

    public Task<IReadOnlyList<ServerChange>> GetChangesAsync(
        long sinceGeneration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_serverChanges);
    }

    public Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_latestServerGeneration);
    }

    public Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }

    public Task CommitSyncedGenerationAsync(long generation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CommittedGeneration = generation;
        return Task.CompletedTask;
    }

    public Task<AttachmentSyncResult> PushPendingAttachmentsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PushAttachmentResult);
    }

    public Task<AttachmentSyncResult> PullRemoteAttachmentsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PullAttachmentResult);
    }
}

public sealed class LocalFieldConflictReplayHarness
{
    private const string EvidenceSchemaVersion = "honua.mobile.local-conflict-replay.evidence.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly GeoPackageStorageService _storage;
    private readonly ISyncService _syncService;
    private readonly string? _defaultEvidenceDirectory;

    public LocalFieldConflictReplayHarness(
        GeoPackageStorageService storage,
        ISyncService syncService,
        string? defaultEvidenceDirectory = null)
    {
        _storage = storage;
        _syncService = syncService;
        _defaultEvidenceDirectory = defaultEvidenceDirectory;
    }

    public async Task<LocalFieldConflictReplayResult> RunAsync(
        LocalFieldConflictReplayPlan? plan = null,
        string? evidenceDirectory = null,
        CancellationToken cancellationToken = default)
    {
        plan ??= new LocalFieldConflictReplayPlan();
        var startedAtUtc = DateTime.UtcNow;
        var events = new List<object>();

        await _storage.CreateLayerAsync(plan.Layer).ConfigureAwait(false);
        events.Add(Event("layer-ready", startedAtUtc, new
        {
            layerId = plan.Layer.Id,
            layerName = plan.Layer.Name
        }));

        var localFeature = CreateFeature(
            plan.FeatureId,
            plan.Layer.Id,
            plan.LocalVersion,
            plan.LocalAttributes);
        await _storage.StoreFeatureAsync(localFeature).ConfigureAwait(false);
        events.Add(Event("local-edit-stored", DateTime.UtcNow, new
        {
            featureId = plan.FeatureId,
            version = plan.LocalVersion
        }));

        var pullResult = await _syncService.PullChangesAsync().ConfigureAwait(false);
        var conflicts = (await _syncService.GetConflictsAsync().ConfigureAwait(false)).ToList();
        var conflict = conflicts.FirstOrDefault(conflictInfo =>
            string.Equals(conflictInfo.FeatureId, plan.FeatureId, StringComparison.Ordinal));
        events.Add(Event("pull-replayed", DateTime.UtcNow, new
        {
            pullResult.IsSuccess,
            pullResult.ChangesPulled,
            conflictCount = conflicts.Count
        }));

        var resolutionApplied = false;
        if (conflict is not null)
        {
            resolutionApplied = plan.Resolution == ConflictResolution.Manual
                ? await _syncService.DeferConflictAsync(conflict.Id).ConfigureAwait(false)
                : await _syncService.ResolveConflictAsync(conflict.Id, plan.Resolution).ConfigureAwait(false);

            events.Add(Event("resolution-selected", DateTime.UtcNow, new
            {
                conflictId = conflict.Id,
                resolution = plan.Resolution.ToString(),
                applied = resolutionApplied
            }));
        }

        var finalFeature = await _storage.GetFeatureAsync(plan.FeatureId, plan.Layer.Id).ConfigureAwait(false);
        var diagnostics = await _storage.GetOfflineCacheDiagnosticsAsync().ConfigureAwait(false);
        var completedAtUtc = DateTime.UtcNow;
        var evidencePath = await WriteEvidenceAsync(
            plan,
            evidenceDirectory ?? _defaultEvidenceDirectory,
            startedAtUtc,
            completedAtUtc,
            events,
            pullResult,
            conflict,
            resolutionApplied,
            finalFeature,
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        return new LocalFieldConflictReplayResult
        {
            RunId = plan.RunId,
            EvidencePath = evidencePath,
            PullResult = pullResult,
            ResolutionApplied = resolutionApplied,
            Conflict = conflict,
            FinalFeature = finalFeature,
            Diagnostics = diagnostics
        };
    }

    public static ServerChange CreateServerUpdate(LocalFieldConflictReplayPlan plan)
    {
        return new ServerChange
        {
            FeatureId = plan.FeatureId,
            LayerId = plan.Layer.Id,
            Operation = StorageChangeOperation.Update,
            Version = plan.ServerVersion,
            Feature = CreateFeature(
                plan.FeatureId,
                plan.Layer.Id,
                plan.ServerVersion,
                plan.ServerAttributes)
        };
    }

    public static ServerChange CreateServerDelete(LocalFieldConflictReplayPlan plan)
    {
        return new ServerChange
        {
            FeatureId = plan.FeatureId,
            LayerId = plan.Layer.Id,
            Operation = StorageChangeOperation.Delete,
            Version = plan.ServerVersion,
            Feature = CreateFeature(
                plan.FeatureId,
                plan.Layer.Id,
                plan.ServerVersion,
                plan.ServerAttributes)
        };
    }

    private static async Task<string?> WriteEvidenceAsync(
        LocalFieldConflictReplayPlan plan,
        string? evidenceDirectory,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        IReadOnlyList<object> events,
        SyncResult pullResult,
        ConflictInfo? conflict,
        bool resolutionApplied,
        Feature? finalFeature,
        OfflineCacheDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            return null;
        }

        Directory.CreateDirectory(evidenceDirectory);
        var evidencePath = Path.Combine(evidenceDirectory, $"{SanitizePathSegment(plan.RunId)}.evidence.json");
        var evidence = new
        {
            schemaVersion = EvidenceSchemaVersion,
            runId = plan.RunId,
            noCloud = true,
            cloudUploadIncluded = false,
            startedAtUtc = FormatDateTime(startedAtUtc),
            completedAtUtc = FormatDateTime(completedAtUtc),
            layer = new
            {
                id = plan.Layer.Id,
                serviceId = plan.Layer.ServiceId,
                sourceId = plan.Layer.SourceId,
                name = plan.Layer.Name
            },
            featureId = plan.FeatureId,
            localVersion = plan.LocalVersion,
            serverVersion = plan.ServerVersion,
            selectedResolution = plan.Resolution.ToString(),
            resolutionApplied,
            pull = new
            {
                pullResult.IsSuccess,
                pullResult.ChangesPulled,
                pullResult.ConflictsDetected,
                errorMessage = DiagnosticRedactor.RedactSensitiveText(pullResult.ErrorMessage)
            },
            conflict = conflict is null ? null : SanitizeConflict(conflict),
            finalState = new
            {
                featureExists = finalFeature is not null,
                featureId = finalFeature?.Id,
                version = finalFeature?.Version,
                attributes = finalFeature is null ? null : SanitizeAttributes(finalFeature.Attributes),
                diagnostics.Operations.ConflictCount,
                diagnostics.Operations.PendingCount
            },
            events
        };

        await using var stream = new FileStream(evidencePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, evidence, JsonOptions, cancellationToken).ConfigureAwait(false);
        return evidencePath;
    }

    private static Feature CreateFeature(
        string featureId,
        int layerId,
        long version,
        IReadOnlyDictionary<string, object?> attributes)
    {
        return new Feature
        {
            Id = featureId,
            LayerId = layerId,
            Version = version,
            Geometry = new Point(21.3, -157.8),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ModifiedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Attributes = attributes.ToDictionary(
                attribute => attribute.Key,
                attribute => attribute.Value,
                StringComparer.Ordinal)
        };
    }

    private static object Event(string name, DateTime timestampUtc, object details)
    {
        return new
        {
            name,
            timestampUtc = FormatDateTime(timestampUtc),
            details
        };
    }

    private static object SanitizeConflict(ConflictInfo conflict)
    {
        return new
        {
            conflict.Id,
            conflict.OperationId,
            conflict.FeatureId,
            conflict.SourceId,
            type = conflict.Type.ToString(),
            detectedAtUtc = FormatDateTime(conflict.DetectedAt),
            conflict.FailureReason,
            localVersion = conflict.RedactedLocalVersion,
            serverVersion = conflict.RedactedServerVersion,
            availableResolutions = conflict.AvailableResolutions.Select(resolution => resolution.ToString()).ToList()
        };
    }

    private static Dictionary<string, object?> SanitizeAttributes(IReadOnlyDictionary<string, object?> attributes)
    {
        return attributes.ToDictionary(
            attribute => attribute.Key,
            attribute => SanitizeAttributeValue(attribute.Key, attribute.Value),
            StringComparer.Ordinal);
    }

    private static object? SanitizeAttributeValue(string name, object? value)
    {
        if (IsSensitiveName(name))
        {
            return "[redacted]";
        }

        return value is string text
            ? DiagnosticRedactor.RedactSensitiveText(text)
            : value;
    }

    private static bool IsSensitiveName(string name)
    {
        var normalized = string.Concat(name
            .Where(char.IsLetterOrDigit))
            .ToLowerInvariant();

        return normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("accesskey", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        sanitized = string.Join("_", sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "local-conflict-replay";
        }

        return sanitized.Length > 96
            ? sanitized[..96]
            : sanitized;
    }

    private static string FormatDateTime(DateTime value)
    {
        var dateTime = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

        return dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    }
}
