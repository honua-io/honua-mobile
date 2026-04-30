using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Models;

namespace Honua.Mobile.Sdk.Tests;

public sealed class HonuaMobileClientTransportSecurityTests
{
    [Fact]
    public async Task QueryFeaturesAsync_WithApiKeyOverHttp_ThrowsAndDoesNotSendRequest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        }));

        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("http://api.honua.test"),
            ApiKey = "test-key",
            PreferGrpcForFeatureQueries = false,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryFeaturesAsync(CreateQueryRequest()));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithApiKeyOverHttp_AllowsWhenInsecureDevOverrideEnabled()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            Assert.True(request.Headers.Contains("X-API-Key"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"features\":[]}", Encoding.UTF8, "application/json"),
            });
        });

        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("http://api.honua.test"),
            ApiKey = "test-key",
            AllowInsecureTransportForDevelopment = true,
            PreferGrpcForFeatureQueries = false,
        });

        using var result = await client.QueryFeaturesAsync(CreateQueryRequest());
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(JsonValueKind.Array, result.RootElement.GetProperty("features").ValueKind);
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithBearerTokenOverHttp_ThrowsAndDoesNotSendRequest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        }));

        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("http://api.honua.test"),
            AccessTokenProvider = _ => ValueTask.FromResult<string?>("token-from-provider"),
            PreferGrpcForFeatureQueries = false,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryFeaturesAsync(CreateQueryRequest()));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task QueryFeaturesAsync_WithGrpcAuthOverHttp_ThrowsAndDoesNotFallBackToRest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        }));

        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            GrpcEndpoint = new Uri("http://grpc.honua.test"),
            ApiKey = "test-key",
            PreferGrpcForFeatureQueries = true,
            AllowRestFallbackOnGrpcFailure = true,
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.QueryFeaturesAsync(CreateQueryRequest()));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void BuildGrpcClientOptions_WithGrpcAuthOverLoopbackHttp_ThrowsUnlessDevOverrideEnabled()
    {
        var client = CreateClient(new RecordingHandler(CreateEmptyResponse), new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            GrpcEndpoint = new Uri("http://localhost:5000"),
            ApiKey = "test-key",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => client.BuildGrpcClientOptions());
        Assert.Contains("Refusing to send authentication over non-HTTPS transport", exception.Message);
    }

    [Fact]
    public void BuildGrpcClientOptions_WithGrpcAuthOverLoopbackHttp_AllowsWhenDevOverrideEnabled()
    {
        var client = CreateClient(new RecordingHandler(CreateEmptyResponse), new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            GrpcEndpoint = new Uri("http://localhost:5000"),
            ApiKey = "test-key",
            AllowInsecureTransportForDevelopment = true,
        });

        var options = client.BuildGrpcClientOptions();

        Assert.Equal("http://localhost:5000/", options.Address);
        Assert.Equal("test-key", options.ApiKey);
    }

    [Fact]
    public async Task GetGrpcClient_WhenCalledConcurrently_ReturnsSingleSharedClient()
    {
        using var client = CreateClient(new RecordingHandler(CreateEmptyResponse), new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://localhost:5001"),
        });

        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(client.GetGrpcClient))
            .ToArray();

        var clients = await Task.WhenAll(tasks);

        foreach (var grpcClient in clients)
        {
            Assert.Same(clients[0], grpcClient);
        }
    }

    private static QueryFeaturesRequest CreateQueryRequest() => new()
    {
        ServiceId = "assets",
        LayerId = 0,
    };

    private static HonuaMobileClient CreateClient(HttpMessageHandler handler, HonuaMobileClientOptions options)
    {
        return new HonuaMobileClient(
            new HttpClient(handler)
            {
                BaseAddress = options.BaseUri,
            },
            options);
    }

    private static Task<HttpResponseMessage> CreateEmptyResponse(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return _responder(request, cancellationToken);
        }
    }
}
