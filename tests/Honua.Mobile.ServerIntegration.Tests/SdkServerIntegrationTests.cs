using System.Text;
using System.Text.Json;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Auth;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Abstractions.Routing;
using Honua.Sdk.Abstractions.Scenes;
using Honua.Sdk.OgcFeatures.Models;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class SdkServerIntegrationTests
{
    [Fact]
    public async Task HonuaMobileClient_RoundTripsImplementedRestServerSurface()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        using var client = CreateClient(server);

        using var featureResult = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
            OutFields = ["objectid", "name"],
            ReturnGeometry = true,
        });
        Assert.Equal("Pump Station", featureResult.RootElement
            .GetProperty("features")[0]
            .GetProperty("attributes")
            .GetProperty("name")
            .GetString());

        var streamPages = new List<JsonDocument>();
        await foreach (var page in client.QueryFeaturesStreamAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        }))
        {
            streamPages.Add(page);
        }

        using (var streamPage = Assert.Single(streamPages))
        {
            Assert.Equal("Pump Station", streamPage.RootElement
                .GetProperty("features")[0]
                .GetProperty("attributes")
                .GetProperty("name")
                .GetString());
        }

        var featureServerQuery = await client.QueryAsync(new FeatureQueryRequest
        {
            Source = new FeatureSource { ServiceId = "assets", LayerId = 0 },
            Limit = 1,
        });
        Assert.Equal("Pump Station", featureServerQuery.Features[0].Attributes["name"].GetString());

        var featureServerEdit = await client.ApplyEditsAsync(new FeatureEditRequest
        {
            Source = new FeatureSource { ServiceId = "assets", LayerId = 0 },
            Adds =
            [
                new FeatureEditFeature
                {
                    Attributes = new Dictionary<string, JsonElement>
                    {
                        ["name"] = JsonSerializer.SerializeToElement("SDK Pump"),
                    },
                    Geometry = JsonSerializer.SerializeToElement(new { x = -157.8, y = 21.3 }),
                },
            ],
        });
        Assert.True(featureServerEdit.Succeeded);

        using var editResult = await client.ApplyEditsAsync(new ApplyEditsRequest
        {
            ServiceId = "assets",
            LayerId = 0,
            Adds =
            [
                new FeatureEditFeature
                {
                    Attributes = new Dictionary<string, JsonElement>
                    {
                        ["name"] = JsonSerializer.SerializeToElement("Pump Station"),
                    },
                    Geometry = JsonSerializer.SerializeToElement(new { x = -157.8, y = 21.3 }),
                },
            ],
            UpdatesJson = """[{ "attributes": { "objectid": 43, "name": "Updated" } }]""",
            Deletes = [7],
            RollbackOnFailure = true,
            ForceWrite = true,
        });
        Assert.True(editResult.RootElement.GetProperty("addResults")[0].GetProperty("success").GetBoolean());

        using var collections = await client.GetOgcCollectionsAsync();
        Assert.Equal("buildings", collections.RootElement.GetProperty("collections")[0].GetProperty("id").GetString());

        using var items = await client.GetOgcItemsAsync(new OgcItemsRequest
        {
            CollectionId = "buildings",
            Limit = 10,
            Offset = 0,
            PropertyNames = ["name"],
            CqlFilter = "name = 'HQ'",
        });
        Assert.Equal("FeatureCollection", items.RootElement.GetProperty("type").GetString());

        var ogcQuery = await client.QueryAsync(new FeatureQueryRequest
        {
            Source = new FeatureSource { CollectionId = "buildings" },
            Limit = 1,
        });
        Assert.Equal("HQ", ogcQuery.Features[0].Attributes["name"].GetString());

        using var created = await client.CreateOgcItemAsync(new OgcCreateItemRequest
        {
            CollectionId = "buildings",
            Feature = CreateOgcFeature("building-created", "HQ"),
        });
        Assert.Equal("building-created", created.RootElement.GetProperty("id").GetString());

        using var replaced = await client.ReplaceOgcItemAsync(new OgcReplaceItemRequest
        {
            CollectionId = "buildings",
            FeatureId = "building-1",
            Feature = CreateOgcFeature("building-1", "HQ Replaced"),
        });
        Assert.True(replaced.RootElement.GetProperty("properties").GetProperty("replaced").GetBoolean());

        using var patched = await client.PatchOgcItemAsync(new OgcPatchItemRequest
        {
            CollectionId = "buildings",
            FeatureId = "building-1",
            Patch = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["properties"] = new Dictionary<string, object?> { ["name"] = "HQ Patched" },
            }),
        });
        Assert.True(patched.RootElement.GetProperty("properties").GetProperty("patched").GetBoolean());

        using var deleted = await client.DeleteOgcItemAsync(new OgcDeleteItemRequest
        {
            CollectionId = "buildings",
            FeatureId = "building-1",
        });
        Assert.True(deleted.RootElement.GetProperty("deleted").GetBoolean());

        var ogcEdit = await client.ApplyEditsAsync(new FeatureEditRequest
        {
            Source = new FeatureSource { CollectionId = "buildings" },
            DeleteIds = ["building-1"],
        });
        Assert.True(ogcEdit.Succeeded);

        var scenes = await client.Scenes.ListScenesAsync(new HonuaSceneListRequest
        {
            Capabilities = [HonuaSceneCapabilities.ThreeDimensionalTiles],
        });
        Assert.Equal("downtown-honolulu", Assert.Single(scenes).Id);

        var scene = await client.Scenes.GetSceneAsync("downtown-honolulu");
        Assert.Equal("downtown-honolulu", scene.Id);

        var resolvedScene = await client.Scenes.ResolveSceneAsync("downtown-honolulu");
        Assert.Equal("downtown-honolulu", resolvedScene.SceneId);

        var directions = await client.Routing.GetDirectionsAsync(
            RoutingLocation.FromLongitudeLatitude(-157.8583, 21.3069, "Start"),
            RoutingLocation.FromLongitudeLatitude(-157.8037, 21.2810, "Finish"));
        Assert.Equal("Route 1", directions.Routes[0].Name);

        var serviceArea = await client.Routing.GetServiceAreaAsync(
            RoutingLocation.FromLongitudeLatitude(-157.8583, 21.3069, "Depot"),
            TimeSpan.FromMinutes(30));
        Assert.NotNull(serviceArea);

        var closest = await client.Routing.FindClosestFacilityAsync(
            [RoutingLocation.FromLongitudeLatitude(-157.85, 21.30, "Incident")],
            [RoutingLocation.FromLongitudeLatitude(-157.80, 21.28, "Facility A")]);
        Assert.Equal("Incident - Facility A", closest.Routes[0].Name);

        Assert.True(server.Received("GET", "/rest/services/assets/FeatureServer/0/query"));
        Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/0/applyEdits"));
        Assert.True(server.Received("GET", "/ogc/features/collections"));
        Assert.True(server.Received("POST", "/ogc/features/collections/buildings/items"));
        Assert.True(server.Received("PUT", "/ogc/features/collections/buildings/items/building-1"));
        Assert.True(server.Received("PATCH", "/ogc/features/collections/buildings/items/building-1"));
        Assert.True(server.Received("DELETE", "/ogc/features/collections/buildings/items/building-1"));
        Assert.True(server.Received("GET", "/api/scenes"));
        Assert.True(server.Received("GET", "/api/scenes/downtown-honolulu"));
        Assert.True(server.Received("GET", "/api/scenes/downtown-honolulu/resolve"));
        Assert.True(server.Received("POST", "/rest/services/Routing/NAServer/Route/solve"));
        Assert.True(server.Received("POST", "/rest/services/Routing/NAServer/ServiceArea/solveServiceArea"));
        Assert.True(server.Received("POST", "/rest/services/Routing/NAServer/ClosestFacility/solveClosestFacility"));

        Assert.Contains(server.Requests, request =>
            request.Method == "GET" &&
            request.Path == "/rest/services/assets/FeatureServer/0/query" &&
            request.Header("X-API-Key") == "integration-api-key");
    }

    [Fact]
    public async Task FeatureAttachments_RoundTripAgainstServerEndpoints()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        using var client = CreateClient(server);
        var source = new FeatureSource { ServiceId = "assets", LayerId = 0 };

        var listed = await client.ListAttachmentsAsync(new FeatureAttachmentListRequest
        {
            Source = source,
            ObjectId = 42,
        });
        Assert.Equal("photo.txt", Assert.Single(listed).Name);

        var downloaded = await client.DownloadAttachmentAsync(new FeatureAttachmentDownloadRequest
        {
            Source = source,
            ObjectId = 42,
            AttachmentId = 7,
        });
        using (downloaded.Content)
        using (var reader = new StreamReader(downloaded.Content, Encoding.UTF8))
        {
            Assert.Equal("photo", await reader.ReadToEndAsync());
        }

        using var addStream = new MemoryStream(Encoding.UTF8.GetBytes("photo"));
        var add = await client.AddAttachmentAsync(new FeatureAttachmentAddRequest
        {
            Source = source,
            ObjectId = 42,
            Name = "photo.txt",
            ContentType = "text/plain",
            Content = addStream,
            Keywords = "field",
        });
        Assert.True(add.Succeeded);

        using var updateStream = new MemoryStream(Encoding.UTF8.GetBytes("updated"));
        var update = await client.UpdateAttachmentAsync(new FeatureAttachmentUpdateRequest
        {
            Source = source,
            ObjectId = 42,
            AttachmentId = 7,
            Name = "photo.txt",
            ContentType = "text/plain",
            Content = updateStream,
        });
        Assert.True(update.Succeeded);

        var delete = await client.DeleteAttachmentAsync(new FeatureAttachmentDeleteRequest
        {
            Source = source,
            ObjectId = 42,
            AttachmentId = 7,
        });
        Assert.True(delete.Succeeded);

        Assert.True(server.Received("GET", "/rest/services/assets/FeatureServer/0/42/attachments"));
        Assert.True(server.Received("GET", "/rest/services/assets/FeatureServer/0/42/attachments/7"));
        Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/0/42/addAttachment"));
        Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/0/42/updateAttachment"));
        Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/0/42/deleteAttachments"));
    }

    [Fact]
    public async Task RefreshingAuthTokenProvider_RefreshesBearerTokenThroughServerEndpoint()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        var store = new InMemoryAuthTokenStore();
        await store.WriteAsync(new HonuaAuthToken(
            HonuaAuthScheme.Bearer,
            "expired-access-token",
            "refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(-5)));
        using var http = new HttpClient { BaseAddress = server.BaseUri };
        var provider = new RefreshingAuthTokenProvider(
            store,
            http,
            new RefreshingAuthTokenProviderOptions
            {
                RefreshEndpoint = server.Uri("/oauth/token"),
                RefreshSkew = TimeSpan.FromMinutes(2),
            });

        var token = await provider.GetTokenAsync();

        Assert.NotNull(token);
        Assert.Equal(HonuaAuthScheme.Bearer, token.Scheme);
        Assert.Equal("refreshed-access-token", token.AccessToken);
        Assert.Equal("next-refresh-token", token.RefreshToken);
        Assert.True(server.Received("POST", "/oauth/token"));
    }

    private static HonuaMobileClient CreateClient(HonuaIntegrationServer server)
    {
        var options = new HonuaMobileClientOptions
        {
            BaseUri = server.BaseUri,
            ApiKey = "integration-api-key",
            BearerToken = "integration-bearer-token",
            AllowInsecureTransportForDevelopment = true,
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
            RoutingServiceId = "Routing",
        };

        return new HonuaMobileClient(new HttpClient(), options);
    }

    private static JsonElement CreateOgcFeature(string id, string name)
    {
        // OgcCreateItemRequest.Feature is a raw JsonElement; previously the test
        // built a strongly-typed OgcFeature, which was an unnecessary mobile-side
        // type now that Honua.Sdk.OgcFeatures.Conversion.RequestConverters
        // accepts the JsonElement payload directly.
        return JsonSerializer.SerializeToElement(new OgcFeature
        {
            Id = JsonSerializer.SerializeToElement(id),
            Properties = new Dictionary<string, JsonElement>
            {
                ["name"] = JsonSerializer.SerializeToElement(name),
            },
            Geometry = JsonSerializer.SerializeToElement(new
            {
                type = "Point",
                coordinates = new[] { -157.8, 21.3 },
            }),
        });
    }
}
