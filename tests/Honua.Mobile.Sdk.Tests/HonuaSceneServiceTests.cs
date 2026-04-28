using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Scenes;

namespace Honua.Mobile.Sdk.Tests;

public sealed class HonuaSceneServiceTests
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

        var ex = await Assert.ThrowsAsync<HonuaMobileApiException>(() => client.Scenes.GetSceneAsync("missing"));

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

        var ex = await Assert.ThrowsAsync<HonuaMobileApiException>(() => client.Scenes.ResolveSceneAsync("protected"));

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

        var ex = await Assert.ThrowsAsync<HonuaMobileApiException>(() => client.Scenes.ResolveSceneAsync(
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

        var ex = await Assert.ThrowsAsync<HonuaMobileApiException>(() => client.Scenes.GetSceneAsync("bad"));

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

        var ex = await Assert.ThrowsAsync<HonuaMobileApiException>(() => client.Scenes.ListScenesAsync());

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

    private static string ReadFixture(string name, [CallerFilePath] string sourceFile = "")
    {
        var testDirectory = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException("Unable to resolve test directory.");
        return File.ReadAllText(Path.Combine(testDirectory, "Fixtures", "Scenes", name));
    }

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
