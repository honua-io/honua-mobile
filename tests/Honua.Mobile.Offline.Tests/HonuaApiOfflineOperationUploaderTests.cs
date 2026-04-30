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
            var body = "{\"addResults\":[{\"success\":true,\"objectId\":1}],\"updateResults\":[],\"deleteResults\":[]}";
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

        Assert.True(result.Outcome == UploadOutcome.Success, result.Message);
        Assert.Contains("adds=", postedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_FeatureServerConflict_ReturnsConflict()
    {
        var uploader = CreateUploader((request, _) =>
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri!.PathAndQuery.Contains("/rest/services/default/FeatureServer/0", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"objectIdField":"objectid"}""", Encoding.UTF8, "application/json"),
                };
            }

            var body = "{\"addResults\":[],\"updateResults\":[{\"success\":false,\"error\":{\"code\":409,\"message\":\"conflict\"}}],\"deleteResults\":[]}";
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

        Assert.True(result.Outcome == UploadOutcome.Conflict, result.Message);
    }

    [Fact]
    public async Task UploadAsync_FeatureServerEmptyApplyEditsEnvelope_ReturnsFatalFailure()
    {
        var uploader = CreateUploader((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
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

        Assert.Equal(UploadOutcome.FatalFailure, result.Outcome);
        Assert.Contains("malformed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadAsync_FeatureServerFailureWithoutError_ReturnsFatalFailure()
    {
        var uploader = CreateUploader((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"addResults":[{}],"updateResults":[],"deleteResults":[]}""", Encoding.UTF8, "application/json"),
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

        Assert.Equal(UploadOutcome.FatalFailure, result.Outcome);
        Assert.Contains("reported failure", result.Message, StringComparison.OrdinalIgnoreCase);
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
    public async Task UploadAsync_OgcAdd_UsesSdkFeatureEditRequest()
    {
        string? capturedPath = null;
        string? capturedMediaType = null;
        string? capturedBody = null;
        var uploader = CreateUploader((request, _) =>
        {
            capturedPath = request.RequestUri!.PathAndQuery;
            capturedMediaType = request.Content?.Headers.ContentType?.MediaType;
            capturedBody = request.Content is null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"Feature","id":"building-1"}""", Encoding.UTF8, "application/json"),
            };
        });

        var result = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "buildings",
            TargetCollection = "buildings",
            OperationType = OfflineOperationType.Add,
            PayloadJson = """
            {
              "protocol": "ogcfeatures",
              "collectionId": "buildings",
              "feature": {
                "type": "Feature",
                "properties": { "name": "HQ" },
                "geometry": { "type": "Point", "coordinates": [-157.8, 21.3] }
              }
            }
            """,
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.Success, result.Outcome);
        Assert.Contains("/ogc/features/collections/buildings/items", capturedPath);
        Assert.Equal("application/geo+json", capturedMediaType);
        Assert.Contains("\"name\":\"HQ\"", capturedBody);
    }

    [Fact]
    public async Task UploadAsync_OgcPatch_KeepsCompatibilityPatchPath()
    {
        HttpMethod? capturedMethod = null;
        string? capturedPath = null;
        string? capturedMediaType = null;
        var uploader = CreateUploader((request, _) =>
        {
            capturedMethod = request.Method;
            capturedPath = request.RequestUri!.PathAndQuery;
            capturedMediaType = request.Content?.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"Feature","id":"building-1"}""", Encoding.UTF8, "application/json"),
            };
        });

        var result = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "buildings",
            TargetCollection = "buildings",
            OperationType = OfflineOperationType.Update,
            PayloadJson = """
            {
              "protocol": "ogcfeatures",
              "collectionId": "buildings",
              "featureId": "building-1",
              "patch": { "properties": { "name": "HQ" } }
            }
            """,
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.Success, result.Outcome);
        Assert.Equal(HttpMethod.Patch, capturedMethod);
        Assert.Contains("/ogc/features/collections/buildings/items/building-1", capturedPath);
        Assert.Equal("application/merge-patch+json", capturedMediaType);
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
    public async Task UploadAsync_MalformedDeleteCsv_ReturnsFatalFailure()
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
              "layerId": 0,
              "deletesCsv": "1,not-an-id"
            }
            """,
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.FatalFailure, result.Outcome);
        Assert.Contains("invalid object id", result.Message, StringComparison.Ordinal);
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
                PreferGrpcForFeatureEdits = false,
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
