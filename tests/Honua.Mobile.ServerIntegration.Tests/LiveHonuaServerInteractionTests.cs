using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FieldServices = Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.Maui.Diagnostics;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.MapAreas;
using Honua.Mobile.Offline.ScenePackages;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Auth;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Abstractions.Routing;
using Honua.Sdk.Abstractions.Scenes;
using Honua.Sdk.OgcFeatures.Models;
using Honua.Sdk.Offline.Abstractions;
using SdkFeatureClient = Honua.Mobile.Sdk.Features.HonuaMobileSdkFeatureClient;
using BoundingBox = Honua.Sdk.Geometry.GeographicBoundingBox;
using ReplicaSyncClient = Honua.Sdk.Offline.ReplicaSyncClient;
using OfflineSyncEngine = Honua.Mobile.Offline.Sync.OfflineSyncEngine;
using OfflineSyncEngineOptions = Honua.Mobile.Offline.Sync.OfflineSyncEngineOptions;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class LiveHonuaServerInteractionTests : IClassFixture<LiveHonuaServerFixture>, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LiveHonuaServerFixture _server;
    private readonly string _rootDirectory;

    public LiveHonuaServerInteractionTests(LiveHonuaServerFixture server)
    {
        _server = server;
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"honua-live-server-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    [SkippableFact]
    public async Task LiveImage_HealthEndpoint_RespondsReady()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var http = CreateHttpClient();
        using var response = await http.GetAsync(_server.Uri(_server.Options.ReadyPath));

        Assert.True(response.IsSuccessStatusCode, $"Expected ready endpoint to succeed, got {(int)response.StatusCode}.");
    }

    [SkippableFact]
    public async Task LiveImage_FeatureServerRestAndSdkFeatureContracts_RoundTrip()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var client = CreateMobileClient(preferGrpc: false);

        // Conformance: the FeatureServer REST query response must conform to the
        // canonical geospatial.v1.QueryFeaturesResponse contract (shared fixture
        // feature_query_response.json). A #1238-class JSONB-projection regression
        // returns HTTP 400 here; the runner attributes that to the named contract
        // and marks it known-tracked-xfail rather than surfacing a bare 400.
        await ConformanceContractRunner.RunAsync(
            FeatureContractConformance.FeatureQueryContract,
            async () =>
            {
                using var query = await client.QueryFeaturesAsync(new QueryFeaturesRequest
                {
                    ServiceId = _server.Options.ServiceId,
                    LayerId = _server.Options.LayerId,
                    Where = "1=1",
                    OutFields = ["*"],
                    ReturnGeometry = true,
                    ResultRecordCount = 1,
                });
                AssertFeatureQueryContract(query.RootElement, requireFeature: true);

                JsonDocument? streamPage = null;
                await foreach (var page in client.QueryFeaturesStreamAsync(new QueryFeaturesRequest
                {
                    ServiceId = _server.Options.ServiceId,
                    LayerId = _server.Options.LayerId,
                    ResultRecordCount = 1,
                }))
                {
                    streamPage = page;
                    break;
                }

                using (streamPage)
                {
                    if (streamPage is null)
                    {
                        throw new ContractDriftException(
                            FeatureContractConformance.FeatureQueryContract,
                            "server-streaming query yielded no page for the seeded layer.");
                    }

                    AssertFeatureQueryContract(streamPage.RootElement, requireFeature: false);
                }

                var sdkQuery = await client.QueryAsync(new FeatureQueryRequest
                {
                    Source = FeatureSource(),
                    Limit = 1,
                });
                AssertFeatureQueryContract(ToFeatureServerJson(sdkQuery), requireFeature: true);

                FeatureQueryResult? sdkPage = null;
                await foreach (var page in client.QueryPagesAsync(new FeatureQueryRequest
                {
                    Source = FeatureSource(),
                    Limit = 1,
                }))
                {
                    sdkPage = page;
                    break;
                }

                if (sdkPage is null)
                {
                    throw new ContractDriftException(
                        FeatureContractConformance.FeatureQueryContract,
                        "SDK QueryPagesAsync yielded no page for the seeded layer.");
                }

                AssertFeatureQueryContract(ToFeatureServerJson(sdkPage), requireFeature: true);
            });

        long? restObjectId = null;
        long? sdkObjectId = null;
        try
        {
            restObjectId = await AddFeatureServerFeatureAsync(client, "rest");
            using var update = await client.ApplyEditsAsync(new ApplyEditsRequest
            {
                ServiceId = _server.Options.ServiceId,
                LayerId = _server.Options.LayerId,
                UpdatesJson = JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        attributes = CreateFeatureAttributes("rest-update", restObjectId.Value),
                        geometry = CreateFeatureServerGeometry(),
                    },
                }, JsonOptions),
                ForceWrite = true,
            });
            AssertEditSucceeded(update, "updateResults");

            var sdkEdit = await client.ApplyEditsAsync(new FeatureEditRequest
            {
                Source = FeatureSource(),
                Adds =
                [
                    new FeatureEditFeature
                    {
                        Attributes = CreateSdkAttributes("sdk-add"),
                        Geometry = JsonSerializer.SerializeToElement(CreateGeoJsonPoint(), JsonOptions),
                    },
                ],
            });
            Assert.True(sdkEdit.Succeeded, FormatFeatureEditDiagnostic(sdkEdit, "FeatureServer SDK ApplyEdits.Adds"));
            sdkObjectId = Assert.Single(sdkEdit.AddResults).ObjectId;
            Assert.True(sdkObjectId > 0);
        }
        finally
        {
            if (restObjectId is not null)
            {
                await DeleteFeatureServerFeatureAsync(client, restObjectId.Value);
            }

            if (sdkObjectId is not null)
            {
                await DeleteFeatureServerFeatureAsync(client, sdkObjectId.Value);
            }
        }
    }

    [SkippableFact]
    public async Task LiveImage_MobileSdkFeatureClientAdapter_QuerySmoke()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var client = CreateMobileClient(preferGrpc: false);
        IHonuaFeatureQueryClient featureClient = new SdkFeatureClient(client);

        Assert.Equal("honua-mobile", featureClient.ProviderName);

        // Conformance: the SDK feature-client adapter response must conform to the
        // canonical FeatureService.QueryFeatures contract. A #1238-class regression
        // returns HTTP 400 here; attributed + known-tracked-xfail by the runner.
        await ConformanceContractRunner.RunAsync(
            FeatureContractConformance.FeatureQueryContract,
            async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = await featureClient.QueryAsync(new FeatureQueryRequest
                {
                    Source = FeatureSource(),
                    Limit = 1,
                }, timeout.Token);

                AssertFeatureQueryContract(ToFeatureServerJson(result), requireFeature: true);
            });
    }

    [SkippableFact]
    public async Task LiveImage_GrpcFeatureQueryAndEdit_RoundTrip()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var client = CreateMobileClient(preferGrpc: true, allowRestFallbackOnGrpcFailure: false);

        // Conformance: the gRPC query response (mapped to FeatureServer JSON) must
        // conform to the canonical geospatial.v1.QueryFeaturesResponse contract.
        await ConformanceContractRunner.RunAsync(
            FeatureContractConformance.FeatureQueryContract,
            async () =>
            {
                using var query = await client.QueryFeaturesAsync(new QueryFeaturesRequest
                {
                    ServiceId = _server.Options.ServiceId,
                    LayerId = _server.Options.LayerId,
                    ResultRecordCount = 1,
                });
                AssertFeatureQueryContract(query.RootElement, requireFeature: false);
            });

        long? objectId = null;
        try
        {
            using var add = await client.ApplyEditsAsync(new ApplyEditsRequest
            {
                ServiceId = _server.Options.ServiceId,
                LayerId = _server.Options.LayerId,
                Adds = [CreateFeatureServerFeature("grpc-add")],
                ForceWrite = true,
            });
            objectId = AssertEditSucceeded(add, "addResults");
        }
        finally
        {
            if (objectId is not null)
            {
                using var restClient = CreateMobileClient(preferGrpc: false);
                await DeleteFeatureServerFeatureAsync(restClient, objectId.Value);
            }
        }
    }

    [SkippableFact]
    public async Task LiveImage_GrpcFeatureQueryStream_RoundTrip()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        // Distinct from LiveImage_GrpcFeatureQueryAndEdit_RoundTrip (unary RPC + edit):
        // this exercises the server-streaming RPC path (QueryFeaturesStreamAsync ->
        // HonuaGrpcClient.QueryFeaturesStreamAsync) end-to-end against the live image,
        // with REST fallback disabled so any pure-streaming bug surfaces here rather
        // than being silently masked by the unary REST path.
        using var client = CreateMobileClient(preferGrpc: true, allowRestFallbackOnGrpcFailure: false);

        await ConformanceContractRunner.RunAsync(
            FeatureContractConformance.FeatureQueryContract,
            async () =>
            {
                JsonDocument? page = null;
                await foreach (var streamed in client.QueryFeaturesStreamAsync(new QueryFeaturesRequest
                {
                    ServiceId = _server.Options.ServiceId,
                    LayerId = _server.Options.LayerId,
                    Where = "1=1",
                    OutFields = ["*"],
                    ReturnGeometry = true,
                    ResultRecordCount = 1,
                }))
                {
                    page = streamed;
                    break;
                }

                using (page)
                {
                    if (page is null)
                    {
                        throw new ContractDriftException(
                            FeatureContractConformance.FeatureQueryContract,
                            "gRPC server-streaming query yielded no page for the seeded layer.");
                    }

                    // Conformance: gRPC streaming page conforms to the canonical
                    // QueryFeaturesResponse envelope (features array + per-feature shape).
                    AssertFeatureQueryContract(page.RootElement, requireFeature: false);
                }
            });
    }

    [SkippableFact]
    public async Task LiveImage_OgcFeaturesCrud_RoundTrip()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var client = CreateMobileClient(preferGrpc: false);
        var ogcSource = new FeatureSource { CollectionId = _server.Options.OgcCollectionId };

        // Conformance: the OGC Features read paths must conform to the canonical
        // FeatureService query contract mapped onto GeoJSON (FeatureCollection of
        // GeoJSON Features). A #1238-class regression returns HTTP 500 here; the
        // runner attributes it to the OGC contract and marks it known-tracked-xfail.
        await ConformanceContractRunner.RunAsync(
            FeatureContractConformance.OgcItemsContract,
            async () =>
            {
                using var collections = await client.GetOgcCollectionsAsync();
                if (collections.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new ContractDriftException(
                        FeatureContractConformance.OgcItemsContract,
                        $"GET collections root must be a JSON object, got {collections.RootElement.ValueKind}.");
                }

                using var items = await client.GetOgcItemsAsync(new OgcItemsRequest
                {
                    CollectionId = _server.Options.OgcCollectionId,
                    Limit = 1,
                });
                AssertOgcItemsContract(items.RootElement, requireFeature: true);

                var sdkQuery = await client.QueryAsync(new FeatureQueryRequest
                {
                    Source = ogcSource,
                    Limit = 1,
                });
                AssertOgcItemsContract(ToOgcFeatureCollectionJson(sdkQuery), requireFeature: true);

                FeatureQueryResult? sdkPage = null;
                await foreach (var page in client.QueryPagesAsync(new FeatureQueryRequest
                {
                    Source = ogcSource,
                    Limit = 1,
                }))
                {
                    sdkPage = page;
                    break;
                }

                if (sdkPage is null)
                {
                    throw new ContractDriftException(
                        FeatureContractConformance.OgcItemsContract,
                        "SDK QueryPagesAsync (OGC source) yielded no page for the seeded collection.");
                }

                AssertOgcItemsContract(ToOgcFeatureCollectionJson(sdkPage), requireFeature: true);
            });

        var featureId = $"mobile-live-ogc-{Guid.NewGuid():N}";
        var sdkFeatureId = $"mobile-live-ogc-sdk-{Guid.NewGuid():N}";
        string? createdId = null;
        string? sdkCreatedId = null;
        try
        {
            using var created = await client.CreateOgcItemAsync(new OgcCreateItemRequest
            {
                CollectionId = _server.Options.OgcCollectionId,
                Feature = CreateOgcFeature(featureId, "ogc-create"),
            });
            createdId = ReadId(created.RootElement) ?? featureId;

            using var replaced = await client.ReplaceOgcItemAsync(new OgcReplaceItemRequest
            {
                CollectionId = _server.Options.OgcCollectionId,
                FeatureId = createdId,
                Feature = CreateOgcFeature(createdId, "ogc-replace"),
            });
            Assert.Equal(JsonValueKind.Object, replaced.RootElement.ValueKind);

            using var patched = await client.PatchOgcItemAsync(new OgcPatchItemRequest
            {
                CollectionId = _server.Options.OgcCollectionId,
                FeatureId = createdId,
                Patch = JsonSerializer.SerializeToElement(new
                {
                    properties = new { notes = $"patched {Guid.NewGuid():N}" },
                }, JsonOptions),
            });
            Assert.Equal(JsonValueKind.Object, patched.RootElement.ValueKind);

            var sdkEdit = await client.ApplyEditsAsync(new FeatureEditRequest
            {
                Source = ogcSource,
                Adds =
                [
                    new FeatureEditFeature
                    {
                        Id = sdkFeatureId,
                        Attributes = CreateSdkAttributes("ogc-sdk-add"),
                        Geometry = JsonSerializer.SerializeToElement(CreateGeoJsonPoint(), JsonOptions),
                    },
                ],
            });
            Assert.True(sdkEdit.Succeeded, FormatFeatureEditDiagnostic(sdkEdit, "OGC SDK ApplyEdits.Adds"));
            sdkCreatedId = Assert.Single(sdkEdit.AddResults).Id ?? sdkFeatureId;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(sdkCreatedId))
            {
                var delete = await client.ApplyEditsAsync(new FeatureEditRequest
                {
                    Source = ogcSource,
                    DeleteIds = [sdkCreatedId],
                });
                Assert.True(delete.Succeeded, delete.Error?.Message);
            }

            if (!string.IsNullOrWhiteSpace(createdId))
            {
                using var deleted = await client.DeleteOgcItemAsync(new OgcDeleteItemRequest
                {
                    CollectionId = _server.Options.OgcCollectionId,
                    FeatureId = createdId,
                });
                Assert.Equal(JsonValueKind.Object, deleted.RootElement.ValueKind);
            }
        }
    }

    [SkippableFact]
    public async Task LiveImage_FeatureAttachments_RoundTrip()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var client = CreateMobileClient(preferGrpc: false);
        var source = FeatureSource();
        long? objectId = null;

        try
        {
            objectId = await AddFeatureServerFeatureAsync(client, "attachment-parent");

            using var addStream = new MemoryStream(Encoding.UTF8.GetBytes("live attachment"));
            var add = await client.AddAttachmentAsync(new FeatureAttachmentAddRequest
            {
                Source = source,
                ObjectId = objectId.Value,
                Name = "live-attachment.txt",
                ContentType = "text/plain",
                Content = addStream,
                Keywords = "live-image",
            });
            Assert.True(add.Succeeded, add.Error?.Message);
            var attachmentId = add.AttachmentId.GetValueOrDefault();
            Assert.True(attachmentId > 0);

            var listed = await client.ListAttachmentsAsync(new FeatureAttachmentListRequest
            {
                Source = source,
                ObjectId = objectId.Value,
            });
            Assert.Contains(listed, attachment => attachment.AttachmentId == attachmentId);

            var downloaded = await client.DownloadAttachmentAsync(new FeatureAttachmentDownloadRequest
            {
                Source = source,
                ObjectId = objectId.Value,
                AttachmentId = attachmentId,
            });
            using (downloaded.Content)
            using (var reader = new StreamReader(downloaded.Content, Encoding.UTF8))
            {
                Assert.Contains("live attachment", await reader.ReadToEndAsync());
            }

            using var updateStream = new MemoryStream(Encoding.UTF8.GetBytes("updated live attachment"));
            var update = await client.UpdateAttachmentAsync(new FeatureAttachmentUpdateRequest
            {
                Source = source,
                ObjectId = objectId.Value,
                AttachmentId = attachmentId,
                Name = "live-attachment-updated.txt",
                ContentType = "text/plain",
                Content = updateStream,
            });
            Assert.True(update.Succeeded, update.Error?.Message);

            var delete = await client.DeleteAttachmentAsync(new FeatureAttachmentDeleteRequest
            {
                Source = source,
                ObjectId = objectId.Value,
                AttachmentId = attachmentId,
            });
            Assert.True(delete.Succeeded, delete.Error?.Message);
        }
        finally
        {
            if (objectId is not null)
            {
                await DeleteFeatureServerFeatureAsync(client, objectId.Value);
            }
        }
    }

    [SkippableFact]
    public async Task LiveImage_ReplicaSyncAndOfflineQueue_RoundTrip()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var http = CreateHttpClient();
        var replicaClient = new ReplicaSyncClient(http);
        var replica = await replicaClient.CreateReplicaAsync(
            _server.Options.ServiceId,
            $"mobile-live-{Guid.NewGuid():N}",
            _server.Options.ReplicaLayerIds.ToArray());

        try
        {
            Assert.False(string.IsNullOrWhiteSpace(replica.ReplicaId));

            var changes = await replicaClient.ExtractChangesAsync(_server.Options.ServiceId, replica.ReplicaId);
            Assert.True(changes.ServerGen >= 0);

            var sync = await replicaClient.SynchronizeReplicaAsync(_server.Options.ServiceId, replica.ReplicaId);
            Assert.True(sync.ServerGen >= 0);
        }
        finally
        {
            await replicaClient.UnRegisterReplicaAsync(_server.Options.ServiceId, replica.ReplicaId);
        }

        using var client = CreateMobileClient(preferGrpc: false);
        var featureObjectId = await AddFeatureServerFeatureAsync(client, "offline-delete");
        var ogcId = await CreateOgcFeatureAsync(client, "offline-delete");

        var store = CreateStore();
        await store.InitializeAsync();
        await store.EnqueueAsync(CreateFeatureServerDeleteOperation(featureObjectId));
        await store.EnqueueAsync(CreateOgcDeleteOperation(ogcId));

        var engine = new OfflineSyncEngine(store, new HonuaApiOfflineOperationUploader(client));
        await using var orchestrator = new BackgroundSyncOrchestrator(
            engine,
            new AlwaysOnlineConnectivity(),
            new BackgroundSyncOrchestratorOptions { RunImmediately = false });

        var result = await orchestrator.RunOnceIfOnlineAsync();

        Assert.NotNull(result);
        Assert.Equal(2, result.Loaded);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, await store.CountPendingAsync());
    }

    [SkippableFact]
    public async Task LiveImage_MapAndScenePackageDownloaders_FetchServerAssets()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        Assert.NotNull(_server.Options.SceneAssetBaseUri);

        var store = CreateStore();
        using var http = CreateHttpClient();
        var mapDownloader = new MapAreaDownloader(http, store);
        var map = await mapDownloader.DownloadAsync(new MapAreaDownloadRequest
        {
            AreaId = "live-image-downtown",
            Name = "Live Image Downtown",
            BoundingBox = new BoundingBox(-158.1250, 21.2600, -157.7000, 21.5200),
            MinZoom = 1,
            MaxZoom = 1,
            OutputDirectory = Path.Combine(_rootDirectory, "maps"),
            Layers =
            [
                new MapLayerDownloadSource
                {
                    LayerKey = _server.Options.LayerId.ToString(CultureInfo.InvariantCulture),
                    SourceUrl = _server.Uri($"/tiles/{_server.Options.LayerId}/1/0/0.mvt").ToString(),
                    ContentType = "application/vnd.mapbox-vector-tile",
                    Required = true,
                },
            ],
        });
        Assert.Equal(1, map.DownloadedLayerCount);
        Assert.True(File.Exists(map.GeoPackagePath));

        var manifest = await CreateLiveSceneManifestAsync(http, _server.Options.SceneAssetBaseUri!);
        var sceneDownloader = new ScenePackageDownloader(http, store);
        var scene = await sceneDownloader.DownloadAsync(new ScenePackageDownloadRequest
        {
            Manifest = manifest,
            AssetBaseUri = _server.Options.SceneAssetBaseUri!,
            OutputDirectory = Path.Combine(_rootDirectory, "scenes"),
            UtcNow = Now,
        });

        Assert.Equal(manifest.Assets.Count, scene.DownloadedAssetCount);
        Assert.Single(await store.ListMapAreasAsync());
        Assert.Single(await store.ListScenePackagesAsync());
    }

    [SkippableFact]
    public async Task LiveImage_ScenesAndRouting_RoundTrip()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var client = CreateMobileClient(preferGrpc: false);
        var scenes = await client.Scenes.ListScenesAsync(new HonuaSceneListRequest
        {
            Capabilities = [HonuaSceneCapabilities.ThreeDimensionalTiles],
        });
        Assert.Contains(scenes, scene => scene.Id == _server.Options.SceneId);

        var sceneMetadata = await client.Scenes.GetSceneAsync(_server.Options.SceneId);
        Assert.Equal(_server.Options.SceneId, sceneMetadata.Id);

        var resolved = await client.Scenes.ResolveSceneAsync(_server.Options.SceneId);
        Assert.Equal(_server.Options.SceneId, resolved.SceneId);

        var directions = await client.Routing.GetDirectionsAsync(
            RoutingLocation.FromLongitudeLatitude(-157.8583, 21.3069, "Start"),
            RoutingLocation.FromLongitudeLatitude(-157.8037, 21.2810, "Finish"));
        Assert.NotEmpty(directions.Routes);

        var optimized = await client.Routing.OptimizeRouteAsync(
            [
                RoutingLocation.FromLongitudeLatitude(-157.86, 21.30, "A"),
                RoutingLocation.FromLongitudeLatitude(-157.84, 21.31, "B"),
                RoutingLocation.FromLongitudeLatitude(-157.82, 21.32, "C"),
            ],
            new RouteOptimizationOptions
            {
                PreserveFirstStop = true,
                PreserveLastStop = false,
            });
        Assert.NotEmpty(optimized.Routes);

        var serviceArea = await client.Routing.GetServiceAreaAsync(
            RoutingLocation.FromLongitudeLatitude(-157.8583, 21.3069, "Depot"),
            TimeSpan.FromMinutes(30));
        Assert.NotNull(serviceArea);

        var closest = await client.Routing.FindClosestFacilityAsync(
            [RoutingLocation.FromLongitudeLatitude(-157.85, 21.30, "Incident")],
            [RoutingLocation.FromLongitudeLatitude(-157.80, 21.28, "Facility A")]);
        Assert.NotEmpty(closest.Routes);
    }

    [SkippableFact]
    public async Task LiveImage_AuthRefreshAndExceptionUploads_RoundTrip()
    {
        Skip.IfNot(_server.Enabled, "HONUA_MOBILE_LIVE_SERVER_TESTS not set or fixture sql missing");

        using var authHttp = CreateHttpClient();
        var authService = new FieldServices.AuthenticationService(authHttp);
        Assert.True(await authService.ValidateConnectionAsync(_server.BaseUri.ToString(), _server.Options.ApiKey));

        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-access-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(-10)));
        var provider = new RefreshingAuthTokenProvider(
            store,
            authHttp,
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = _server.Uri(_server.Options.OAuthRefreshPath),
                RefreshSkew = TimeSpan.FromMinutes(2),
            });

        var token = await provider.GetTokenAsync();
        Assert.NotNull(token);
        Assert.Equal(HonuaAuthScheme.Bearer, token.Scheme);

        await UploadAppExceptionReportAsync();
        await UploadMauiExceptionReportAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    // --- Compatibility-Train conformance helpers ------------------------------
    //
    // Assert a live FeatureServer/OGC response against the shared, pinned
    // geospatial-grpc conformance fixtures (see ConformanceFixtures /
    // FeatureContractConformance). When the fixtures bundle has not been fetched
    // (HONUA_MOBILE_CONFORMANCE_FIXTURES_DIR unset) the structural contract checks
    // still run; the canonical fixture is consulted only to confirm it loads and
    // names the contract, so a developer without the bundle is not blocked.

    private static void AssertFeatureQueryContract(JsonElement live, bool requireFeature)
    {
        var canonical = ConformanceFixtures.Instance.Available
            ? ConformanceFixtures.Instance.FeatureQueryResponse
            : default;
        FeatureContractConformance.AssertFeatureQueryResponse(live, canonical, requireFeature);
    }

    private static void AssertOgcItemsContract(JsonElement live, bool requireFeature)
        => FeatureContractConformance.AssertOgcItemsResponse(live, requireFeature);

    // Project the provider-neutral SDK FeatureQueryResult into the canonical
    // FeatureServer JSON envelope (objectIdFieldName + features[].attributes/geometry)
    // so it can be validated against the same QueryFeaturesResponse contract as the
    // raw REST/gRPC responses.
    private static JsonElement ToFeatureServerJson(FeatureQueryResult result)
    {
        var features = result.Features.Select(feature =>
        {
            var obj = new Dictionary<string, object?>
            {
                ["attributes"] = feature.Attributes.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value),
            };
            if (feature.Geometry is { } geometry)
            {
                obj["geometry"] = geometry;
            }

            return obj;
        }).ToArray();

        var envelope = new Dictionary<string, object?>
        {
            ["objectIdFieldName"] = result.ObjectIdFieldName ?? "OBJECTID",
            ["features"] = features,
        };

        return JsonSerializer.SerializeToElement(envelope, JsonOptions);
    }

    // Project the provider-neutral SDK FeatureQueryResult into a GeoJSON
    // FeatureCollection so the OGC read path can be validated against the OGC items
    // contract.
    private static JsonElement ToOgcFeatureCollectionJson(FeatureQueryResult result)
    {
        var features = result.Features.Select(feature =>
        {
            var obj = new Dictionary<string, object?>
            {
                ["type"] = "Feature",
                ["properties"] = feature.Attributes.ToDictionary(kvp => kvp.Key, kvp => (object?)kvp.Value),
            };
            if (feature.Id is not null)
            {
                obj["id"] = feature.Id;
            }

            if (feature.Geometry is { } geometry)
            {
                obj["geometry"] = geometry;
            }

            return obj;
        }).ToArray();

        var envelope = new Dictionary<string, object?>
        {
            ["type"] = "FeatureCollection",
            ["features"] = features,
        };

        return JsonSerializer.SerializeToElement(envelope, JsonOptions);
    }

    private HttpClient CreateHttpClient()
    {
        var http = new HttpClient { BaseAddress = _server.BaseUri, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _server.Options.ApiKey);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _server.Options.BearerToken);
        return http;
    }

    private HttpClient CreateUnauthenticatedHttpClient()
    {
        return new HttpClient { BaseAddress = _server.BaseUri, Timeout = TimeSpan.FromSeconds(30) };
    }

    private HonuaMobileClient CreateMobileClient(bool preferGrpc, bool allowRestFallbackOnGrpcFailure = true)
    {
        return new HonuaMobileClient(
            new HttpClient(),
            new HonuaMobileClientOptions
            {
                BaseUri = _server.BaseUri,
                GrpcEndpoint = _server.Options.GrpcEndpoint ?? _server.BaseUri,
                ApiKey = _server.Options.ApiKey,
                BearerToken = _server.Options.BearerToken,
                AllowInsecureTransportForDevelopment = _server.BaseUri.Scheme == Uri.UriSchemeHttp,
                PreferGrpcForFeatureQueries = preferGrpc,
                PreferGrpcForFeatureEdits = preferGrpc,
                AllowRestFallbackOnGrpcFailure = allowRestFallbackOnGrpcFailure,
                RoutingServiceId = _server.Options.RoutingServiceId,
            });
    }

    private GeoPackageSyncStore CreateStore()
        => new(new GeoPackageSyncStoreOptions
        {
            DatabasePath = Path.Combine(_rootDirectory, $"sync-store-{Guid.NewGuid():N}.gpkg"),
        });

    private FeatureSource FeatureSource()
        => new()
        {
            ServiceId = _server.Options.ServiceId,
            LayerId = _server.Options.LayerId,
        };

    private async Task<long> AddFeatureServerFeatureAsync(HonuaMobileClient client, string suffix)
    {
        using var result = await client.ApplyEditsAsync(new ApplyEditsRequest
        {
            ServiceId = _server.Options.ServiceId,
            LayerId = _server.Options.LayerId,
            Adds = [CreateFeatureServerFeature(suffix)],
            ForceWrite = true,
        });

        var objectId = AssertEditSucceeded(result, "addResults");
        Assert.True(objectId > 0, result.RootElement.GetRawText());
        return objectId;
    }

    private async Task DeleteFeatureServerFeatureAsync(HonuaMobileClient client, long objectId)
    {
        using var result = await client.ApplyEditsAsync(new ApplyEditsRequest
        {
            ServiceId = _server.Options.ServiceId,
            LayerId = _server.Options.LayerId,
            Deletes = [objectId],
            ForceWrite = true,
        });
        AssertEditSucceeded(result, "deleteResults");
    }

    private FeatureEditFeature CreateFeatureServerFeature(string suffix)
    {
        return new FeatureEditFeature
        {
            Attributes = CreateFeatureAttributes(suffix),
            Geometry = JsonSerializer.SerializeToElement(CreateFeatureServerGeometry(), JsonOptions),
        };
    }

    private static Dictionary<string, JsonElement> CreateSdkAttributes(string suffix)
        => CreateFeatureAttributes(suffix);

    private static Dictionary<string, JsonElement> CreateFeatureAttributes(string suffix, long? objectId = null)
    {
        var attributes = new Dictionary<string, JsonElement>
        {
            ["globalid"] = JsonSerializer.SerializeToElement($"mobile-live-{suffix}-{Guid.NewGuid():N}", JsonOptions),
            ["site_name"] = JsonSerializer.SerializeToElement($"Live Image {suffix}", JsonOptions),
            ["status"] = JsonSerializer.SerializeToElement("open", JsonOptions),
            ["priority"] = JsonSerializer.SerializeToElement("normal", JsonOptions),
            ["assigned_to"] = JsonSerializer.SerializeToElement("mobile-live", JsonOptions),
            ["inspection_date"] = JsonSerializer.SerializeToElement("2026-05-06T12:00:00Z", JsonOptions),
            ["sync_version"] = JsonSerializer.SerializeToElement(1, JsonOptions),
            ["offline_action"] = JsonSerializer.SerializeToElement("live-image-test", JsonOptions),
            ["notes"] = JsonSerializer.SerializeToElement($"Created by honua-mobile live image test {suffix}", JsonOptions),
        };

        if (objectId is not null)
        {
            attributes["objectid"] = JsonSerializer.SerializeToElement(objectId.Value, JsonOptions);
        }

        return attributes;
    }

    private static object CreateFeatureServerGeometry()
        => new
        {
            x = -158.0100,
            y = 21.3100,
            spatialReference = new { wkid = 4326 },
        };

    private static object CreateGeoJsonPoint()
        => new
        {
            type = "Point",
            coordinates = new[] { -158.0100, 21.3100 },
        };

    private static long AssertEditSucceeded(JsonDocument document, string resultProperty)
    {
        var result = document.RootElement.GetProperty(resultProperty)[0];
        Assert.True(result.GetProperty("success").GetBoolean(), document.RootElement.GetRawText());

        if (result.TryGetProperty("objectId", out var objectId) && objectId.TryGetInt64(out var id))
        {
            return id;
        }

        if (result.TryGetProperty("objectid", out var objectIdLower) && objectIdLower.TryGetInt64(out var lowerId))
        {
            return lowerId;
        }

        return 0;
    }

    // Captures provider name, top-level error, and per-feature results so live-server
    // failures surface enough context to diagnose contract drift on the next CI run.
    private static string FormatFeatureEditDiagnostic(FeatureEditResponse response, string context)
    {
        static string FormatResults(IReadOnlyList<FeatureEditResult> results)
            => results.Count == 0
                ? "(none)"
                : string.Join(",", results.Select(r =>
                    $"{{id={r.Id ?? "<null>"},oid={r.ObjectId?.ToString(CultureInfo.InvariantCulture) ?? "<null>"},success={r.Succeeded},error={r.Error?.Code?.ToString(CultureInfo.InvariantCulture) ?? "<null>"}/{r.Error?.Message ?? "<null>"}}}"));

        var validation = response.ValidationResults.Count == 0
            ? "(none)"
            : string.Join(",", response.ValidationResults.Select(v => $"{{severity={v.Severity},blocks={v.BlocksApply},rule={v.RuleId ?? "<null>"},message={v.Message}}}"));

        return $"{context} failed. provider={response.ProviderName}; "
            + $"error={response.Error?.Code?.ToString(CultureInfo.InvariantCulture) ?? "<null>"}/{response.Error?.Message ?? "<null>"}; "
            + $"adds={FormatResults(response.AddResults)}; "
            + $"updates={FormatResults(response.UpdateResults)}; "
            + $"deletes={FormatResults(response.DeleteResults)}; "
            + $"validation={validation}";
    }

    private JsonElement CreateOgcFeature(string id, string suffix)
    {
        return JsonSerializer.SerializeToElement(new OgcFeature
        {
            Id = JsonSerializer.SerializeToElement(id, JsonOptions),
            Properties = CreateFeatureAttributes(suffix),
            Geometry = JsonSerializer.SerializeToElement(CreateGeoJsonPoint(), JsonOptions),
        }, JsonOptions);
    }

    private async Task<string> CreateOgcFeatureAsync(HonuaMobileClient client, string suffix)
    {
        var id = $"mobile-live-ogc-{Guid.NewGuid():N}";
        using var created = await client.CreateOgcItemAsync(new OgcCreateItemRequest
        {
            CollectionId = _server.Options.OgcCollectionId,
            Feature = CreateOgcFeature(id, suffix),
        });

        return ReadId(created.RootElement) ?? id;
    }

    private static string? ReadId(JsonElement element)
    {
        if (!element.TryGetProperty("id", out var id))
        {
            return null;
        }

        return id.ValueKind switch
        {
            JsonValueKind.String => id.GetString(),
            JsonValueKind.Number => id.GetRawText(),
            _ => null,
        };
    }

    private OfflineEditOperation CreateFeatureServerDeleteOperation(long objectId)
    {
        return new OfflineEditOperation
        {
            OperationId = $"live-fs-delete-{Guid.NewGuid():N}",
            LayerKey = $"{_server.Options.ServiceId}/{_server.Options.LayerId}",
            TargetCollection = _server.Options.ServiceId,
            OperationType = OfflineOperationType.Delete,
            PayloadJson = JsonSerializer.Serialize(new OfflineOperationPayload
            {
                Protocol = "FeatureServer",
                ServiceId = _server.Options.ServiceId,
                LayerId = _server.Options.LayerId,
                DeleteObjectIds = [objectId],
            }, JsonOptions),
        };
    }

    private OfflineEditOperation CreateOgcDeleteOperation(string featureId)
    {
        return new OfflineEditOperation
        {
            OperationId = $"live-ogc-delete-{Guid.NewGuid():N}",
            LayerKey = _server.Options.OgcCollectionId,
            TargetCollection = _server.Options.OgcCollectionId,
            OperationType = OfflineOperationType.Delete,
            PayloadJson = JsonSerializer.Serialize(new OfflineOperationPayload
            {
                Protocol = "ogc",
                CollectionId = _server.Options.OgcCollectionId,
                FeatureId = featureId,
            }, JsonOptions),
        };
    }

    private async Task<HonuaScenePackageManifest> CreateLiveSceneManifestAsync(HttpClient http, Uri assetBaseUri)
    {
        var metadata = await ReadAssetAsync(http, new Uri(assetBaseUri, "metadata/scene.json"));
        var tileset = await ReadAssetAsync(http, new Uri(assetBaseUri, "tileset.json"));
        var declaredBytes = metadata.Length + tileset.Length;

        return new HonuaScenePackageManifest
        {
            SchemaVersion = HonuaScenePackageManifest.CurrentSchemaVersion,
            PackageId = "live-image-scene-package",
            SceneId = _server.Options.SceneId,
            DisplayName = "Live Image Scene Package",
            EditionGate = HonuaScenePackageEditionGates.Pro,
            ServerRevision = "live-image",
            CreatedAtUtc = Now.AddHours(-1),
            StaleAfterUtc = Now.AddDays(1),
            OfflineUseExpiresAtUtc = Now.AddDays(1),
            AuthExpiresAtUtc = Now.AddHours(1),
            Extent = new HonuaSceneBounds
            {
                MinLongitude = -158.1250,
                MinLatitude = 21.2600,
                MaxLongitude = -157.7000,
                MaxLatitude = 21.5200,
            },
            Lod = new HonuaScenePackageLod
            {
                MinZoom = 12,
                MaxZoom = 17,
                MaxGeometricErrorMeters = 4,
            },
            ByteBudget = new HonuaScenePackageByteBudget
            {
                MaxPackageBytes = declaredBytes + 1024,
                DeclaredBytes = declaredBytes,
            },
            Attribution = ["Honua"],
            Assets =
            [
                CreateAsset("scene-metadata", HonuaScenePackageAssetTypes.SceneMetadata, "metadata/scene.json", metadata),
                CreateAsset("tileset", HonuaScenePackageAssetTypes.ThreeDimensionalTileset, "tileset.json", tileset),
            ],
        };
    }

    private static async Task<byte[]> ReadAssetAsync(HttpClient http, Uri uri)
    {
        using var response = await http.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static HonuaScenePackageAsset CreateAsset(string key, string type, string path, byte[] payload)
        => new()
        {
            Key = key,
            Type = type,
            Role = "metadata",
            Path = path,
            ContentType = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? "application/json"
                : "application/octet-stream",
            Bytes = payload.Length,
            Sha256 = HonuaIntegrationServer.Sha256Hex(payload),
            Required = true,
        };

    private async Task UploadAppExceptionReportAsync()
    {
        var auth = new FakeAuthenticationService(_server.BaseUri.ToString(), _server.Options.ApiKey);
        var options = new MobileExceptionReportingOptions
        {
            Mode = MobileExceptionReportingMode.ServerUpload,
            QueueDirectory = Path.Combine(_rootDirectory, "app-exceptions"),
            UploadEndpoint = _server.Uri(_server.Options.ExceptionUploadPath),
            UploadInitialBackoff = TimeSpan.Zero,
            UploadMaxBackoff = TimeSpan.Zero,
        };
        var queue = new FileMobileExceptionReportQueue(options);
        var reporter = new LocalMobileExceptionReporter(queue, options);
        using var http = CreateUnauthenticatedHttpClient();
        var uploader = new HttpMobileExceptionReportUploader(
            http,
            options,
            [new FieldCollectionExceptionReportAuthHeader(auth)]);
        var worker = new MobileExceptionReportUploadWorker(queue, uploader, options);

        await reporter.ReportAsync(
            new InvalidOperationException("live app exception token=secret"),
            new MobileExceptionReportContext
            {
                Source = "live-image-app",
                Properties = new Dictionary<string, object?> { ["apiKey"] = "secret" },
            });
        await worker.FlushPendingAsync();
        await AssertQueueDrainedAsync(queue);
    }

    private async Task UploadMauiExceptionReportAsync()
    {
        var options = new MobileExceptionReportingOptions
        {
            Mode = MobileExceptionReportingMode.ServerUpload,
            QueueDirectory = Path.Combine(_rootDirectory, "maui-exceptions"),
            UploadEndpoint = _server.Uri(_server.Options.ExceptionUploadPath),
            UploadInitialBackoff = TimeSpan.Zero,
            UploadMaxBackoff = TimeSpan.Zero,
        };
        var queue = new FileMobileExceptionReportQueue(options);
        var reporter = new LocalMobileExceptionReporter(queue, options);
        var uploader = new HttpMobileExceptionReportUploader(CreateHttpClient(), options);
        var worker = new MobileExceptionReportUploadWorker(queue, uploader, options);

        await reporter.ReportAsync(
            new InvalidOperationException("live maui exception token=secret"),
            new MobileExceptionReportContext { Source = "live-image-maui" });
        await worker.FlushPendingAsync();

        await AssertQueueDrainedAsync(queue);
    }

    private static async Task AssertQueueDrainedAsync(IMobileExceptionReportQueue queue)
    {
        var pending = new List<QueuedMobileExceptionReport>();
        await foreach (var report in queue.ReadPendingAsync())
        {
            pending.Add(report);
        }

        Assert.Empty(pending);
    }

    private sealed class AlwaysOnlineConnectivity : IConnectivityStateProvider
    {
        public bool IsOnline => true;
    }

    private sealed class FakeAuthenticationService : FieldServices.IAuthenticationService
    {
        public FakeAuthenticationService(string serverUrl, string apiKey)
        {
            ServerUrl = serverUrl;
            ApiKey = apiKey;
        }

        public bool IsAuthenticated => true;

        public bool RequiresReauthentication => false;

        public string? SessionStatusMessage => "Session active";

        public DateTimeOffset? ExpiresAtUtc => null;

        public HonuaAuthScheme? AuthScheme => HonuaAuthScheme.ApiKey;

        public string? CurrentUserId => "live-image";

        public string? CurrentUserName => "Live Image";

        public string? ApiKey { get; }

        public string? ServerUrl { get; }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public Task<FieldServices.AuthenticationResult> AuthenticateAsync(string serverUrl, string apiKey)
            => Task.FromResult(FieldServices.AuthenticationResult.Success("live-image", "Live Image", apiKey));

        public Task<FieldServices.AuthenticationResult> AuthenticateWithCredentialsAsync(
            string serverUrl,
            string username,
            string password)
            => Task.FromResult(FieldServices.AuthenticationResult.Failure("Credentials are not used in live image tests."));

        public Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> EnsureValidSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public ValueTask<HonuaAuthToken?> GetAuthTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<HonuaAuthToken?>(new HonuaAuthToken(HonuaAuthScheme.ApiKey, ApiKey!));

        public Task LogoutAsync() => Task.CompletedTask;

        public Task<bool> ValidateConnectionAsync(string serverUrl, string? apiKey = null) => Task.FromResult(true);
    }
}
