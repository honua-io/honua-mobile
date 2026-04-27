using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Models;

namespace Honua.Mobile.Sdk.Tests;

public sealed class HonuaMobileClientHttpTests
{
    [Fact]
    public async Task QueryFeaturesAsync_Success_ReturnsEsriJson()
    {
        var responseJson = """
        {
            "objectIdFieldName": "objectid",
            "features": [
                { "attributes": { "objectid": 1, "name": "Pump Station" }, "geometry": { "x": -157.8, "y": 21.3 } }
            ]
        }
        """;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Contains("/rest/services/assets/FeatureServer/0/query", request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler);
        using var result = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        });

        var features = result.RootElement.GetProperty("features");
        Assert.Equal(JsonValueKind.Array, features.ValueKind);
        Assert.Equal(1, features.GetArrayLength());
        Assert.Equal("Pump Station", features[0].GetProperty("attributes").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ApplyEditsAsync_Success_ReturnsEditResults()
    {
        var responseJson = """
        {
            "addResults": [{ "objectId": 42, "success": true }],
            "updateResults": [],
            "deleteResults": []
        }
        """;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Contains("/rest/services/default/FeatureServer/0/applyEdits", request.RequestUri!.PathAndQuery);
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler);
        using var result = await client.ApplyEditsAsync(new ApplyEditsRequest
        {
            ServiceId = "default",
            LayerId = 0,
            AddsJson = """[{"attributes":{"name":"Test"}}]""",
        });

        var addResults = result.RootElement.GetProperty("addResults");
        Assert.Equal(1, addResults.GetArrayLength());
        Assert.True(addResults[0].GetProperty("success").GetBoolean());
        Assert.Equal(42, addResults[0].GetProperty("objectId").GetInt32());
    }

    [Fact]
    public async Task GetOgcCollectionsAsync_Success_ReturnsCollections()
    {
        var responseJson = """
        {
            "collections": [
                { "id": "buildings", "title": "Buildings" },
                { "id": "roads", "title": "Roads" }
            ]
        }
        """;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Contains("/ogc/features/collections", request.RequestUri!.PathAndQuery);
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler);
        using var result = await client.GetOgcCollectionsAsync();

        var collections = result.RootElement.GetProperty("collections");
        Assert.Equal(2, collections.GetArrayLength());
        Assert.Equal("buildings", collections[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task GetOgcItemsAsync_Success_ReturnsFeatureCollection()
    {
        var responseJson = """
        {
            "type": "FeatureCollection",
            "features": [
                { "type": "Feature", "id": "1", "properties": { "name": "HQ" }, "geometry": { "type": "Point", "coordinates": [-157.8, 21.3] } }
            ]
        }
        """;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Contains("/ogc/features/collections/buildings/items", request.RequestUri!.PathAndQuery);
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler);
        using var result = await client.GetOgcItemsAsync(new OgcItemsRequest
        {
            CollectionId = "buildings",
            Limit = 10,
        });

        var features = result.RootElement.GetProperty("features");
        Assert.Equal(1, features.GetArrayLength());
        Assert.Equal("HQ", features[0].GetProperty("properties").GetProperty("name").GetString());
    }

    [Fact]
    public async Task SendJsonAsync_NonSuccessStatusCode_ThrowsHonuaMobileApiException()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"something broke\"}", Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler);
        var ex = await Assert.ThrowsAsync<HonuaMobileApiException>(() => client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        }));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task SendJsonAsync_404_ThrowsHonuaMobileApiException()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler);
        var ex = await Assert.ThrowsAsync<HonuaMobileApiException>(() => client.GetOgcCollectionsAsync());

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithApiKey_SendsApiKeyHeader()
    {
        string? capturedApiKey = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Headers.TryGetValues("X-API-Key", out var values))
            {
                capturedApiKey = values.First();
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"features\":[]}", Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            ApiKey = "my-secret-key",
            PreferGrpcForFeatureQueries = false,
        });

        using var result = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        });

        Assert.Equal("my-secret-key", capturedApiKey);
    }

    [Fact]
    public async Task ApplyEditsAsync_WithBearerToken_SendsAuthorizationHeader()
    {
        string? capturedAuthHeader = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"addResults\":[]}", Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            BearerToken = "jwt-token-123",
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        });

        using var result = await client.ApplyEditsAsync(new ApplyEditsRequest
        {
            ServiceId = "default",
            LayerId = 0,
            AddsJson = "[]",
        });

        Assert.NotNull(capturedAuthHeader);
        Assert.Equal("Bearer jwt-token-123", capturedAuthHeader);
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithAccessTokenProvider_SendsBearerToken()
    {
        string? capturedAuthHeader = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"features\":[]}", Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            AccessTokenProvider = _ => ValueTask.FromResult<string?>("dynamic-token-456"),
            PreferGrpcForFeatureQueries = false,
        });

        using var result = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        });

        Assert.Equal("Bearer dynamic-token-456", capturedAuthHeader);
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithApiKeyAndBearerToken_SendsBothHeaders()
    {
        string? capturedApiKey = null;
        string? capturedAuthHeader = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            if (request.Headers.TryGetValues("X-API-Key", out var values))
            {
                capturedApiKey = values.First();
            }
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"features\":[]}", Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            ApiKey = "api-key-789",
            BearerToken = "bearer-token-xyz",
            PreferGrpcForFeatureQueries = false,
        });

        using var result = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "assets",
            LayerId = 0,
        });

        Assert.Equal("api-key-789", capturedApiKey);
        Assert.Equal("Bearer bearer-token-xyz", capturedAuthHeader);
    }

    private static HonuaMobileClient CreateClient(HttpMessageHandler handler, HonuaMobileClientOptions? options = null)
    {
        options ??= new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        };

        return new HonuaMobileClient(
            new HttpClient(handler)
            {
                BaseAddress = options.BaseUri,
            },
            options);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
