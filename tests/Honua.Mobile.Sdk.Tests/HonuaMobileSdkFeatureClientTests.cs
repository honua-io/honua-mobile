using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Honua.Mobile.Sdk.Features;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Sdk.Tests;

public sealed class HonuaMobileSdkFeatureClientTests
{
    [Fact]
    public async Task QueryAsync_FeatureServerRequest_UsesSdkFeatureContract()
    {
        Uri? capturedUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "objectIdFieldName": "objectid",
                      "count": 1,
                      "features": [
                        {
                          "attributes": { "objectid": 1, "name": "Pump Station" },
                          "geometry": { "x": -157.8, "y": 21.3 }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        });

        var adapter = CreateAdapter(handler);

        var result = await adapter.QueryAsync(new FeatureQueryRequest
        {
            Source = new FeatureSource { ServiceId = "assets", LayerId = 0 },
            Filter = "status = 'open'",
            ObjectIds = [1, 2],
            OutFields = ["objectid", "name"],
            ReturnGeometry = false,
            Offset = 10,
            Limit = 20,
            OrderBy = "name ASC",
        });

        Assert.NotNull(capturedUri);
        var pathAndQuery = capturedUri.PathAndQuery;
        Assert.Contains("/rest/services/assets/FeatureServer/0/query", pathAndQuery);
        Assert.Contains("where=status%20%3D%20%27open%27", pathAndQuery);
        Assert.Contains("objectIds=1%2C2", pathAndQuery);
        Assert.Contains("outFields=objectid%2Cname", pathAndQuery);
        Assert.Contains("returnGeometry=false", pathAndQuery);
        Assert.Contains("resultOffset=10", pathAndQuery);
        Assert.Contains("resultRecordCount=20", pathAndQuery);

        Assert.Equal("geoservices-featureserver", result.ProviderName);
        Assert.Equal(1, result.NumberMatched);
        Assert.Equal(1, result.NumberReturned);
        Assert.Equal("objectid", result.ObjectIdFieldName);
        Assert.Equal("Pump Station", result.Features[0].Attributes["name"].GetString());
    }

