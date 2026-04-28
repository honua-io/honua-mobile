using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Routing;

namespace Honua.Mobile.Sdk.Tests;

public sealed class HonuaRoutingClientTests
{
    [Fact]
    public async Task GetDirectionsAsync_SendsNasServerSolvePayloadAndParsesDirections()
    {
        Dictionary<string, string>? form = null;
        HttpMethod? method = null;
        Uri? uri = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            method = request.Method;
            uri = request.RequestUri;
            form = await ReadFormAsync(request, ct);
            return JsonResponse("""
            {
              "routes": {
                "features": [
                  { "attributes": { "Name": "Route 1", "Total_Length": 10.5, "Total_Time": 25 } }
                ]
              },
              "directions": [
                {
                  "features": [
                    { "attributes": { "text": "Head east", "length": 0.1, "time": 1.2, "maneuverType": "esriDMTDepart" } }
                  ]
                }
              ]
            }
            """);
        });
        var client = CreateClient(handler);

        var result = await client.Routing.GetDirectionsAsync(
            RoutingLocation.FromLongitudeLatitude(-157.8583, 21.3069, "Start"),
            RoutingLocation.FromLongitudeLatitude(-157.8037, 21.2810, "Finish"),
            new[] { RoutingLocation.FromLongitudeLatitude(-157.84, 21.29, "Waypoint") });

        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("https://api.honua.test/rest/services/Routing/NAServer/Route/solve", uri?.ToString());
        Assert.NotNull(form);
        Assert.Equal("json", form["f"]);
        Assert.Equal("true", form["returnDirections"]);
        Assert.Equal("false", form["findBestSequence"]);

        using var stops = JsonDocument.Parse(form["stops"]);
        var features = stops.RootElement.GetProperty("features").EnumerateArray().ToArray();
        Assert.Equal(3, features.Length);
        Assert.Equal("Start", features[0].GetProperty("attributes").GetProperty("Name").GetString());
        Assert.Equal(-157.8583, features[0].GetProperty("geometry").GetProperty("x").GetDouble(), precision: 4);

        Assert.Single(result.Routes);
        Assert.Equal("Route 1", result.Routes[0].Name);
        Assert.Single(result.Directions);
        Assert.Equal("Head east", result.Directions[0].Text);
        Assert.Equal(TimeSpan.FromMinutes(1.2), result.Directions[0].Time);
    }

    [Fact]
    public async Task OptimizeRouteAsync_SendsBestSequenceParameters()
    {
        Dictionary<string, string>? form = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            form = await ReadFormAsync(request, ct);
            return JsonResponse("{}");
        });
        var client = CreateClient(handler);

        await client.Routing.OptimizeRouteAsync(
            new[]
            {
                RoutingLocation.FromLongitudeLatitude(-157.86, 21.30, "A"),
                RoutingLocation.FromLongitudeLatitude(-157.84, 21.31, "B"),
                RoutingLocation.FromLongitudeLatitude(-157.82, 21.32, "C"),
            },
            new RouteOptimizationOptions
            {
                PreserveFirstStop = true,
                PreserveLastStop = false,
            });

        Assert.NotNull(form);
        Assert.Equal("true", form["findBestSequence"]);
        Assert.Equal("true", form["preserveFirstStop"]);
        Assert.Equal("false", form["preserveLastStop"]);
    }

    [Fact]
    public async Task GetServiceAreaAsync_SendsTravelTimeBreak()
    {
        Dictionary<string, string>? form = null;
        Uri? uri = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            uri = request.RequestUri;
            form = await ReadFormAsync(request, ct);
            return JsonResponse("{}");
        });
        var client = CreateClient(handler);

        await client.Routing.GetServiceAreaAsync(
            RoutingLocation.FromLongitudeLatitude(-157.8583, 21.3069, "Depot"),
            TimeSpan.FromMinutes(30));

        Assert.Equal("https://api.honua.test/rest/services/Routing/NAServer/ServiceArea/solveServiceArea", uri?.ToString());
        Assert.NotNull(form);
        Assert.Equal("30", form["defaultBreaks"]);
        Assert.Equal("esriNATravelDirectionFromFacility", form["travelDirection"]);
        using var facilities = JsonDocument.Parse(form["facilities"]);
        Assert.Equal("Depot", facilities.RootElement.GetProperty("features")[0].GetProperty("attributes").GetProperty("Name").GetString());
    }

    [Fact]
    public async Task FindClosestFacilityAsync_SendsIncidentAndFacilityFeatureSets()
    {
        Dictionary<string, string>? form = null;
        Uri? uri = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            uri = request.RequestUri;
            form = await ReadFormAsync(request, ct);
            return JsonResponse("{}");
        });
        var client = CreateClient(handler);

        await client.Routing.FindClosestFacilityAsync(
            new[] { RoutingLocation.FromLongitudeLatitude(-157.85, 21.30, "Incident") },
            new[]
            {
                RoutingLocation.FromLongitudeLatitude(-157.80, 21.28, "Facility A"),
                RoutingLocation.FromLongitudeLatitude(-157.82, 21.31, "Facility B"),
            },
            new ClosestFacilityOptions { TargetFacilityCount = 1 });

        Assert.Equal("https://api.honua.test/rest/services/Routing/NAServer/ClosestFacility/solveClosestFacility", uri?.ToString());
        Assert.NotNull(form);
        Assert.Equal("1", form["defaultTargetFacilityCount"]);
        using var incidents = JsonDocument.Parse(form["incidents"]);
        using var facilities = JsonDocument.Parse(form["facilities"]);
        Assert.Single(incidents.RootElement.GetProperty("features").EnumerateArray());
        Assert.Equal(2, facilities.RootElement.GetProperty("features").EnumerateArray().Count());
    }

    [Fact]
    public async Task FindClosestFacilityAsync_UsesDirectionSummaryWhenRoutesAreOmitted()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("""
        {
          "directions": [
            {
              "summary": { "routeName": "Incident - Facility A", "totalLength": 2.5, "totalTime": 8 }
            }
          ]
        }
        """)));
        var client = CreateClient(handler);

        var result = await client.Routing.FindClosestFacilityAsync(
            new[] { RoutingLocation.FromLongitudeLatitude(-157.85, 21.30, "Incident") },
            new[] { RoutingLocation.FromLongitudeLatitude(-157.80, 21.28, "Facility A") },
            new ClosestFacilityOptions { ReturnRoutes = false });

        Assert.Single(result.Routes);
        Assert.Equal("Incident - Facility A", result.Routes[0].Name);
        Assert.Equal(2.5, result.Routes[0].TotalDistance);
        Assert.Equal(TimeSpan.FromMinutes(8), result.Routes[0].TotalTime);
    }

    [Fact]
    public async Task RouteBuilder_AppliesTrafficAndRestrictions()
    {
        Dictionary<string, string>? form = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            form = await ReadFormAsync(request, ct);
            return JsonResponse("{}");
        });
        var client = CreateClient(handler);

        await client.Routing.Route()
            .From(RoutingLocation.FromLongitudeLatitude(-157.8583, 21.3069))
            .Via(RoutingLocation.FromLongitudeLatitude(-157.84, 21.29))
            .To(RoutingLocation.FromLongitudeLatitude(-157.8037, 21.2810))
            .WithTraffic()
            .AvoidTolls()
            .ExecuteAsync();

        Assert.NotNull(form);
        Assert.Equal("true", form["useTraffic"]);
        Assert.Equal("true", form["avoidTolls"]);
        using var stops = JsonDocument.Parse(form["stops"]);
        Assert.Equal(3, stops.RootElement.GetProperty("features").EnumerateArray().Count());
    }

    [Fact]
    public async Task RoutingCall_WithApiKeyOverHttp_ThrowsAndDoesNotSendRequest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse("{}")));
        var client = CreateClient(handler, new HonuaMobileClientOptions
        {
            BaseUri = new Uri("http://api.honua.test"),
            ApiKey = "test-key",
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Routing.GetDirectionsAsync(
            RoutingLocation.FromLongitudeLatitude(-157.8583, 21.3069),
            RoutingLocation.FromLongitudeLatitude(-157.8037, 21.2810)));
        Assert.Equal(0, handler.RequestCount);
    }

    private static HonuaMobileClient CreateClient(
        HttpMessageHandler handler,
        HonuaMobileClientOptions? options = null)
    {
        options ??= new HonuaMobileClientOptions
        {
            BaseUri = new Uri("https://api.honua.test"),
            RoutingServiceId = "Routing",
        };

        return new HonuaMobileClient(new HttpClient(handler), options);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static async Task<Dictionary<string, string>> ReadFormAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = await request.Content!.ReadAsStringAsync(ct);
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Decode(pair[0]),
                pair => pair.Length == 2 ? Decode(pair[1]) : string.Empty,
                StringComparer.Ordinal);
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", " "));

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
