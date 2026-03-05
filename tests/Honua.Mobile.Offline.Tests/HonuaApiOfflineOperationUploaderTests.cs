using System.Net;
using System.Text;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;

namespace Honua.Mobile.Offline.Tests;

public sealed class HonuaApiOfflineOperationUploaderTests
{
    [Fact]
    public async Task UploadAsync_FeatureServerAdd_ReturnsSuccess()
    {
        string? postedBody = null;
        var uploader = CreateUploader((request, _) =>
        {
            postedBody = request.Content is null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var body = "{\"addResults\":[{\"success\":true,\"objectId\":1}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        });

        var result = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = """
            {
              "protocol": "FeatureServer",
              "serviceId": "default",
              "layerId": 0,
              "feature": { "attributes": { "asset_id": "A-1" } }
            }
            """,
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.Success, result.Outcome);
        Assert.Contains("adds=", postedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_FeatureServerConflict_ReturnsConflict()
    {
        var uploader = CreateUploader((_, _) =>
        {
            var body = "{\"updateResults\":[{\"success\":false,\"error\":{\"code\":409,\"message\":\"conflict\"}}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        });

        var result = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = """
            {
              "protocol": "FeatureServer",
              "serviceId": "default",
              "layerId": 0,
              "feature": { "attributes": { "objectid": 1, "asset_id": "A-1" } }
            }
            """,
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task UploadAsync_Http503_ReturnsRetryableFailure()
    {
        var uploader = CreateUploader((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("{\"error\":\"unavailable\"}", Encoding.UTF8, "application/json"),
        });

        var result = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Delete,
            PayloadJson = """
            {
              "protocol": "FeatureServer",
              "serviceId": "default",
              "layerId": 0,
              "deleteObjectIds": [1]
            }
            """,
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.RetryableFailure, result.Outcome);
    }

    [Fact]
    public async Task UploadAsync_MalformedDeletePayload_ReturnsFatalFailure()
    {
        var requestCount = 0;
        var uploader = CreateUploader((_, _) =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });

        var result = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Delete,
            PayloadJson = """
            {
              "protocol": "FeatureServer",
              "serviceId": "default",
              "layerId": 0
            }
            """,
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.FatalFailure, result.Outcome);
        Assert.Contains("Delete operation requires", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task UploadAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        var uploader = CreateUploader((_, _) => throw new TaskCanceledException("request canceled"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = """
            {
              "protocol": "FeatureServer",
              "serviceId": "default",
              "layerId": 0,
              "feature": { "attributes": { "asset_id": "A-1" } }
            }
            """,
        }, forceWrite: false, cts.Token));
    }

    private static HonuaApiOfflineOperationUploader CreateUploader(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder)
    {
        var client = new HonuaMobileClient(
            new HttpClient(new StubHttpMessageHandler((request, ct) => Task.FromResult(responder(request, ct))))
            {
                BaseAddress = new Uri("https://api.honua.test"),
            },
            new HonuaMobileClientOptions
            {
                BaseUri = new Uri("https://api.honua.test"),
            });

        return new HonuaApiOfflineOperationUploader(client);
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
