using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;

namespace Honua.Mobile.Offline.Tests;

public sealed class DeltaDownloadEngineTests : IDisposable
{
    private readonly string _databasePath;

    public DeltaDownloadEngineTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-delta-{Guid.NewGuid():N}.gpkg");
    }

    [Fact]
    public async Task DownloadAsync_AddsFeatures_PersistsToStore()
    {
        var store = CreateStore();
        var replicaClient = new FakeReplicaSyncClient
        {
            CreateReplicaResponse = new CreateReplicaResult("replica-1", 10),
            ExtractChangesResponse = new ExtractChangesResult
            {
                ServerGen = 15,
                LayerChanges =
                [
                    new LayerChangeSet
                    {
                        LayerId = 0,
                        AddFeaturesJson =
                        [
                            """{"attributes":{"objectid":1,"name":"Feature A"}}""",
                            """{"attributes":{"objectid":2,"name":"Feature B"}}""",
                        ],
                    },
                ],
            },
            SynchronizeResponse = new SynchronizeResult("replica-1", 15),
        };

        var engine = new DeltaDownloadEngine(store, replicaClient);
        var result = await engine.DownloadAsync("assets");

        Assert.Equal(2, result.Adds);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);
        Assert.Equal(15, result.ServerGen);

        var features = await store.GetFeaturesAsync("0");
        Assert.Equal(2, features.Count);
        Assert.Contains("Feature A", features[0]);
        Assert.Contains("Feature B", features[1]);
    }

    [Fact]
    public async Task DownloadAsync_DeletesFeatures_RemovesFromStore()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await store.UpsertFeatureAsync("0", """{"attributes":{"objectid":10,"name":"To Delete"}}""");
        await store.UpsertFeatureAsync("0", """{"attributes":{"objectid":20,"name":"To Keep"}}""");

        var replicaClient = new FakeReplicaSyncClient
        {
            ExtractChangesResponse = new ExtractChangesResult
            {
                ServerGen = 25,
                LayerChanges =
                [
                    new LayerChangeSet
                    {
                        LayerId = 0,
                        DeleteIds = [10],
                    },
                ],
            },
            SynchronizeResponse = new SynchronizeResult("replica-2", 25),
        };

        await store.SetSyncCursorAsync("replica:assets", "replica-2");

        var engine = new DeltaDownloadEngine(store, replicaClient);
        var result = await engine.DownloadAsync("assets");

        Assert.Equal(0, result.Adds);
        Assert.Equal(0, result.Updates);
        Assert.Equal(1, result.Deletes);

        var features = await store.GetFeaturesAsync("0");
        Assert.Single(features);
        Assert.Contains("To Keep", features[0]);
    }

    [Fact]
    public async Task DownloadAsync_PersistsCursor_AfterDownload()
    {
        var store = CreateStore();
        var replicaClient = new FakeReplicaSyncClient
        {
            CreateReplicaResponse = new CreateReplicaResult("replica-cursor-test", 10),
            ExtractChangesResponse = new ExtractChangesResult
            {
                ServerGen = 30,
                LayerChanges = [],
            },
            SynchronizeResponse = new SynchronizeResult("replica-cursor-test", 30),
        };

        var engine = new DeltaDownloadEngine(store, replicaClient);
        await engine.DownloadAsync("myservice");

        var replicaId = await store.GetSyncCursorAsync("replica:myservice");
        var serverGen = await store.GetSyncCursorAsync("servergen:myservice");

        Assert.Equal("replica-cursor-test", replicaId);
        Assert.Equal("30", serverGen);
    }

    [Fact]
    public async Task DownloadAsync_SecondCall_ReusesExistingReplica()
    {
        var store = CreateStore();
        var replicaClient = new FakeReplicaSyncClient
        {
            CreateReplicaResponse = new CreateReplicaResult("replica-reuse", 10),
            ExtractChangesResponse = new ExtractChangesResult
            {
                ServerGen = 20,
                LayerChanges = [],
            },
            SynchronizeResponse = new SynchronizeResult("replica-reuse", 20),
        };

        var engine = new DeltaDownloadEngine(store, replicaClient);

        await engine.DownloadAsync("svc");
        Assert.Equal(1, replicaClient.CreateReplicaCallCount);

        replicaClient.ExtractChangesResponse = new ExtractChangesResult
        {
            ServerGen = 30,
            LayerChanges = [],
        };
        replicaClient.SynchronizeResponse = new SynchronizeResult("replica-reuse", 30);

        await engine.DownloadAsync("svc");
        Assert.Equal(1, replicaClient.CreateReplicaCallCount);
        Assert.Equal(2, replicaClient.ExtractChangesCallCount);
    }

    [Fact]
    public async Task DownloadAsync_EmptyChangeSet_ReturnsZeroCounts()
    {
        var store = CreateStore();
        var replicaClient = new FakeReplicaSyncClient
        {
            CreateReplicaResponse = new CreateReplicaResult("replica-empty", 5),
            ExtractChangesResponse = new ExtractChangesResult
            {
                ServerGen = 5,
                LayerChanges = [],
            },
            SynchronizeResponse = new SynchronizeResult("replica-empty", 5),
        };

        var engine = new DeltaDownloadEngine(store, replicaClient);
        var result = await engine.DownloadAsync("assets");

        Assert.Equal(0, result.Adds);
        Assert.Equal(0, result.Updates);
        Assert.Equal(0, result.Deletes);
        Assert.Equal(5, result.ServerGen);
    }

    [Fact]
    public async Task DownloadAsync_MixedAddsUpdatesDeletes_CountsAllCorrectly()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await store.UpsertFeatureAsync("0", """{"attributes":{"objectid":100,"name":"Existing"}}""");

        var replicaClient = new FakeReplicaSyncClient
        {
            CreateReplicaResponse = new CreateReplicaResult("replica-mixed", 10),
            ExtractChangesResponse = new ExtractChangesResult
            {
                ServerGen = 50,
                LayerChanges =
                [
                    new LayerChangeSet
                    {
                        LayerId = 0,
                        AddFeaturesJson =
                        [
                            """{"attributes":{"objectid":101,"name":"Added"}}""",
                        ],
                        UpdateFeaturesJson =
                        [
                            """{"attributes":{"objectid":100,"name":"Updated"}}""",
                        ],
                        DeleteIds = [99],
                    },
                ],
            },
            SynchronizeResponse = new SynchronizeResult("replica-mixed", 50),
        };

        var engine = new DeltaDownloadEngine(store, replicaClient);
        var result = await engine.DownloadAsync("assets");

        Assert.Equal(1, result.Adds);
        Assert.Equal(1, result.Updates);
        Assert.Equal(1, result.Deletes);
        Assert.Equal(50, result.ServerGen);

        var features = await store.GetFeaturesAsync("0");
        Assert.Equal(2, features.Count);
        Assert.Contains("Updated", features[0]);
        Assert.Contains("Added", features[1]);
    }

    [Fact]
    public async Task DownloadAsync_MultipleLayerChanges_ProcessesAllLayers()
    {
        var store = CreateStore();
        var replicaClient = new FakeReplicaSyncClient
        {
            CreateReplicaResponse = new CreateReplicaResult("replica-multi", 10),
            ExtractChangesResponse = new ExtractChangesResult
            {
                ServerGen = 40,
                LayerChanges =
                [
                    new LayerChangeSet
                    {
                        LayerId = 0,
                        AddFeaturesJson = ["""{"attributes":{"objectid":1,"name":"Layer0 Feature"}}"""],
                    },
                    new LayerChangeSet
                    {
                        LayerId = 3,
                        AddFeaturesJson = ["""{"attributes":{"objectid":1,"name":"Layer3 Feature"}}"""],
                    },
                ],
            },
            SynchronizeResponse = new SynchronizeResult("replica-multi", 40),
        };

        var engine = new DeltaDownloadEngine(store, replicaClient);
        var result = await engine.DownloadAsync("assets");

        Assert.Equal(2, result.Adds);

        var layer0 = await store.GetFeaturesAsync("0");
        var layer3 = await store.GetFeaturesAsync("3");

        Assert.Single(layer0);
        Assert.Single(layer3);
        Assert.Contains("Layer0 Feature", layer0[0]);
        Assert.Contains("Layer3 Feature", layer3[0]);
    }

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

    private sealed class FakeReplicaSyncClient : IReplicaSyncClient
    {
        public CreateReplicaResult CreateReplicaResponse { get; set; } = new("default", 0);

        public ExtractChangesResult ExtractChangesResponse { get; set; } = new()
        {
            LayerChanges = [],
            ServerGen = 0,
        };

        public SynchronizeResult SynchronizeResponse { get; set; } = new("default", 0);

        public int CreateReplicaCallCount { get; private set; }

        public int ExtractChangesCallCount { get; private set; }

        public int SynchronizeCallCount { get; private set; }

        public int UnregisterCallCount { get; private set; }

        public Task<CreateReplicaResult> CreateReplicaAsync(string serviceId, string replicaName, int[]? layerIds = null, CancellationToken ct = default)
        {
            CreateReplicaCallCount++;
            return Task.FromResult(CreateReplicaResponse);
        }

        public Task<ExtractChangesResult> ExtractChangesAsync(string serviceId, string replicaId, CancellationToken ct = default)
        {
            ExtractChangesCallCount++;
            return Task.FromResult(ExtractChangesResponse);
        }

        public Task<SynchronizeResult> SynchronizeReplicaAsync(string serviceId, string replicaId, string syncDirection = "download", CancellationToken ct = default)
        {
            SynchronizeCallCount++;
            return Task.FromResult(SynchronizeResponse);
        }

        public Task UnRegisterReplicaAsync(string serviceId, string replicaId, CancellationToken ct = default)
        {
            UnregisterCallCount++;
            return Task.CompletedTask;
        }
    }
}
