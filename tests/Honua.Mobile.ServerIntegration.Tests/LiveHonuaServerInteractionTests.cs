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

    [Fact]
    public async Task LiveImage_HealthEndpoint_RespondsReady()
    {
        if (!_server.Enabled)
        {
            return;
        }

        using var http = CreateHttpClient();
        using var response = await http.GetAsync(_server.Uri(_server.Options.ReadyPath));

        Assert.True(response.IsSuccessStatusCode, $"Expected ready endpoint to succeed, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task LiveImage_FeatureServerRestAndSdkFeatureContracts_RoundTrip()
    {
        if (!_server.Enabled)
        {
            return;
        }

        using var client = CreateMobileClient(preferGrpc: false);
        using var query = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = _server.Options.ServiceId,
            LayerId = _server.Options.LayerId,
            Where = "1=1",
            OutFields = ["*"],
            ReturnGeometry = true,
            ResultRecordCount = 1,
        });
        Assert.Equal(JsonValueKind.Object, query.RootElement.ValueKind);

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
            Assert.NotNull(streamPage);
            Assert.Equal(JsonValueKind.Object, streamPage.RootElement.ValueKind);
        }

        var sdkQuery = await client.QueryAsync(new FeatureQueryRequest
        {
            Source = FeatureSource(),
            Limit = 1,
        });
        Assert.NotEmpty(sdkQuery.Features);

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

        Assert.NotNull(sdkPage);
        Assert.NotEmpty(sdkPage.Features);

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
            Assert.True(sdkEdit.Succeeded, sdkEdit.Error?.Message);
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

    [Fact]
    public async Task LiveImage_MobileSdkFeatureClientAdapter_QuerySmoke()
    {
        if (!_server.Enabled)
        {
            return;
        }

        using var client = CreateMobileClient(preferGrpc: false);
        IHonuaFeatureQueryClient featureClient = new SdkFeatureClient(client);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await featureClient.QueryAsync(new FeatureQueryRequest
        {
            Source = FeatureSource(),
            Limit = 1,
        }, timeout.Token);

        Assert.Equal("honua-mobile", featureClient.ProviderName);
        Assert.NotNull(result.Features);
    }

    [Fact]
    public async Task LiveImage_GrpcFeatureQueryAndEdit_RoundTrip()
    {
        if (!_server.Enabled)
        {
            return;
        }

        using var client = CreateMobileClient(preferGrpc: true, allowRestFallbackOnGrpcFailure: false);
        using var query = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = _server.Options.ServiceId,
            LayerId = _server.Options.LayerId,
            ResultRecordCount = 1,
        });
        Assert.Equal(JsonValueKind.Object, query.RootElement.ValueKind);

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

    [Fact]
    public async Task LiveImage_OgcFeaturesCrud_RoundTrip()
    {
        if (!_server.Enabled)
        {
            return;
        }

        using var client = CreateMobileClient(preferGrpc: false);
        var ogcSource = new FeatureSource { CollectionId = _server.Options.OgcCollectionId };

        using var collections = await client.GetOgcCollectionsAsync();
        Assert.Equal(JsonValueKind.Object, collections.RootElement.ValueKind);

        using var items = await client.GetOgcItemsAsync(new OgcItemsRequest
        {
            CollectionId = _server.Options.OgcCollectionId,
            Limit = 1,
        });
        Assert.Equal("FeatureCollection", items.RootElement.GetProperty("type").GetString());

        var sdkQuery = await client.QueryAsync(new FeatureQueryRequest
        {
            Source = ogcSource,
            Limit = 1,
        });
        Assert.NotEmpty(sdkQuery.Features);

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

        Assert.NotNull(sdkPage);
        Assert.NotEmpty(sdkPage.Features);

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
            Assert.True(sdkEdit.Succeeded, sdkEdit.Error?.Message);
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

    [Fact]
    public async Task LiveImage_FeatureAttachments_RoundTrip()
    {
        if (!_server.Enabled)
        {
            return;
        }

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

    [Fact]
    public async Task LiveImage_ReplicaSyncAndOfflineQueue_RoundTrip()
    {
        if (!_server.Enabled)
        {
            return;
        }

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

    [Fact]
    public async Task LiveImage_MapAndScenePackageDownloaders_FetchServerAssets()
    {
        if (!_server.Enabled)
        {
            return;
        }

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

    [Fact]
    public async Task LiveImage_ScenesAndRouting_RoundTrip()
    {
        if (!_server.Enabled)
        {
            return;
        }

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

    [Fact]
    public async Task LiveImage_AuthRefreshAndExceptionUploads_RoundTrip()
    {
        if (!_server.Enabled)
        {
            return;
        }

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

        public Task<bool> RefreshTokenAsync() => Task.FromResult(true);

        public Task LogoutAsync() => Task.CompletedTask;

        public Task<bool> ValidateConnectionAsync(string serverUrl, string? apiKey = null) => Task.FromResult(true);
    }
}
