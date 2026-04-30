using System.Net;
using System.Text;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Scenes;
using Honua.Sdk.Scenes.Exceptions;

namespace Honua.Mobile.Sdk.Tests;

public sealed class HonuaSceneClientAdapterTests
{
    [Fact]
    public async Task ListScenesAsync_ParsesSceneCatalogFixture()
    {
        Uri? uri = null;
        var handler = new RecordingHandler((request, _) =>
        {
            uri = request.RequestUri;
            return Task.FromResult(JsonResponse(ReadFixture("list-scenes.json")));
        });
        var client = CreateClient(handler);

        var scenes = await client.Scenes.ListScenesAsync(new HonuaSceneListRequest
        {
            Capabilities = new[] { HonuaSceneCapabilities.ThreeDimensionalTiles },
        });

        Assert.Equal("https://api.honua.test/api/scenes?f=json&capabilities=3d-tiles", uri?.ToString());
        var scene = Assert.Single(scenes);
        Assert.Equal("downtown-honolulu", scene.Id);
        Assert.Equal("Downtown Honolulu", scene.Name);
        Assert.Contains(HonuaSceneCapabilities.ThreeDimensionalTiles, scene.Capabilities);
        Assert.Contains(HonuaSceneCapabilities.Terrain, scene.Capabilities);
        Assert.True(scene.Auth.RequiresAuthentication);
        Assert.Equal(-157.875, scene.Bounds!.MinLongitude, precision: 3);
        Assert.Equal(21.325, scene.Bounds.MaxLatitude, precision: 3);
    }

