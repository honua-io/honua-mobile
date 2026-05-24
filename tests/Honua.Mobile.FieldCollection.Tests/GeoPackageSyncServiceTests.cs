using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Sync;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.Maui.Diagnostics;
using Honua.Sdk.Abstractions.Features;
using Microsoft.Extensions.Logging.Abstractions;
using SQLite;
using System.Text.Json;
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
    public async Task PushChangesAsync_WhenSessionRequiresReauthentication_LeavesPendingEditsQueued()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 1));
        var auth = new TestAuthenticationService
        {
            IsAuthenticated = false,
            RequiresReauthentication = true,
            SessionStatusMessage = "Session expired. Sign in again.",
            EnsureValidSessionResult = false
        };
        using var sync = CreateSyncService(storage, authService: auth);

        var result = await sync.PushChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("Session expired", result.ErrorMessage);
        Assert.Single(await storage.GetPendingChangesAsync());
        Assert.Equal(1, auth.EnsureValidSessionCalls);
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
    public async Task PushChangesAsync_WithHonuaUploader_UploadsSdkFeatureEditAndMarksChangeSynced()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();
        await storage.CreateLayerAsync(layer);
        var feature = CreateFeature("asset-1", version: 1);
        feature.Attributes["objectid"] = 42;
        await storage.StoreFeatureAsync(feature);
        var metadata = new FixedMetadataService([layer]);
        var client = new RecordingFeatureSyncClient
        {
            EditResponse = new FeatureEditResponse
            {
                ProviderName = "test",
                AddResults = [new FeatureEditResult { Succeeded = true, ObjectId = 42 }]
            }
        };
        using var sync = CreateSyncService(
            storage,
            uploader: new HonuaFieldCollectionChangeUploader(storage, metadata, client));

        var result = await sync.PushChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(await storage.GetPendingChangesAsync());
        var request = Assert.Single(client.EditRequests);
        Assert.Equal("assets", request.Source.ServiceId);
        Assert.Equal(1, request.Source.LayerId);
        var add = Assert.Single(request.Adds);
        Assert.Equal(42, add.ObjectId);
        Assert.Equal(-157.8, add.Geometry!.Value.GetProperty("x").GetDouble());
        Assert.Equal(21.3, add.Geometry!.Value.GetProperty("y").GetDouble());
    }

    [Fact]
    public async Task PushChangesAsync_WithHonuaUploaderDelete_UsesStoredObjectIdAfterLocalDelete()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();
        await storage.CreateLayerAsync(layer);
        var feature = CreateFeature("asset-1", version: 1);
        feature.Attributes["objectid"] = 42;
        await storage.StoreFeatureAsync(feature);
        var initialChanges = await storage.GetPendingChangesAsync();
        await storage.MarkChangesAsSynced(initialChanges.Select(change => change.Id).ToList());
        await storage.DeleteFeatureAsync("asset-1", 1);
        var metadata = new FixedMetadataService([layer]);
        var client = new RecordingFeatureSyncClient
        {
            EditResponse = new FeatureEditResponse
            {
                ProviderName = "test",
                DeleteResults = [new FeatureEditResult { Succeeded = true, ObjectId = 42 }]
            }
        };
        using var sync = CreateSyncService(
            storage,
            uploader: new HonuaFieldCollectionChangeUploader(storage, metadata, client));

        var result = await sync.PushChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(await storage.GetPendingChangesAsync());
        var request = Assert.Single(client.EditRequests);
        Assert.Empty(request.Adds);
        Assert.Empty(request.Updates);
        Assert.Equal([42], request.DeleteObjectIds);
    }

    [Fact]
    public async Task DeleteFeatureAsync_WhenOfflineInsertNeverSynced_CollapsesLocalOnlyLifecycle()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();
        await storage.CreateLayerAsync(layer);
        await storage.StoreFeatureAsync(CreateFeature("local-only", version: 1));
        var metadata = new FixedMetadataService([layer]);
        var client = new RecordingFeatureSyncClient();
        using var sync = CreateSyncService(
            storage,
            uploader: new HonuaFieldCollectionChangeUploader(storage, metadata, client));

        await storage.DeleteFeatureAsync("local-only", 1);
        var result = await sync.PushChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.ChangesPushed);
        Assert.Empty(await storage.GetPendingChangesAsync());
        Assert.Empty(client.EditRequests);
    }

    [Fact]
    public async Task PushChangesAsync_WithHonuaUploaderConflict_StoresConflictForReview()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();
        await storage.CreateLayerAsync(layer);
        var feature = CreateFeature("asset-1", version: 2);
        feature.Attributes["objectid"] = 42;
        await storage.StoreFeatureAsync(feature);
        var metadata = new FixedMetadataService([layer]);
        var client = new RecordingFeatureSyncClient
        {
            EditResponse = new FeatureEditResponse
            {
                ProviderName = "test",
                AddResults =
                [
                    new FeatureEditResult
                    {
                        Succeeded = false,
                        Error = new FeatureEditError { Code = 409, Message = "Version conflict" }
                    }
                ]
            }
        };
        using var sync = CreateSyncService(
            storage,
            uploader: new HonuaFieldCollectionChangeUploader(storage, metadata, client));

        var result = await sync.PushChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.Single(await storage.GetPendingChangesAsync());
        var conflict = Assert.Single(await sync.GetConflictsAsync());
        Assert.Equal("asset-1", conflict.FeatureId);
        Assert.Contains("Version conflict", conflict.RedactedServerVersion);
    }

    [Fact]
    public async Task PushChangesAsync_WhenCompleted_PersistsSyncHistory()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", version: 1));
        using var sync = CreateSyncService(storage, uploader: new FixedResultUploader(true));

        var result = await sync.PushChangesAsync();

        Assert.True(result.IsSuccess);
        var session = Assert.Single(await storage.GetSyncSessionsAsync());
        Assert.Equal(Honua.Mobile.FieldCollection.Services.Storage.Models.SyncSessionStatus.Completed, session.Status);
        Assert.Equal(1, session.ChangesPushed);
        Assert.NotNull(session.EndTime);
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
    public async Task PullChangesAsync_WithHonuaPuller_AppliesServerFeatureAndPersistsCursor()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();
        await storage.CreateLayerAsync(layer);
        var metadata = new FixedMetadataService([layer]);
        var settings = new InMemorySettingsService();
        var client = new RecordingFeatureSyncClient
        {
            QueryResponse = new FeatureQueryResult
            {
                ProviderName = "test",
                ObjectIdFieldName = "objectid",
                Features =
                [
                    new FeatureRecord
                    {
                        Id = "42",
                        Attributes = new Dictionary<string, JsonElement>
                        {
                            ["objectid"] = JsonSerializer.SerializeToElement(42),
                            ["sync_version"] = JsonSerializer.SerializeToElement(5L),
                            ["name"] = JsonSerializer.SerializeToElement("server")
                        },
                        Geometry = JsonSerializer.SerializeToElement(new { x = -157.8, y = 21.3 })
                    }
                ]
            }
        };
        var puller = new HonuaFieldCollectionChangePuller(metadata, client, settings);
        using var sync = CreateSyncService(storage, puller: puller);

        var result = await sync.PullChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.ChangesPulled);
        Assert.Equal(5, await puller.GetLastSyncedGenerationAsync());
        var feature = await storage.GetFeatureAsync("42", 1);
        Assert.NotNull(feature);
        Assert.Equal("server", feature.Attributes["name"]?.ToString());
    }

    [Fact]
    public void IsRemoteSyncConfigured_ReflectsDynamicTransportConfiguration()
    {
        var databasePath = CreateDatabasePath();
        using var storage = new GeoPackageStorageService(databasePath);
        var client = new RecordingFeatureSyncClient { IsConfigured = false };
        var metadata = new FixedMetadataService([CreateLayer()]);
        var settings = new InMemorySettingsService();
        using var sync = CreateSyncService(
            storage,
            uploader: new HonuaFieldCollectionChangeUploader(storage, metadata, client),
            puller: new HonuaFieldCollectionChangePuller(metadata, client, settings));

        Assert.False(sync.IsRemoteSyncConfigured);

        client.IsConfigured = true;

        Assert.True(sync.IsRemoteSyncConfigured);
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

    [Fact]
    public async Task LocalFieldConflictReplayHarness_WritesRedactedEvidenceAndAppliesResolution()
    {
        var databasePath = CreateDatabasePath();
        var evidenceDirectory = Path.Combine(Path.GetTempPath(), $"honua-conflict-replay-{Guid.NewGuid():N}");
        await using var cleanup = new DatabaseCleanup(databasePath, evidenceDirectory);
        using var storage = new GeoPackageStorageService(databasePath);
        var plan = new LocalFieldConflictReplayPlan
        {
            RunId = "local-conflict-replay-test",
            Layer = CreateLayer(),
            FeatureId = "asset-1",
            LocalAttributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = "local",
                ["apiKey"] = "secret-local"
            },
            ServerAttributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = "server",
                ["authorization"] = "Bearer secret-server"
            },
            Resolution = ConflictResolution.AcceptServer
        };
        var peer = new LocalReplayFieldSyncPeer([LocalFieldConflictReplayHarness.CreateServerUpdate(plan)]);
        using var sync = CreateSyncService(storage, uploader: peer, puller: peer, attachmentSynchronizer: peer);
        var harness = new LocalFieldConflictReplayHarness(storage, sync, evidenceDirectory);

        var result = await harness.RunAsync(plan);

        Assert.True(result.PullResult.IsSuccess);
        Assert.NotNull(result.Conflict);
        Assert.True(result.ResolutionApplied);
        Assert.NotNull(result.FinalFeature);
        Assert.Equal("server", result.FinalFeature.Attributes["name"]?.ToString());
        Assert.Equal(0, result.Diagnostics.Operations.ConflictCount);
        Assert.True(File.Exists(result.EvidencePath));

        var evidenceJson = await File.ReadAllTextAsync(result.EvidencePath);
        using var evidence = JsonDocument.Parse(evidenceJson);
        Assert.Equal(
            "honua.mobile.local-conflict-replay.evidence.v1",
            evidence.RootElement.GetProperty("schemaVersion").GetString());
        Assert.True(evidence.RootElement.GetProperty("noCloud").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("cloudUploadIncluded").GetBoolean());
        Assert.Equal("AcceptServer", evidence.RootElement.GetProperty("selectedResolution").GetString());
        Assert.True(evidence.RootElement.GetProperty("resolutionApplied").GetBoolean());
        Assert.Equal("UpdateUpdate", evidence.RootElement.GetProperty("conflict").GetProperty("type").GetString());
        Assert.True(evidence.RootElement.GetProperty("finalState").GetProperty("featureExists").GetBoolean());
        Assert.Equal(4, evidence.RootElement.GetProperty("events").GetArrayLength());
        Assert.DoesNotContain("secret-local", evidenceJson);
        Assert.DoesNotContain("secret-server", evidenceJson);
    }

    [Fact]
    public async Task LocalReplayFieldSyncPeer_WhenAttachmentUploadFails_ReturnsRetryableFailure()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new DatabaseCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreAttachmentMetadataAsync(new AttachmentInfo
        {
            Id = "attachment-retry-1",
            LayerId = 1,
            FeatureId = "asset-1",
            FileName = "retry-photo.jpg",
            ContentType = "image/jpeg",
            PayloadKind = AttachmentPayloadKind.Photo,
            SizeBytes = 128,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTime.UtcNow,
            SyncStatus = AttachmentSyncStatus.PendingUpload
        });
        var peer = new LocalReplayFieldSyncPeer
        {
            PushAttachmentResult = new AttachmentSyncResult { Failed = 1 }
        };
        using var sync = CreateSyncService(storage, uploader: peer, puller: peer, attachmentSynchronizer: peer);

        var result = await sync.PushChangesAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("failed to upload", result.ErrorMessage, StringComparison.Ordinal);
        var diagnostics = await storage.GetOfflineCacheDiagnosticsAsync();
        Assert.Equal(1, diagnostics.Operations.AttachmentPendingCount);
    }

    private static GeoPackageSyncService CreateSyncService(
        GeoPackageStorageService storage,
        IFieldCollectionChangeUploader? uploader = null,
        IFieldCollectionChangePuller? puller = null,
        IFieldCollectionAttachmentSynchronizer? attachmentSynchronizer = null,
        IAuthenticationService? authService = null,
        IMobileExceptionReporter? exceptionReporter = null)
    {
        return new GeoPackageSyncService(
            storage,
            authService ?? new TestAuthenticationService(),
            new TestConnectivityService(),
            uploader ?? new FixedResultUploader(true),
            puller ?? new FixedPuller([]),
            attachmentSynchronizer,
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

    private static LayerInfo CreateLayer()
    {
        return new LayerInfo
        {
            Id = 1,
            ServiceId = "assets",
            SourceId = "assets/FeatureServer/1",
            Name = "Assets",
            GeometryType = GeometryType.Point,
            IsEditable = true
        };
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

    private sealed class RecordingFeatureSyncClient : IFieldCollectionFeatureSyncClient
    {
        public bool IsConfigured { get; set; } = true;
        public FeatureEditResponse? EditResponse { get; set; }
        public FeatureQueryResult? QueryResponse { get; set; }
        public List<FeatureEditRequest> EditRequests { get; } = [];
        public List<FeatureQueryRequest> QueryRequests { get; } = [];

        public Task<FeatureEditResponse> ApplyEditsAsync(
            FeatureEditRequest request,
            CancellationToken cancellationToken = default)
        {
            EditRequests.Add(request);
            return Task.FromResult(EditResponse ?? new FeatureEditResponse
            {
                ProviderName = "test",
                UpdateResults = [new FeatureEditResult { Succeeded = true }]
            });
        }

        public Task<FeatureQueryResult> QueryAsync(
            FeatureQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            QueryRequests.Add(request);
            return Task.FromResult(QueryResponse ?? new FeatureQueryResult
            {
                ProviderName = "test",
                Features = []
            });
        }
    }

    private sealed class FixedMetadataService : IFieldCollectionMetadataService
    {
        private readonly IReadOnlyList<LayerInfo> _layers;
        private string _selectedServiceId;

        public FixedMetadataService(IReadOnlyList<LayerInfo> layers)
        {
            _layers = layers;
            _selectedServiceId = layers.FirstOrDefault()?.ServiceId ?? "assets";
        }

        public Task<IReadOnlyList<FieldProjectInfo>> GetProjectsAsync(
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FieldProjectInfo>>(
            [
                new FieldProjectInfo
                {
                    ServiceId = _selectedServiceId,
                    Name = _selectedServiceId,
                    LayerCount = _layers.Count,
                    Layers = _layers.ToList()
                }
            ]);
        }

        public Task<FieldProjectInfo?> GetSelectedProjectAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<FieldProjectInfo?>(new FieldProjectInfo
            {
                ServiceId = _selectedServiceId,
                Name = _selectedServiceId,
                LayerCount = _layers.Count,
                Layers = _layers.ToList()
            });
        }

        public Task SelectProjectAsync(string serviceId, CancellationToken cancellationToken = default)
        {
            _selectedServiceId = serviceId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LayerInfo>> GetLayersAsync(
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_layers);
        }
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
        {
            return Task.FromResult(
                _values.TryGetValue(key, out var value) && value is T typed
                    ? typed
                    : defaultValue);
        }

        public Task SetSettingAsync<T>(string key, T value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveSettingAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> HasSettingAsync(string key)
        {
            return Task.FromResult(_values.ContainsKey(key));
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
        private readonly string[] _paths;

        public DatabaseCleanup(params string[] paths)
        {
            _paths = paths;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var path in _paths)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                if (File.Exists($"{path}-wal"))
                {
                    File.Delete($"{path}-wal");
                }

                if (File.Exists($"{path}-shm"))
                {
                    File.Delete($"{path}-shm");
                }

                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
