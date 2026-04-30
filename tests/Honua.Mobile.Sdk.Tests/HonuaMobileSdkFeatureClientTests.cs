using System.Net;
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

        Assert.Equal("featureserver", result.ProviderName);
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

        Assert.Equal("featureserver", result.ProviderName);
        Assert.True(result.Succeeded);
        Assert.Equal(42, result.AddResults[0].ObjectId);
        Assert.Equal(7, result.DeleteResults[0].ObjectId);
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