    [Fact]
    public async Task GetSceneAsync_ParsesEndpointMetadata()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse(ReadFixture("scene-metadata.json")));
        });
        var client = CreateClient(handler);

        var scene = await client.Scenes.GetSceneAsync("downtown-honolulu");

        Assert.Equal("downtown-honolulu", scene.Id);
        Assert.Equal(new Uri("https://api.honua.test/api/scenes/downtown-honolulu/tileset.json"), scene.Tileset!.Url);
        Assert.Equal("quantized-mesh", scene.Terrain!.Format);
        Assert.Equal(21.3069, scene.Center!.Latitude, precision: 4);
        Assert.Single(scene.Links);
    }

    [Fact]
    public async Task ResolveSceneAsync_ReturnsClientReadyUrls()
    {
        Uri? uri = null;
        var handler = new RecordingHandler((request, _) =>
        {
            uri = request.RequestUri;
            return Task.FromResult(JsonResponse(ReadFixture("resolve-scene.json")));
        });
        var client = CreateClient(handler);

        var resolution = await client.Scenes.ResolveSceneAsync(
            "downtown-honolulu",
            new HonuaSceneResolveRequest
            {
                RequiredCapabilities = new[] { HonuaSceneCapabilities.ThreeDimensionalTiles },
            });

        Assert.Equal("https://api.honua.test/api/scenes/downtown-honolulu/resolve?f=json&capabilities=3d-tiles&includeTerrain=true", uri?.ToString());
        Assert.Equal("downtown-honolulu", resolution.SceneId);
        Assert.Equal(new Uri("https://api.honua.test/api/scenes/downtown-honolulu/tileset.json?sig=test"), resolution.TilesetUrl);
        Assert.Equal(new Uri("https://api.honua.test/api/scenes/downtown-honolulu/terrain?sig=test"), resolution.TerrainUrl);
        Assert.True(resolution.Auth.RequiresAuthentication);
        Assert.Equal(2, resolution.Endpoints.Count);
        Assert.Equal("downtown-honolulu", resolution.Endpoints[0].Headers["X-Honua-Scene"]);
    }

    [Fact]
    public async Task ResolveSceneAsync_ParsesSignedUrlAccessEnvelope()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse(ReadFixture("resolve-access-scene.json")));
        });
        var client = CreateClient(handler);

        var resolution = await client.Scenes.ResolveSceneAsync("protected-downtown");

        Assert.Equal(new DateTimeOffset(2026, 4, 28, 18, 30, 0, TimeSpan.Zero), resolution.ExpiresAt);
        var access = resolution.Access;
        Assert.NotNull(access);
        Assert.Equal(HonuaSceneAccessModes.SignedUrl, access.Mode);
        Assert.True(access.IsSupportedMode);
        Assert.True(access.IsBrowserSafe);
        Assert.False(access.CustomHeadersAllowed);
        Assert.Equal("registered-origins", access.CorsMode);
        Assert.Equal("scene-rev-42", access.RevocationKey);
        Assert.Equal(300, access.Cache.MaxAgeSeconds);
        Assert.Equal(60, access.Cache.StaleWhileRevalidateSeconds);
        Assert.False(access.Cache.Public);
        Assert.True(access.ShouldRefresh(new DateTimeOffset(2026, 4, 28, 18, 21, 0, TimeSpan.Zero)));
        Assert.False(access.IsExpired(new DateTimeOffset(2026, 4, 28, 18, 21, 0, TimeSpan.Zero)));
        Assert.All(resolution.Endpoints, endpoint => Assert.Same(access, endpoint.Access));
    }

    [Theory]
    [InlineData("public", true, true, false)]
    [InlineData("signedUrl", true, true, false)]
    [InlineData("proxy", true, true, false)]
    [InlineData("headers", false, true, true)]
    [InlineData("device-cert", false, false, false)]
    public async Task ResolveSceneAsync_ParsesAccessModeMetadata(
        string mode,
        bool browserSafe,
        bool supported,
        bool customHeadersAllowed)
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse(AccessModeFixture(mode, customHeadersAllowed)));
        });
        var client = CreateClient(handler);

        var resolution = await client.Scenes.ResolveSceneAsync("mode-test");

        var access = resolution.Access;
        Assert.NotNull(access);
        Assert.Equal(browserSafe, access.IsBrowserSafe);
        Assert.Equal(supported, access.IsSupportedMode);
        Assert.Equal(customHeadersAllowed, access.CustomHeadersAllowed);
    }

    [Fact]
    public async Task ResolveSceneAsync_EndpointTypeDoesNotOverrideInheritedAccess()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse("""
                {
                  "sceneId": "endpoint-type-inheritance",
                  "access": {
                    "mode": "signed-url",
                    "expiresAtUtc": "2026-04-28T18:30:00Z"
                  },
                  "endpoints": [
                    {
                      "type": "3d-tiles",
                      "url": "https://api.honua.test/api/scenes/endpoint-type-inheritance/tileset.json",
                      "format": "3d-tiles",
                      "requiresAuthentication": true
                    }
                  ]
                }
                """));
        });
        var client = CreateClient(handler);

        var resolution = await client.Scenes.ResolveSceneAsync("endpoint-type-inheritance");

        Assert.Equal(HonuaSceneAccessModes.SignedUrl, resolution.Access!.Mode);
        var endpoint = Assert.Single(resolution.Endpoints);
        Assert.Equal(HonuaSceneCapabilities.ThreeDimensionalTiles, endpoint.Kind);
        Assert.Same(resolution.Access, endpoint.Access);
        Assert.True(endpoint.Access!.IsBrowserSafe);
    }

    [Fact]
    public async Task ResolveSceneAsync_EndpointAccessOverridesRootForNativeHeaders()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse("""
                {
                  "sceneId": "native-header-scene",
                  "access": {
                    "mode": "signed-url",
                    "expiresAtUtc": "2026-04-28T18:30:00Z"
                  },
                  "endpoints": [
                    {
                      "kind": "3d-tiles",
                      "url": "https://api.honua.test/api/scenes/native-header-scene/tileset.json",
                      "format": "3d-tiles",
                      "requiresAuthentication": true,
                      "headers": {
                        "X-Honua-Scene": "native-header-scene"
                      },
                      "access": {
                        "mode": "headers",
                        "customHeadersAllowed": true,
                        "corsMode": "native-only"
                      }
                    }
                  ]
                }
                """));
        });
        var client = CreateClient(handler);

        var resolution = await client.Scenes.ResolveSceneAsync("native-header-scene");

        Assert.Equal(HonuaSceneAccessModes.SignedUrl, resolution.Access!.Mode);
        var endpoint = Assert.Single(resolution.Endpoints);
        var endpointAccess = endpoint.Access;
        Assert.NotNull(endpointAccess);
        Assert.Equal(HonuaSceneAccessModes.Headers, endpointAccess.Mode);
        Assert.True(endpointAccess.CustomHeadersAllowed);
        Assert.False(endpointAccess.IsBrowserSafe);
        Assert.Equal("native-only", endpointAccess.CorsMode);
        Assert.Equal("native-header-scene", endpoint.Headers["X-Honua-Scene"]);
    }

    [Fact]
    public async Task ResolveSceneAsync_ExpiredAccessEnvelope_ReportsExpiredAndRefreshDue()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse("""
                {
                  "sceneId": "expired-access",
                  "tilesetUrl": "https://cdn.honua.test/scenes/expired/tileset.json?sig=old",
                  "capabilities": ["3d-tiles"],
                  "access": {
                    "mode": "signed-url",
                    "refreshAfterUtc": "2026-04-28T16:55:00Z",
                    "expiresAtUtc": "2026-04-28T17:00:00Z"
                  }
                }
                """));
        });
        var client = CreateClient(handler);

        var resolution = await client.Scenes.ResolveSceneAsync("expired-access");

        var access = resolution.Access;
        Assert.NotNull(access);
        var now = new DateTimeOffset(2026, 4, 28, 17, 1, 0, TimeSpan.Zero);
        Assert.True(access.ShouldRefresh(now));
        Assert.True(access.IsExpired(now));
    }

    [Fact]
    public async Task ResolveSceneAsync_WithEndpointArrayOnly_PopulatesUrlsAndInheritedAuth()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse(ReadFixture("resolve-array-only-scene.json")));
        });
        var client = CreateClient(handler);

        var resolution = await client.Scenes.ResolveSceneAsync(
            "array-only",
            new HonuaSceneResolveRequest
            {
                RequiredCapabilities = new[] { HonuaSceneCapabilities.ThreeDimensionalTiles },
            });

        Assert.Equal(new Uri("https://api.honua.test/api/scenes/array-only/tileset.json"), resolution.TilesetUrl);
        Assert.Equal(new Uri("https://api.honua.test/api/scenes/array-only/terrain"), resolution.TerrainUrl);
        Assert.All(resolution.Endpoints, endpoint => Assert.True(endpoint.RequiresAuthentication));
    }

    [Fact]
    public async Task ResolveSceneAsync_WithSdkCredentials_SendsAuthHeaders()
    {
        string? apiKey = null;
        string? authorization = null;
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.Headers.TryGetValues("X-API-Key", out var values))
            {
                apiKey = values.First();
            }

            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(JsonResponse(ReadFixture("resolve-scene.json")));
        });
        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            ApiKey = "scene-api-key",
            BearerToken = "scene-bearer-token",
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        });

        await client.Scenes.ResolveSceneAsync("downtown-honolulu");

        Assert.Equal("scene-api-key", apiKey);
        Assert.Equal("Bearer scene-bearer-token", authorization);
    }

    [Fact]
    public async Task GetSceneAsync_NotFound_ThrowsApiException()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"title":"Scene not found"}""", Encoding.UTF8, "application/problem+json"),
            });
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<HonuaSceneException>(() => client.Scenes.GetSceneAsync("missing"));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task ResolveSceneAsync_Unauthorized_ThrowsApiException()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"title":"Authentication required"}""", Encoding.UTF8, "application/problem+json"),
            });
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<HonuaSceneException>(() => client.Scenes.ResolveSceneAsync("protected"));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.NotNull(ex.ResponseBody);
        Assert.Contains("Authentication required", ex.ResponseBody);
    }

    [Fact]
    public async Task ResolveSceneAsync_MissingRequiredCapability_ThrowsApiException()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse(ReadFixture("unsupported-scene.json")));
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<HonuaSceneException>(() => client.Scenes.ResolveSceneAsync(
            "terrain-only",
            new HonuaSceneResolveRequest
            {
                RequiredCapabilities = new[] { HonuaSceneCapabilities.ThreeDimensionalTiles },
            }));

        Assert.Contains("3d-tiles", ex.Message);
    }

    [Fact]
    public async Task GetSceneAsync_MalformedResponse_ThrowsApiException()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse(ReadFixture("malformed-scene.json")));
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<HonuaSceneException>(() => client.Scenes.GetSceneAsync("bad"));

        Assert.Contains("malformed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListScenesAsync_InvalidJson_ThrowsApiException()
    {
        var handler = new RecordingHandler((_, _) =>
        {
            return Task.FromResult(JsonResponse("not-json"));
        });
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<HonuaSceneException>(() => client.Scenes.ListScenesAsync());

        Assert.Contains("invalid JSON", ex.Message);
    }

    private static HonuaMobileClient CreateClient(
        HttpMessageHandler handler,
        HonuaMobileClientOptions? options = null)
    {
        options ??= new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        };

        return new HonuaMobileClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string AccessModeFixture(string mode, bool customHeadersAllowed)
    {
        var headers = customHeadersAllowed ? "true" : "false";
        return $$"""
            {
              "sceneId": "mode-test",
              "tilesetUrl": "https://cdn.honua.test/scenes/mode-test/tileset.json",
              "capabilities": ["3d-tiles"],
              "access": {
                "mode": "{{mode}}",
                "customHeadersAllowed": {{headers}}
              }
            }
            """;
    }

    private static string ReadFixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Scenes", name));

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }
}
