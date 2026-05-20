using System.Text;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.MapAreas;
using Honua.Mobile.Offline.ScenePackages;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Scenes;
using Honua.Sdk.Offline;
using BoundingBox = Honua.Sdk.Geometry.GeographicBoundingBox;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class OfflineServerIntegrationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 4, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _rootDirectory;

    public OfflineServerIntegrationTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"honua-server-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task ReplicaSyncClient_RoundTripsAllReplicaServerEndpoints()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        using var http = new HttpClient { BaseAddress = server.BaseUri };
        var client = new ReplicaSyncClient(http);

        var replica = await client.CreateReplicaAsync("offline", "device-replica", [0, 1]);
        Assert.Equal("replica-abc-123", replica.ReplicaId);
        Assert.Equal(42, replica.ServerGen);

        var changes = await client.ExtractChangesAsync("offline", replica.ReplicaId);
        Assert.Equal(55, changes.ServerGen);
        var layerChanges = Assert.Single(changes.LayerChanges);
        Assert.NotNull(layerChanges.DeleteIds);
        Assert.Equal([3L, 4L], layerChanges.DeleteIds);

        var sync = await client.SynchronizeReplicaAsync("offline", replica.ReplicaId);
        Assert.Equal(100, sync.ServerGen);

        await client.UnRegisterReplicaAsync("offline", replica.ReplicaId);

        Assert.True(server.Received("POST", "/rest/services/offline/FeatureServer/createReplica"));
        Assert.True(server.Received("POST", "/rest/services/offline/FeatureServer/extractChanges"));
        Assert.True(server.Received("POST", "/rest/services/offline/FeatureServer/synchronizeReplica"));
        Assert.True(server.Received("POST", "/rest/services/offline/FeatureServer/unRegisterReplica"));

        var createReplica = server.SingleRequest("POST", "/rest/services/offline/FeatureServer/createReplica");
        Assert.Contains("layers=0%2C1", createReplica.Body);
        Assert.Contains("replicaName=device-replica", createReplica.Body);
    }

    [Fact]
    public async Task MapAndScenePackageDownloaders_FetchAssetsFromServerAndRegisterCatalogRecords()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        var store = CreateStore();
        using var http = new HttpClient();
        var mapDownloader = new MapAreaDownloader(http, store);

        var mapResult = await mapDownloader.DownloadAsync(new MapAreaDownloadRequest
        {
            AreaId = "downtown",
            Name = "Downtown",
            BoundingBox = new BoundingBox(-157.9, 21.2, -157.7, 21.4),
            MinZoom = 10,
            MaxZoom = 12,
            OutputDirectory = Path.Combine(_rootDirectory, "maps"),
            Layers =
            [
                new MapLayerDownloadSource
                {
                    LayerKey = "roads",
                    SourceUrl = server.Uri("/tiles/roads?bbox={minLon},{minLat},{maxLon},{maxLat}&z={minZoom}-{maxZoom}").ToString(),
                    Priority = 1,
                },
                new MapLayerDownloadSource
                {
                    LayerKey = "buildings",
                    SourceUrl = server.Uri("/tiles/buildings").ToString(),
                    Priority = 2,
                },
            ],
        });

        Assert.Equal(2, mapResult.DownloadedLayerCount);
        Assert.True(File.Exists(mapResult.GeoPackagePath));
        Assert.True(server.Received("GET", "/tiles/roads"));
        Assert.True(server.Received("GET", "/tiles/buildings"));

        var metadata = Encoding.UTF8.GetBytes("""{"sceneId":"downtown-honolulu"}""");
        var tileset = Encoding.UTF8.GetBytes("""{"asset":{"version":"1.1"}}""");
        var manifest = CreateManifest(
            CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", metadata, "\"meta-1\""),
            CreateAsset("buildings-tileset", HonuaScenePackageAssetTypes.ThreeDimensionalTileset, "tilesets/buildings/tileset.json", tileset, "\"tiles-1\""));
        var sceneDownloader = new ScenePackageDownloader(http, store);

        var sceneResult = await sceneDownloader.DownloadAsync(new ScenePackageDownloadRequest
        {
            Manifest = manifest,
            AssetBaseUri = server.Uri("/scene-assets/pkg_downtown_honolulu_2026_04/"),
            OutputDirectory = Path.Combine(_rootDirectory, "scene-packages"),
            UtcNow = Now,
        });

        Assert.Equal(2, sceneResult.DownloadedAssetCount);
        Assert.True(File.Exists(Path.Combine(sceneResult.PackageDirectory, "metadata", "scene.json")));
        Assert.True(File.Exists(Path.Combine(sceneResult.PackageDirectory, "tilesets", "buildings", "tileset.json")));
        Assert.True(server.Received("GET", "/scene-assets/pkg_downtown_honolulu_2026_04/metadata/scene.json"));
        Assert.True(server.Received("GET", "/scene-assets/pkg_downtown_honolulu_2026_04/tilesets/buildings/tileset.json"));

        Assert.Single(await store.ListMapAreasAsync());
        Assert.Single(await store.ListScenePackagesAsync());
    }

    [Fact]
    public async Task OfflineOperationUploader_UploadsFeatureServerAndOgcPayloadsThroughMobileClient()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        using var client = CreateMobileClient(server);
        var uploader = new HonuaApiOfflineOperationUploader(client);

        var featureServerResult = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "assets/0",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = JsonSerializer.Serialize(new OfflineOperationPayload
            {
                Protocol = "FeatureServer",
                ServiceId = "assets",
                LayerId = 0,
                Feature = JsonSerializer.SerializeToElement(new
                {
                    attributes = new { name = "Offline Pump" },
                    geometry = new { x = -157.8, y = 21.3 },
                }),
            }, JsonOptions),
        }, forceWrite: true);

        var ogcPatchResult = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "buildings",
            TargetCollection = "buildings",
            OperationType = OfflineOperationType.Update,
            PayloadJson = JsonSerializer.Serialize(new OfflineOperationPayload
            {
                Protocol = "ogc",
                CollectionId = "buildings",
                FeatureId = "building-1",
                Patch = JsonSerializer.SerializeToElement(new
                {
                    properties = new { name = "Offline HQ" },
                }),
            }, JsonOptions),
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.Success, featureServerResult.Outcome);
        Assert.Equal(UploadOutcome.Success, ogcPatchResult.Outcome);
        Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/0/applyEdits"));
        Assert.True(server.Received("PATCH", "/ogc/features/collections/buildings/items/building-1"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private GeoPackageSyncStore CreateStore()
        => new(new GeoPackageSyncStoreOptions
        {
            DatabasePath = Path.Combine(_rootDirectory, "sync-store.gpkg"),
        });

    private static HonuaMobileClient CreateMobileClient(HonuaIntegrationServer server)
    {
        return new HonuaMobileClient(
            new HttpClient(),
            new HonuaMobileClientOptions
            {
                BaseUri = server.BaseUri,
                AllowInsecureTransportForDevelopment = true,
                PreferGrpcForFeatureQueries = false,
                PreferGrpcForFeatureEdits = false,
            });
    }

    private static HonuaScenePackageManifest CreateManifest(
        HonuaScenePackageAsset firstAsset,
        HonuaScenePackageAsset secondAsset)
    {
        var assets = new[] { firstAsset, secondAsset };
        var assetBytes = assets.Sum(asset => asset.Bytes ?? 0);
        return new HonuaScenePackageManifest
        {
            SchemaVersion = HonuaScenePackageManifest.CurrentSchemaVersion,
            PackageId = "pkg_downtown_honolulu_2026_04",
            SceneId = "downtown-honolulu",
            DisplayName = "Downtown Honolulu 3D",
            EditionGate = HonuaScenePackageEditionGates.Pro,
            ServerRevision = "scene-rev-42",
            CreatedAtUtc = Now.AddHours(-1),
            StaleAfterUtc = Now.AddDays(30),
            OfflineUseExpiresAtUtc = Now.AddDays(60),
            AuthExpiresAtUtc = Now.AddDays(1),
            Extent = new HonuaSceneBounds
            {
                MinLongitude = -157.872,
                MinLatitude = 21.293,
                MaxLongitude = -157.841,
                MaxLatitude = 21.319,
            },
            Lod = new HonuaScenePackageLod
            {
                MinZoom = 12,
                MaxZoom = 17,
                MaxGeometricErrorMeters = 4.0,
            },
            ByteBudget = new HonuaScenePackageByteBudget
            {
                MaxPackageBytes = assetBytes + 1024,
                DeclaredBytes = assetBytes,
            },
            Attribution = ["Honua"],
            Assets = assets,
        };
    }

    private static HonuaScenePackageAsset CreateAsset(
        string key,
        string type,
        string path,
        byte[] payload,
        string etag)
        => new()
        {
            Key = key,
            Type = type,
            Role = "metadata",
            Path = path,
            ContentType = "application/octet-stream",
            Bytes = payload.Length,
            Sha256 = HonuaIntegrationServer.Sha256Hex(payload),
            ETag = etag,
            Required = true,
        };
}
