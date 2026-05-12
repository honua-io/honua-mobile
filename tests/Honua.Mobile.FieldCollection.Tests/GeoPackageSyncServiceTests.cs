using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Sync;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.Maui.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SQLite;
using StorageChangeRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeRecord;
using StorageChangeOperation = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeOperation;
using StorageConflictRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictRecord;
using StorageConflictType = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictType;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class GeoPackageSyncServiceTests
{
    public GeoPackageSyncServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task PushChangesAsync_WhenUploaderReturnsFalse_ReturnsFailureAndLeavesChangePending()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 1));
        using var sync = CreateSyncService(storage, uploader: new FixedResultUploader(false));

        var result = await sync.PushChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("failed to upload", result.ErrorMessage);
        Assert.Single(await storage.GetPendingChangesAsync());
    }

    [Fact]
    public async Task PushChangesAsync_WhenUsingQueuedProductionUploader_ReturnsConfigurationFailureAndLeavesChangePending()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 1));
        using var sync = CreateSyncService(
            storage,
            uploader: new QueuedFieldCollectionChangeUploader(
                NullLogger<QueuedFieldCollectionChangeUploader>.Instance));

        var result = await sync.PushChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.False(sync.IsRemoteSyncConfigured);
        Assert.Contains("remote field sync is not configured", result.ErrorMessage);
        Assert.Single(await storage.GetPendingChangesAsync());
    }

    [Fact]
    public async Task PullChangesAsync_WhenUsingLocalOnlyProductionPuller_ReturnsConfigurationFailure()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        using var sync = CreateSyncService(
            storage,
            puller: new LocalOnlyFieldCollectionChangePuller(
                NullLogger<LocalOnlyFieldCollectionChangePuller>.Instance));

        var result = await sync.PullChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.False(sync.IsRemoteSyncConfigured);
        Assert.Contains("remote field sync is not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task PushChangesAsync_WhenHandledFailureOccurs_ReportsSyncExceptionContext()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 1));
        var reporter = new RecordingExceptionReporter();
        using var sync = CreateSyncService(
            storage,
            uploader: new FixedResultUploader(false),
            exceptionReporter: reporter);

        var result = await sync.PushChangesAsync();

        Assert.False(result.IsSuccess);
        var report = Assert.Single(reporter.Reports);
        var context = AssertSyncReportContext(report, "sync.push");
        Assert.Equal("PushingChanges", context.Properties["status"]);
        Assert.Equal("Active", context.Properties["sessionStatus"]);
        Assert.Equal(0, context.Properties["changesPushed"]);
        Assert.Equal(0, context.Properties["conflictsDetected"]);
    }

    [Fact]
    public async Task PushChangesAsync_WhenCanceled_DoesNotReportSyncException()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 1));
        var reporter = new RecordingExceptionReporter();
        using var sync = CreateSyncService(
            storage,
            uploader: new CancelingUploader(),
            exceptionReporter: reporter);

        var result = await sync.PushChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Push was cancelled", result.ErrorMessage);
        Assert.Empty(reporter.Reports);
    }

    [Fact]
    public async Task PushChangesAsync_WhenUploaderReturnsTrue_MarksChangeSynced()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 1));
        using var sync = CreateSyncService(storage, uploader: new FixedResultUploader(true));

        var result = await sync.PushChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ChangesPushed);
        Assert.Empty(await storage.GetPendingChangesAsync());
    }

    [Fact]
    public async Task PullChangesAsync_WhenApplyFails_ReturnsFailureAndDoesNotCountPulledChange()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var serverChange = new ServerChange
        {
            FeatureId = "asset-1",
            LayerId = 1,
            Operation = StorageChangeOperation.Update,
            Version = 1,
            Feature = CreateFeature("asset-1", version: 1, geometry: new UnsupportedGeometry())
        };
        using var sync = CreateSyncService(storage, puller: new FixedPuller([serverChange]));

        var result = await sync.PullChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.ChangesPulled);
    }

    [Fact]
    public async Task PullChangesAsync_WhenHandledFailureOccurs_ReportsSyncExceptionContext()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var serverChange = new ServerChange
        {
            FeatureId = "asset-1",
            LayerId = 1,
            Operation = StorageChangeOperation.Update,
            Version = 1,
            Feature = CreateFeature("asset-1", version: 1, geometry: new UnsupportedGeometry())
        };
        var reporter = new RecordingExceptionReporter();
        using var sync = CreateSyncService(
            storage,
            puller: new FixedPuller([serverChange]),
            exceptionReporter: reporter);

        var result = await sync.PullChangesAsync();

        Assert.False(result.IsSuccess);
        var report = Assert.Single(reporter.Reports);
        var context = AssertSyncReportContext(report, "sync.pull");
        Assert.Equal("PullingChanges", context.Properties["status"]);
        Assert.Equal("Active", context.Properties["sessionStatus"]);
        Assert.Equal(0, context.Properties["changesPulled"]);
        Assert.Equal(0, context.Properties["conflictsDetected"]);
    }

    [Fact]
    public async Task PullChangesAsync_WhenCanceled_DoesNotReportSyncException()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var reporter = new RecordingExceptionReporter();
        using var sync = CreateSyncService(
            storage,
            puller: new CancelingPuller(),
            exceptionReporter: reporter);

        var result = await sync.PullChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Pull was cancelled", result.ErrorMessage);
        Assert.Empty(reporter.Reports);
    }

    [Fact]
    public async Task SyncAsync_WhenHandledFailureOccurs_ReportsFullSyncExceptionContext()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var serverChange = new ServerChange
        {
            FeatureId = "asset-1",
            LayerId = 1,
            Operation = StorageChangeOperation.Update,
            Version = 1,
            Feature = CreateFeature("asset-1", version: 1, geometry: new UnsupportedGeometry())
        };
        var reporter = new RecordingExceptionReporter();
        using var sync = CreateSyncService(
            storage,
            puller: new FixedPuller([serverChange]),
            exceptionReporter: reporter);

        var result = await sync.SyncAsync();

        Assert.False(result.IsSuccess);
        var report = Assert.Single(reporter.Reports);
        var context = AssertSyncReportContext(report, "sync.full");
        Assert.Equal("Error", context.Properties["status"]);
        Assert.Equal("Failed", context.Properties["sessionStatus"]);
        Assert.Equal(0, context.Properties["changesPulled"]);
        Assert.Equal(0, context.Properties["changesPushed"]);
    }

    [Fact]
    public async Task PullChangesAsync_WhenLocalVersionIsNewer_StoresResolvableConflict()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 2));
        var serverFeature = CreateFeature("asset-1", version: 1);
        serverFeature.Attributes["name"] = "server";
        var serverChange = new ServerChange
        {
            FeatureId = "asset-1",
            LayerId = 1,
            Operation = StorageChangeOperation.Update,
            Version = 1,
            Feature = serverFeature
        };
        using var sync = CreateSyncService(storage, puller: new FixedPuller([serverChange]));

        var result = await sync.PullChangesAsync();
        var conflicts = (await sync.GetConflictsAsync()).ToList();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ChangesPulled);
        var conflict = Assert.Single(conflicts);
        Assert.Equal("asset-1", conflict.FeatureId);

        Assert.True(await sync.ResolveConflictAsync(conflict.Id, ConflictResolution.AcceptServer));
        Assert.Empty(await sync.GetConflictsAsync());
        var resolvedFeature = await storage.GetFeatureAsync("asset-1", 1);
        Assert.NotNull(resolvedFeature);
        Assert.Equal("server", resolvedFeature.Attributes["name"]?.ToString());
    }

    [Fact]
    public async Task StoreFeatureAsync_WhenChangeJournalInsertFails_RollsBackFeatureWrite()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.InitializeAsync();
        InstallFailingChangeRecordInsertTrigger(databasePath);

        await Assert.ThrowsAsync<SQLiteException>(() =>
            storage.StoreFeatureAsync(CreateFeature("asset-rollback", version: 1)));

        Assert.Null(await storage.GetFeatureAsync("asset-rollback", 1));
        Assert.Empty(await storage.GetPendingChangesAsync());
    }

    [Fact]
    public async Task DeleteFeatureAsync_WhenChangeJournalInsertFails_RollsBackFeatureDelete()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-delete", version: 1));
        var pendingChanges = await storage.GetPendingChangesAsync();
        await storage.MarkChangesAsSynced(pendingChanges.Select(change => change.Id).ToList());
        InstallFailingChangeRecordInsertTrigger(databasePath);

        await Assert.ThrowsAsync<SQLiteException>(() => storage.DeleteFeatureAsync("asset-delete", 1));

        Assert.NotNull(await storage.GetFeatureAsync("asset-delete", 1));
        Assert.Empty(await storage.GetPendingChangesAsync());
    }

    [Fact]
    public async Task GetOfflineCacheDiagnosticsAsync_SeparatesMetadataFeatureAndOperationState()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.CreateLayerAsync(new LayerInfo
        {
            Id = 1,
            Name = "Assets",
            GeometryType = GeometryType.Point,
            IsEditable = true
        });
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 1));
        var pendingChanges = await storage.GetPendingChangesAsync();
        await storage.MarkChangesAsSynced([pendingChanges[0].Id]);
        await storage.StoreConflictAsync(new StorageConflictRecord
        {
            Id = "conflict-1",
            FeatureId = "asset-1",
            LayerId = 1,
            ConflictType = StorageConflictType.UpdateUpdate,
            LocalVersion = 2,
            ServerVersion = 3,
            LocalData = """{"attributes":{"name":"local","apiKey":"secret-local"}}""",
            ServerData = """{"attributes":{"name":"server","authorization":"Bearer secret-token"}}""",
            CreatedAt = DateTime.UtcNow
        });

        var diagnostics = await storage.GetOfflineCacheDiagnosticsAsync();

        Assert.Equal("honua-field-tests-", diagnostics.PackageId[..18]);
        Assert.Equal("Available", diagnostics.MetadataCache.Status);
        Assert.Equal("Available", diagnostics.FeatureCache.Status);
        Assert.Equal(1, diagnostics.MetadataCache.SourceCount);
        Assert.Equal(1, diagnostics.FeatureCache.TotalFeatureCount);
        Assert.Equal(1, diagnostics.Operations.SucceededCount);
        Assert.Equal(1, diagnostics.Operations.ConflictCount);
        var conflict = Assert.Single(diagnostics.ConflictReview);
        Assert.Equal("1", conflict.SourceId);
        Assert.Contains("[redacted]", conflict.LocalState);
        Assert.Contains("[redacted]", conflict.ServerState);
        Assert.DoesNotContain("secret-token", conflict.ServerState);
    }

    [Fact]
    public async Task DeferConflictAsync_MarksManualReviewAndKeepsConflictVisible()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreConflictAsync(new StorageConflictRecord
        {
            Id = "conflict-1",
            FeatureId = "asset-1",
            LayerId = 1,
            ConflictType = StorageConflictType.UpdateUpdate,
            LocalVersion = 2,
            ServerVersion = 3,
            LocalData = """{"attributes":{"name":"local"}}""",
            ServerData = """{"attributes":{"name":"server"}}""",
            CreatedAt = DateTime.UtcNow
        });
        using var sync = CreateSyncService(storage);

        Assert.True(await sync.DeferConflictAsync("conflict-1"));

        var stored = await storage.GetConflictAsync("conflict-1");
        Assert.NotNull(stored);
        Assert.Equal(
            Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictResolution.Manual,
            stored.Resolution);
        Assert.Null(stored.ResolvedAt);
        Assert.Single(await sync.GetConflictsAsync());

        var diagnostics = await storage.GetOfflineCacheDiagnosticsAsync();
        var conflict = Assert.Single(diagnostics.ConflictReview);
        Assert.Equal("Deferred", conflict.Status);
        Assert.Equal(1, diagnostics.Operations.ConflictCount);
    }

    private static GeoPackageSyncService CreateSyncService(
        GeoPackageStorageService storage,
        IFieldCollectionChangeUploader? uploader = null,
        IFieldCollectionChangePuller? puller = null,
        IMobileExceptionReporter? exceptionReporter = null)
    {
        return new GeoPackageSyncService(
            storage,
            new TestAuthenticationService(),
            new TestConnectivityService(),
            uploader ?? new FixedResultUploader(true),
            puller ?? new FixedPuller([]),
            exceptionReporter: exceptionReporter);
    }

    private static MobileExceptionReportContext AssertSyncReportContext(
        ReportedException report,
        string expectedOperation)
    {
        var context = Assert.IsType<MobileExceptionReportContext>(report.Context);
        Assert.Equal("FieldCollection.Sync", context.Source);
        Assert.Equal(expectedOperation, context.Operation);
        Assert.Equal(MobileExceptionSeverity.Error, context.Severity);
        Assert.False(string.IsNullOrWhiteSpace(context.CorrelationId));
        Assert.True(context.Properties.ContainsKey("pendingChangesCount"));
        return context;
    }

    private static Feature CreateFeature(
        string id,
        long version,
        Geometry? geometry = null)
    {
        return new Feature
        {
            Id = id,
            LayerId = 1,
            Version = version,
            Geometry = geometry ?? new Point(21.3, -157.8),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ModifiedAt = DateTime.UtcNow,
            Attributes = new Dictionary<string, object?>
            {
                ["name"] = "local"
            }
        };
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"honua-field-tests-{Guid.NewGuid():N}.gpkg");
    }

    private static void InstallFailingChangeRecordInsertTrigger(string databasePath)
    {
        using var connection = new SQLiteConnection(databasePath, SQLiteOpenFlags.ReadWrite);
        connection.Execute(
            """
            CREATE TRIGGER fail_change_record_insert
            BEFORE INSERT ON change_records
            BEGIN
                SELECT RAISE(FAIL, 'change journal insert failed');
            END;
            """);
    }

    private sealed class FixedResultUploader : IFieldCollectionChangeUploader
    {
        private readonly bool _result;

        public FixedResultUploader(bool result)
        {
            _result = result;
        }

        public Task<bool> UploadChangeAsync(
            StorageChangeRecord change,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class CancelingUploader : IFieldCollectionChangeUploader
    {
        public Task<bool> UploadChangeAsync(
            StorageChangeRecord change,
            CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException("Push canceled.");
        }
    }

    private sealed class FixedPuller : IFieldCollectionChangePuller
    {
        private readonly IReadOnlyList<ServerChange> _changes;

        public FixedPuller(IReadOnlyList<ServerChange> changes)
        {
            _changes = changes;
        }

        public Task<IReadOnlyList<ServerChange>> GetChangesAsync(
            long sinceGeneration,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_changes);
        }

        public Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1L);
        }

        public Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }
    }

    private sealed class CancelingPuller : IFieldCollectionChangePuller
    {
        public Task<IReadOnlyList<ServerChange>> GetChangesAsync(
            long sinceGeneration,
            CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException("Pull canceled.");
        }

        public Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1L);
        }

        public Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }
    }

    private sealed class RecordingExceptionReporter : IMobileExceptionReporter
    {
        public List<ReportedException> Reports { get; } = [];

        public Task ReportAsync(
            Exception exception,
            MobileExceptionReportContext? context = null,
            CancellationToken cancellationToken = default)
        {
            Reports.Add(new ReportedException(exception, context));
            return Task.CompletedTask;
        }
    }

    private sealed record ReportedException(Exception Exception, MobileExceptionReportContext? Context);

    private sealed class UnsupportedGeometry : Geometry
    {
        public override string Type => "Unsupported";
    }

    private sealed class DatabaseCleanup : IAsyncDisposable
    {
        private readonly string _databasePath;

        public DatabaseCleanup(string databasePath)
        {
            _databasePath = databasePath;
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }

            return ValueTask.CompletedTask;
        }
    }
}
