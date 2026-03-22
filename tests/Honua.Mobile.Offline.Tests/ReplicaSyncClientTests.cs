using System.Net;
using System.Text;
using Honua.Mobile.Offline.Sync;

namespace Honua.Mobile.Offline.Tests;

public sealed class ReplicaSyncClientTests
{
    [Fact]
    public async Task CreateReplicaAsync_Success_ReturnsReplicaIdAndServerGen()
    {
        var responseJson = """
        {
            "replicaID": "replica-abc-123",
            "serverGen": 42
        }
        """;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Contains("rest/services/assets/FeatureServer/createReplica", request.RequestUri!.PathAndQuery);
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        var result = await client.CreateReplicaAsync("assets", "test-replica");

        Assert.Equal("replica-abc-123", result.ReplicaId);
        Assert.Equal(42, result.ServerGen);
    }

    [Fact]
    public async Task CreateReplicaAsync_WithLayerIds_SendsLayersParameter()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"replicaID":"r1","serverGen":1}""", Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        await client.CreateReplicaAsync("assets", "test-replica", [0, 3, 5]);

        Assert.NotNull(capturedBody);
        Assert.Contains("layers=0%2C3%2C5", capturedBody);
    }

    [Fact]
    public async Task ExtractChangesAsync_Success_ParsesAddsUpdatesDeletes()
    {
        var responseJson = """
        {
            "serverGen": 55,
            "layerChanges": [
                {
                    "id": 0,
                    "addFeatures": [
                        { "attributes": { "objectid": 1, "name": "New Feature" } }
                    ],
                    "updateFeatures": [
                        { "attributes": { "objectid": 2, "name": "Updated Feature" } }
                    ],
                    "deleteIds": [3, 4]
                }
            ]
        }
        """;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Contains("rest/services/assets/FeatureServer/extractChanges", request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        var result = await client.ExtractChangesAsync("assets", "replica-abc-123");

        Assert.Equal(55, result.ServerGen);
        Assert.Single(result.LayerChanges);

        var layerChange = result.LayerChanges[0];
        Assert.Equal(0, layerChange.LayerId);
        Assert.NotNull(layerChange.AddFeaturesJson);
        Assert.Single(layerChange.AddFeaturesJson);
        Assert.NotNull(layerChange.UpdateFeaturesJson);
        Assert.Single(layerChange.UpdateFeaturesJson);
        Assert.NotNull(layerChange.DeleteIds);
        Assert.Equal([3L, 4L], layerChange.DeleteIds);
    }

    [Fact]
    public async Task ExtractChangesAsync_EmptyChanges_ReturnsEmptyLayerChanges()
    {
        var responseJson = """
        {
            "serverGen": 60,
            "layerChanges": []
        }
        """;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        var result = await client.ExtractChangesAsync("assets", "replica-abc-123");

        Assert.Equal(60, result.ServerGen);
        Assert.Empty(result.LayerChanges);
    }

    [Fact]
    public async Task SynchronizeReplicaAsync_Success_ReturnsSyncResult()
    {
        var responseJson = """
        {
            "serverGen": 100
        }
        """;

        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Contains("rest/services/assets/FeatureServer/synchronizeReplica", request.RequestUri!.PathAndQuery);
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        var result = await client.SynchronizeReplicaAsync("assets", "replica-abc-123");

        Assert.Equal("replica-abc-123", result.ReplicaId);
        Assert.Equal(100, result.ServerGen);
    }

    [Fact]
    public async Task UnRegisterReplicaAsync_Success_CompletesWithoutError()
    {
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            Assert.Contains("rest/services/assets/FeatureServer/unRegisterReplica", request.RequestUri!.PathAndQuery);
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        await client.UnRegisterReplicaAsync("assets", "replica-abc-123");
    }

    [Fact]
    public async Task CreateReplicaAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":{\"message\":\"server error\"}}", Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.CreateReplicaAsync("assets", "test-replica"));
    }

    [Fact]
    public async Task ExtractChangesAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ExtractChangesAsync("assets", "replica-abc-123"));
    }

    [Fact]
    public async Task SynchronizeReplicaAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SynchronizeReplicaAsync("assets", "replica-abc-123"));
    }

    [Fact]
    public async Task UnRegisterReplicaAsync_NonSuccessStatusCode_ThrowsHttpRequestException()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.UnRegisterReplicaAsync("assets", "replica-abc-123"));
    }

    [Fact]
    public async Task CreateReplicaAsync_ServerReturnsError_ThrowsInvalidOperationException()
    {
        var responseJson = """
        {
            "error": {
                "code": 400,
                "message": "Invalid replica name"
            }
        }
        """;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateReplicaAsync("assets", "bad-name"));
        Assert.Contains("Invalid replica name", ex.Message);
    }

    [Fact]
    public async Task ExtractChangesAsync_ServerReturnsError_ThrowsInvalidOperationException()
    {
        var responseJson = """
        {
            "error": {
                "code": 500,
                "message": "Replica not found"
            }
        }
        """;

        var handler = new StubHttpMessageHandler((_, _) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        var client = new ReplicaSyncClient(new HttpClient(handler) { BaseAddress = new Uri("https://api.honua.test") });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExtractChangesAsync("assets", "bad-replica"));
        Assert.Contains("Replica not found", ex.Message);
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