    [Fact]
    public async Task ApplyEditsAsync_FeatureServerRequest_UsesSdkFeatureEditContract()
    {
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            capturedBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "addResults": [{ "objectId": 42, "success": true }],
                      "updateResults": [],
                      "deleteResults": [{ "objectId": 7, "success": true }]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        });

        var adapter = CreateAdapter(handler);
        var result = await adapter.ApplyEditsAsync(new FeatureEditRequest
        {
            Source = new FeatureSource { ServiceId = "assets", LayerId = 0 },
            Adds =
            [
                new FeatureEditFeature
                {
                    Attributes = new Dictionary<string, JsonElement>
                    {
                        ["name"] = JsonSerializer.SerializeToElement("Pump Station")
                    },
                    Geometry = JsonSerializer.SerializeToElement(new { x = -157.8, y = 21.3 }),
                }
            ],
            DeleteObjectIds = [7],
            RollbackOnFailure = true,
            ForceWrite = true,
        });

        Assert.NotNull(capturedBody);
        var form = ParseForm(capturedBody);
        Assert.True(bool.Parse(form["rollbackOnFailure"]));
        Assert.True(bool.Parse(form["forceWrite"]));
        Assert.Equal("7", form["deletes"]);
        Assert.Contains("\"name\":\"Pump Station\"", form["adds"]);
        Assert.Contains("\"geometry\"", form["adds"]);

        Assert.Equal("geoservices-featureserver", result.ProviderName);
        Assert.True(result.Succeeded);
        Assert.Equal(42, result.AddResults[0].ObjectId);
        Assert.Equal(7, result.DeleteResults[0].ObjectId);
    }

    [Fact]
    public async Task ApplyEditsAsync_OgcRequest_DeletesObjectIdsThroughSdkFeatureContract()
    {
        var capturedPaths = new List<string>();
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedPaths.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            });
        });

        var adapter = CreateAdapter(handler);
        var result = await adapter.ApplyEditsAsync(new FeatureEditRequest
        {
            Source = new FeatureSource { CollectionId = "buildings" },
            DeleteObjectIds = [42],
        });

        Assert.Contains(capturedPaths, path => path.Contains("/ogc/features/collections/buildings/items/42", StringComparison.Ordinal));
        Assert.True(result.Succeeded);
        Assert.Equal("42", result.DeleteResults[0].Id);
    }

    [Fact]
    public async Task AttachmentOperationsAsync_FeatureServerRequest_UsesSdkAttachmentContract()
    {
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get &&
                path == "/rest/services/assets/FeatureServer/0/42/attachments")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "attachmentInfos": [
                            {
                              "id": 7,
                              "parentObjectId": 42,
                              "name": "photo.txt",
                              "contentType": "text/plain",
                              "size": 5,
                              "keywords": "field"
                            }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            if (request.Method == HttpMethod.Get &&
                path == "/rest/services/assets/FeatureServer/0/42/attachments/7")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(Encoding.UTF8.GetBytes("photo")),
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = "\"photo.txt\"",
                };
                return response;
            }

            if (request.Method == HttpMethod.Post &&
                path == "/rest/services/assets/FeatureServer/0/42/addAttachment")
            {
                var body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                Assert.Contains("name=attachment", body);
                Assert.Contains("filename=photo.txt", body);
                Assert.Contains("field", body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{ "addAttachmentResult": { "objectId": 8, "success": true } }""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            if (request.Method == HttpMethod.Post &&
                path == "/rest/services/assets/FeatureServer/0/42/updateAttachment")
            {
                var body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                Assert.Contains("name=attachmentId", body);
                Assert.Contains("7", body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{ "updateAttachmentResult": { "objectId": 7, "success": true } }""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            if (request.Method == HttpMethod.Post &&
                path == "/rest/services/assets/FeatureServer/0/42/deleteAttachments")
            {
                var body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                Assert.Contains("attachmentIds=7", body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{ "deleteAttachmentResults": [{ "objectId": 7, "success": true }] }""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });

        var adapter = CreateAdapter(handler);
        var attachments = (IHonuaFeatureAttachmentClient)adapter;
        var source = new FeatureSource { ServiceId = "assets", LayerId = 0 };

        Assert.True(attachments.AttachmentCapabilities.SupportsList);
        Assert.True(attachments.AttachmentCapabilities.SupportsDownload);
        Assert.True(attachments.AttachmentCapabilities.SupportsAdd);
        Assert.True(attachments.AttachmentCapabilities.SupportsUpdate);
        Assert.True(attachments.AttachmentCapabilities.SupportsDelete);

        var listed = await attachments.ListAttachmentsAsync(new FeatureAttachmentListRequest
        {
            Source = source,
            ObjectId = 42,
        });
        var info = Assert.Single(listed);
        Assert.Equal(7, info.AttachmentId);
        Assert.Equal("photo.txt", info.Name);

        var downloaded = await attachments.DownloadAttachmentAsync(new FeatureAttachmentDownloadRequest
        {
            Source = source,
            ObjectId = 42,
            AttachmentId = 7,
        });
        using var reader = new StreamReader(downloaded.Content, Encoding.UTF8);
        Assert.Equal("photo", await reader.ReadToEndAsync());
        Assert.Equal("photo.txt", downloaded.Info.Name);

        using var addContent = new MemoryStream(Encoding.UTF8.GetBytes("photo"));
        var addResult = await attachments.AddAttachmentAsync(new FeatureAttachmentAddRequest
        {
            Source = source,
            ObjectId = 42,
            Name = "photo.txt",
            ContentType = "text/plain",
            Content = addContent,
            Keywords = "field",
        });
        Assert.True(addResult.Succeeded);
        Assert.Equal(8, addResult.AttachmentId);
        Assert.True(addContent.CanRead);

        using var updateContent = new MemoryStream(Encoding.UTF8.GetBytes("updated"));
        var updateResult = await attachments.UpdateAttachmentAsync(new FeatureAttachmentUpdateRequest
        {
            Source = source,
            ObjectId = 42,
            AttachmentId = 7,
            Name = "photo.txt",
            ContentType = "text/plain",
            Content = updateContent,
        });
        Assert.True(updateResult.Succeeded);
        Assert.Equal(7, updateResult.AttachmentId);
        Assert.True(updateContent.CanRead);

        var deleteResult = await attachments.DeleteAttachmentAsync(new FeatureAttachmentDeleteRequest
        {
            Source = source,
            ObjectId = 42,
            AttachmentId = 7,
        });
        Assert.True(deleteResult.Succeeded);
        Assert.Equal(7, deleteResult.AttachmentId);
    }

    private static HonuaMobileSdkFeatureClient CreateAdapter(HttpMessageHandler handler)
    {
        var options = new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        };
        var client = new HonuaMobileClient(
            new HttpClient(handler) { BaseAddress = options.BaseUri },
            options);

        return new HonuaMobileSdkFeatureClient(client);
    }

    private static IReadOnlyDictionary<string, string> ParseForm(string body)
        => body
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(pair[0]),
                pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1]).Replace("+", " ", StringComparison.Ordinal) : string.Empty);

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
