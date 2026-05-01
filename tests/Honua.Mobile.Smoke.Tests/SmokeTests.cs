using System.Diagnostics;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Models;

namespace Honua.Mobile.Smoke.Tests;

/// <summary>
/// Reliability smoke tests that verify critical paths in the Honua Mobile offline stack.
/// These are intentionally lightweight and run without external dependencies so they can
/// gate every CI build.
/// </summary>
public sealed class SmokeTests : IDisposable
{
    private readonly string _databasePath;

    public SmokeTests()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"honua-smoke-{Guid.NewGuid():N}.gpkg");
    }

    // ---------------------------------------------------------------
    // 1. GeoPackage store initializes without error
    // ---------------------------------------------------------------

    [Fact]
    public async Task GeoPackageStore_Initializes_WithoutError()
    {
        var store = CreateStore();

        await store.InitializeAsync();

        // A second initialization should also succeed (idempotent).
        await store.InitializeAsync();
    }

    // ---------------------------------------------------------------
    // 2. Sync engine handles empty queue gracefully
    // ---------------------------------------------------------------

    [Fact]
    public async Task SyncEngine_EmptyQueue_ReturnsZeroCounts()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var uploader = new NoOpUploader();
        var engine = new OfflineSyncEngine(store, uploader);

        var result = await engine.SyncAsync();

        Assert.Equal(0, result.Loaded);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Empty(result.Failures);
    }

    // ---------------------------------------------------------------
    // 3. Offline edit operations round-trip
    //    (enqueue -> get pending -> mark succeeded)
    // ---------------------------------------------------------------

    [Fact]
    public async Task OfflineEdit_RoundTrip_EnqueueGetPendingMarkSucceeded()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var operation = new OfflineEditOperation
        {
            OperationId = "smoke-roundtrip-1",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = "{\"name\":\"hydrant\"}",
            Priority = 1,
        };

        await store.EnqueueAsync(operation);

        var pending = await store.GetPendingAsync(10);
        Assert.Single(pending);
        Assert.Equal("smoke-roundtrip-1", pending[0].OperationId);
        Assert.Equal("assets", pending[0].LayerKey);
        Assert.Equal(OfflineOperationType.Add, pending[0].OperationType);

        await store.MarkSucceededAsync("smoke-roundtrip-1");

        var remaining = await store.CountPendingAsync();
        Assert.Equal(0, remaining);
    }

    // ---------------------------------------------------------------
    // 4. Delta download engine handles missing replica gracefully
    //    When no replica cursor exists the engine creates one. We
    //    verify this path with a fake client that returns an empty
    //    change set.
    // ---------------------------------------------------------------

    [Fact]
    public async Task DeltaDownloadEngine_MissingReplica_CreatesAndDownloads()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        var fakeClient = new FakeReplicaSyncClient();
        var engine = new DeltaDownloadEngine(store, fakeClient);

        var result = await engine.DownloadAsync("service-1");

        Assert.Equal(0, result.Adds);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);

        // The engine should have persisted the replica cursor.
        var cursor = await store.GetSyncCursorAsync("replica:service-1");
        Assert.NotNull(cursor);
        Assert.Equal(fakeClient.CreatedReplicaId, cursor);
    }

    // ---------------------------------------------------------------
    // 5. Background sync orchestrator starts and stops cleanly
    // ---------------------------------------------------------------

    [Fact]
    public async Task BackgroundSyncOrchestrator_StartStop_CompletesCleanly()
    {
        var runner = new NoOpSyncRunner();
        var connectivity = new AlwaysOnlineConnectivityStateProvider();
        await using var orchestrator = new BackgroundSyncOrchestrator(
            runner,
            connectivity,
            new BackgroundSyncOrchestratorOptions
            {
                SyncInterval = TimeSpan.FromMilliseconds(50),
                RunImmediately = true,
            });

        await orchestrator.StartAsync();
        Assert.True(orchestrator.IsRunning);

        // Let at least one cycle execute.
        await Task.Delay(120);
        await orchestrator.StopAsync();

        Assert.False(orchestrator.IsRunning);
        Assert.True(runner.CallCount >= 1, "Expected at least one sync cycle to run.");
    }

    // ---------------------------------------------------------------
    // 6. Sync cursor persistence round-trips
    // ---------------------------------------------------------------

    [Fact]
    public async Task SyncCursor_Persistence_RoundTrips()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await store.SetSyncCursorAsync("servergen:svc-1", "42");
        var value = await store.GetSyncCursorAsync("servergen:svc-1");
        Assert.Equal("42", value);

        // Overwrite and verify the update is persisted.
        await store.SetSyncCursorAsync("servergen:svc-1", "99");
        var updated = await store.GetSyncCursorAsync("servergen:svc-1");
        Assert.Equal("99", updated);

        // A cursor that was never set should return null.
        var missing = await store.GetSyncCursorAsync("nonexistent-key");
        Assert.Null(missing);
    }

    // ---------------------------------------------------------------
    // 7. Optional live server smoke for platform CI
    // ---------------------------------------------------------------

    [Fact]
    public async Task LiveFeatureQuery_WhenConfigured_CompletesUnderOneSecond()
    {
        var baseUrl = Environment.GetEnvironmentVariable("HONUA_MOBILE_SMOKE_BASE_URL");
        var serviceId = Environment.GetEnvironmentVariable("HONUA_MOBILE_SMOKE_SERVICE_ID");
        var layerIdValue = Environment.GetEnvironmentVariable("HONUA_MOBILE_SMOKE_LAYER_ID");
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            string.IsNullOrWhiteSpace(serviceId) ||
            !int.TryParse(layerIdValue, out var layerId))
        {
            return;
        }

        using var http = new HttpClient();
        var client = new HonuaMobileClient(http, new HonuaMobileClientOptions
        {
            BaseUri = new Uri(baseUrl),
            ApiKey = Environment.GetEnvironmentVariable("HONUA_MOBILE_SMOKE_API_KEY"),
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var stopwatch = Stopwatch.StartNew();
        using var result = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = serviceId,
            LayerId = layerId,
            ResultRecordCount = 1,
        }, timeout.Token);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Live query took {stopwatch.Elapsed}.");
        Assert.True(result.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private GeoPackageSyncStore CreateStore()
    {
        return new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = _databasePath,
        });
    }

    // -- Fakes -----------------------------------------------------

    private sealed class NoOpUploader : IOfflineOperationUploader
    {
        public Task<UploadResult> UploadAsync(
            OfflineEditOperation operation,
            bool forceWrite,
            CancellationToken ct = default)
        {
            return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success });
        }
    }

    private sealed class NoOpSyncRunner : IOfflineSyncRunner
    {
        public int CallCount { get; private set; }

        public Task<SyncRunResult> SyncAsync(CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new SyncRunResult
            {
                Loaded = 0,
                Succeeded = 0,
                Failed = 0,
            });
        }
    }

    private sealed class FakeReplicaSyncClient : IReplicaSyncClient
    {
        public string CreatedReplicaId { get; } = Guid.NewGuid().ToString("N");

        public Task<CreateReplicaResult> CreateReplicaAsync(
            string serviceId,
            string replicaName,
            int[]? layerIds = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(new CreateReplicaResult(CreatedReplicaId, ServerGen: 1));
        }

        public Task<ExtractChangesResult> ExtractChangesAsync(
            string serviceId,
            string replicaId,
            CancellationToken ct = default)
        {
            return Task.FromResult(new ExtractChangesResult
            {
                LayerChanges = [],
                ServerGen = 2,
            });
        }

        public Task<SynchronizeResult> SynchronizeReplicaAsync(
            string serviceId,
            string replicaId,
            string syncDirection = "download",
            CancellationToken ct = default)
        {
            return Task.FromResult(new SynchronizeResult(replicaId, ServerGen: 2));
        }

        public Task UnRegisterReplicaAsync(
            string serviceId,
            string replicaId,
            CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
