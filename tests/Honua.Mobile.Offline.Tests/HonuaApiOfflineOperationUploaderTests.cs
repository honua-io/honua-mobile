using System.Net;
using System.Text;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;

namespace Honua.Mobile.Offline.Tests;

public sealed class HonuaApiOfflineOperationUploaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadAsync_ReplicaBoundFeatureServerEdit_NeverUsesApplyEdits(bool forceWrite)
    {
        var uploader = CreateUploader((_, _) =>
            throw new InvalidOperationException("A replica-bound edit reached the network."));

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
            ReplicaBinding = new OfflineReplicaBinding("replica-A", 10),
        }, forceWrite);

        Assert.Equal(UploadOutcome.FatalFailure, result.Outcome);
        Assert.Equal("Replica-bound edits require a replica-aware offline uploader.", result.Message);
    }

    [Theory]
    [InlineData("""{"type":"Feature","id":""}""")]
    [InlineData("""{"type":"Feature","id":"   "}""")]
    public async Task UploadAsync_OgcCreateWithBlankId_ReturnsNoServerIds(string responseBody)
    {
        var uploader = CreateUploader((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
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
              "feature": { "type": "Feature", "properties": {}, "geometry": { "type": "Point", "coordinates": [0, 0] } }
            }
            """,
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.Success, result.Outcome);
        Assert.Empty(result.ServerFeatureIds);
    }

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
        Assert.Equal(["1"], result.ServerFeatureIds);
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

    [Theory]
    [InlineData("42", "42")]
    [InlineData("\"record-42\"", "record-42")]
    public async Task UploadAsync_OgcAdd_RetainsNumericOrStringServerId(string jsonId, string expectedId)
    {
        var uploader = CreateUploader((_, _) => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("{\"type\":\"Feature\",\"id\":" + jsonId + "}", Encoding.UTF8, "application/geo+json"),
        });

        var result = await uploader.UploadAsync(new OfflineEditOperation
        {
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = """{"protocol":"ogcfeatures","collectionId":"assets","feature":{"type":"Feature","properties":{}}}""",
        }, forceWrite: false);

        Assert.Equal(UploadOutcome.Success, result.Outcome);
        Assert.Equal([expectedId], result.ServerFeatureIds);
    }

    [Fact]
    public async Task UploadAsync_OgcAdd_PostsGeoJsonItemWithIdempotencyKey()
    {
        string? capturedPath = null;
        string? capturedMediaType = null;
        string? capturedBody = null;
        string? capturedIdempotencyKey = null;
        var uploader = CreateUploader((request, _) =>
        {
            capturedPath = request.RequestUri!.PathAndQuery;
            capturedMediaType = request.Content?.Headers.ContentType?.MediaType;
            capturedBody = request.Content is null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedIdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.FirstOrDefault()
                : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"type":"Feature","id":"building-1"}""", Encoding.UTF8, "application/json"),
            };
        });

        var operation = new OfflineEditOperation
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
        };

        var result = await uploader.UploadAsync(operation, forceWrite: false);

        Assert.Equal(UploadOutcome.Success, result.Outcome);
        Assert.Equal(["building-1"], result.ServerFeatureIds);
        Assert.Contains("/ogc/features/collections/buildings/items", capturedPath);
        Assert.Equal("application/geo+json", capturedMediaType);
        // The stored GeoJSON feature is forwarded verbatim (no lossy normalization), so assert the
        // attribute is present rather than a specific compact serialization.
        Assert.Contains("\"name\"", capturedBody);
        Assert.Contains("\"HQ\"", capturedBody);
        // An OGC create is a non-idempotent POST, so it must carry the stable per-operation
        // Idempotency-Key for at-most-once retries (parity with the FeatureServer path).
        Assert.False(string.IsNullOrWhiteSpace(capturedIdempotencyKey));
        Assert.Contains(operation.OperationId, capturedIdempotencyKey!, StringComparison.Ordinal);
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

    [Fact]
    public async Task UploadAsync_FeatureServerPerFeatureUpdateConflictCode_ReturnsConflict()
    {
        // Honua Server returns HTTP 200 with a per-feature error carrying the stable update-update
        // conflict code (1004); it must be routed through conflict resolution, not dropped as fatal.
        var uploader = CreateUploader((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"objectIdField":"objectid"}""", Encoding.UTF8, "application/json"),
                };
            }

            var body = "{\"addResults\":[],\"updateResults\":[{\"success\":false,\"error\":{\"code\":1004,\"description\":\"version mismatch\"}}],\"deleteResults\":[]}";
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
    public async Task UploadAsync_FeatureServer_SendsStableIdempotencyKeyAcrossRetries()
    {
        var capturedKeys = new List<string?>();
        var uploader = CreateUploader((request, _) =>
        {
            capturedKeys.Add(request.Headers.TryGetValues("Idempotency-Key", out var values)
                ? values.FirstOrDefault()
                : null);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"addResults\":[{\"success\":true,\"objectId\":1}],\"updateResults\":[],\"deleteResults\":[]}",
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var operation = new OfflineEditOperation
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
        };

        // Simulate the same queued operation being uploaded twice (e.g. a lost ack on the first try).
        await uploader.UploadAsync(operation, forceWrite: false);
        await uploader.UploadAsync(operation, forceWrite: false);

        Assert.Equal(2, capturedKeys.Count);
        Assert.All(capturedKeys, key => Assert.False(string.IsNullOrWhiteSpace(key)));
        Assert.Equal(capturedKeys[0], capturedKeys[1]);
        Assert.Contains(operation.OperationId, capturedKeys[0]!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterruptedUpload_ReclaimsEncryptedQueueAndReplaysServerResult()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"honua-crash-replay-{Guid.NewGuid():N}.gpkg");
        var clock = new TestTimeProvider();
        var options = new GeoPackageSyncStoreOptions
        {
            DatabasePath = databasePath,
            EncryptionKey = "test-only-crash-replay-key",
            InProgressLeaseTimeout = TimeSpan.FromMinutes(5),
            TimeProvider = clock,
        };
        var operation = new OfflineEditOperation
        {
            OperationId = "offline-capture-42",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = """
            {"protocol":"FeatureServer","serviceId":"default","layerId":0,"feature":{"attributes":{"asset_name":"Offline asset"}}}
            """,
        };
        var serverResults = new Dictionary<string, string>();
        var requestCount = 0;
        var uploader = CreateUploader((request, _) =>
        {
            requestCount++;
            var key = Assert.Single(request.Headers.GetValues("Idempotency-Key"));
            if (!serverResults.TryGetValue(key, out var result))
            {
                var serverObjectId = serverResults.Count + 42;
                result = $$"""{"addResults":[{"success":true,"objectId":{{serverObjectId}}}]}""";
                serverResults.Add(key, result);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(result, Encoding.UTF8, "application/json"),
            };
        });

        try
        {
            var store = new GeoPackageSyncStore(options);
            await store.InitializeAsync();
            const string cachedFeature = """{"attributes":{"OBJECTID":-1,"asset_name":"Offline asset"}}""";
            await store.UpsertFeatureAndEnqueueAsync(operation, cachedFeature);
            var claimed = Assert.Single(await store.GetPendingAsync(1));
            var serverResult = await uploader.UploadAsync(claimed, forceWrite: false);
            Assert.Equal(UploadOutcome.Success, serverResult.Outcome);
            Assert.Equal(["42"], serverResult.ServerFeatureIds);

            // The first process stops after the remote commit and before saving its acknowledgement.
            var reopened = new GeoPackageSyncStore(options);
            var engine = new OfflineSyncEngine(reopened, uploader);
            Assert.Equal(cachedFeature, Assert.Single(await reopened.GetFeaturesAsync("assets")));
            Assert.Null(await reopened.GetUploadAcknowledgementAsync(operation.OperationId));
            Assert.Equal(0, (await engine.SyncAsync()).Loaded);
            Assert.Equal(1, requestCount);

            clock.UtcNow += TimeSpan.FromMinutes(6);
            Assert.Equal(1, (await engine.SyncAsync()).Succeeded);
            Assert.Equal(2, requestCount);
            Assert.Single(serverResults);

            var recovered = new GeoPackageSyncStore(options);
            Assert.Equal(["42"], (await recovered.GetUploadAcknowledgementAsync(operation.OperationId))!.ServerFeatureIds);
            Assert.Equal(0, (await new OfflineSyncEngine(recovered, uploader).SyncAsync()).Loaded);
            Assert.Equal(2, requestCount);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                File.Delete(databasePath + suffix);
            }
        }
    }

    [Fact]
    public async Task UploadAsync_FeatureServerIdempotencyReservationContention_ReturnsRetryableFailure()
    {
        var uploader = CreateUploader((_, _) => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent("""
                {"error":{"code":409,"message":"A concurrent request with the same Idempotency-Key is already being processed. Retry after a brief delay."}}
                """, Encoding.UTF8, "application/json"),
        });
        var operation = new OfflineEditOperation
        {
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = """
                {"protocol":"FeatureServer","serviceId":"default","layerId":0,"feature":{"attributes":{"asset_id":"A-1"}}}
                """,
        };

        var result = await uploader.UploadAsync(operation, forceWrite: false);

        Assert.Equal(UploadOutcome.RetryableFailure, result.Outcome);
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => UtcNow;
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
