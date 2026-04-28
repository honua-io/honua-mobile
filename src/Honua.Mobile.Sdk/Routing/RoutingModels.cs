using System.Text.Json;

namespace Honua.Mobile.Sdk.Routing;

/// <summary>
/// WGS84 coordinate used by routing operations. Longitude maps to X and latitude maps to Y.
/// </summary>
public readonly record struct RoutingCoordinate(double Longitude, double Latitude)
{
    /// <summary>
    /// Creates a coordinate from latitude/longitude ordered values, which is common for GPS APIs.
    /// </summary>
    public static RoutingCoordinate FromLatitudeLongitude(double latitude, double longitude) => new(longitude, latitude);
}

/// <summary>
/// Point input for route stops, facilities, incidents, and service-area centers.
/// </summary>
public sealed class RoutingLocation
{
    /// <summary>
    /// WGS84 point coordinate.
    /// </summary>
    public required RoutingCoordinate Coordinate { get; init; }

    /// <summary>
    /// Optional display name sent as the route stop/facility name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional stable caller identifier.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Additional attributes included with the network-analysis feature.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Attributes { get; init; }

    /// <summary>
    /// Creates a location from longitude/latitude ordered values.
    /// </summary>
    public static RoutingLocation FromLongitudeLatitude(double longitude, double latitude, string? name = null) => new()
    {
        Coordinate = new RoutingCoordinate(longitude, latitude),
        Name = name,
    };

    /// <summary>
    /// Creates a location from latitude/longitude ordered values, which is common for GPS APIs.
    /// </summary>
    public static RoutingLocation FromLatitudeLongitude(double latitude, double longitude, string? name = null) => new()
    {
        Coordinate = RoutingCoordinate.FromLatitudeLongitude(latitude, longitude),
        Name = name,
    };
}

/// <summary>
/// Supplies the current device location to routing builders or callers.
/// Platform packages can implement this with MAUI/Android/iOS GPS APIs.
/// </summary>
public interface IRoutingLocationProvider
{
    /// <summary>
    /// Resolves the current device location.
    /// </summary>
    ValueTask<RoutingLocation> GetCurrentLocationAsync(CancellationToken ct = default);
}

/// <summary>
/// Options shared by route solve and route optimization requests.
/// </summary>
public sealed class RouteSolveOptions
{
    /// <summary>
    /// Override for the NAServer service id. Defaults to <see cref="HonuaMobileClientOptions.RoutingServiceId"/>.
    /// </summary>
    public string? ServiceId { get; init; }

    /// <summary>
    /// Override for the route layer name. Defaults to <see cref="HonuaMobileClientOptions.RoutingRouteLayerName"/>.
    /// </summary>
    public string? RouteLayerName { get; init; }

    /// <summary>
    /// REST response format. Defaults to JSON.
    /// </summary>
    public string ResponseFormat { get; init; } = "json";

    /// <summary>
    /// Requests turn-by-turn directions in the response.
    /// </summary>
    public bool ReturnDirections { get; init; } = true;

    /// <summary>
    /// Requests route geometry in the response.
    /// </summary>
    public bool ReturnRoutes { get; init; } = true;

    /// <summary>
    /// Requests traffic-aware routing when the server/network supports it.
    /// </summary>
    public bool UseTraffic { get; init; }

    /// <summary>
    /// Requests toll-road restrictions when the server/network supports them.
    /// </summary>
    public bool AvoidTolls { get; init; }

    /// <summary>
    /// Requests highway restrictions when the server/network supports them.
    /// </summary>
    public bool AvoidHighways { get; init; }

    /// <summary>
    /// Optional server travel mode name or JSON travel-mode payload.
    /// </summary>
    public string? TravelMode { get; init; }

    /// <summary>
    /// Optional route start time.
    /// </summary>
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>
    /// Direction length units sent to GeoServices-compatible NAServer endpoints.
    /// </summary>
    public string DirectionsLengthUnits { get; init; } = "esriMeters";

    /// <summary>
    /// Route line shape type sent to GeoServices-compatible NAServer endpoints.
    /// </summary>
    public string OutputLines { get; init; } = "esriNAOutputLineTrueShape";

    /// <summary>
    /// Additional raw NAServer form parameters for server-specific routing extensions.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? AdditionalParameters { get; init; }
}

/// <summary>
/// Directions request for point-to-point and multi-stop routing.
/// </summary>
public sealed class RouteDirectionsRequest
{
    public required RoutingLocation Origin { get; init; }

    public required RoutingLocation Destination { get; init; }

    public IReadOnlyList<RoutingLocation>? Waypoints { get; init; }

    public RouteSolveOptions Options { get; init; } = new();
}

/// <summary>
/// Options for optimized multi-stop route requests.
/// </summary>
public sealed class RouteOptimizationOptions
{
    public string? ServiceId { get; init; }

    public string? RouteLayerName { get; init; }

    public string ResponseFormat { get; init; } = "json";

    public bool ReturnDirections { get; init; } = true;

    public bool ReturnRoutes { get; init; } = true;

    public bool UseTraffic { get; init; }

    public bool AvoidTolls { get; init; }

    public bool AvoidHighways { get; init; }

    public string? TravelMode { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public bool PreserveFirstStop { get; init; } = true;

    public bool PreserveLastStop { get; init; } = true;

    public string DirectionsLengthUnits { get; init; } = "esriMeters";

    public string OutputLines { get; init; } = "esriNAOutputLineTrueShape";

    public IReadOnlyDictionary<string, string?>? AdditionalParameters { get; init; }
}

/// <summary>
/// Optimizes a sequence of stops using NAServer route solving with best-sequence enabled.
/// </summary>
public sealed class RouteOptimizationRequest
{
    public required IReadOnlyList<RoutingLocation> Stops { get; init; }

    public RouteOptimizationOptions Options { get; init; } = new();
}

/// <summary>
/// Options for service-area / isochrone requests.
/// </summary>
public sealed class ServiceAreaOptions
{
    public string? ServiceId { get; init; }

    public string? ServiceAreaLayerName { get; init; }

    public string ResponseFormat { get; init; } = "json";

    public string TravelDirection { get; init; } = "esriNATravelDirectionFromFacility";

    public string OutputPolygons { get; init; } = "esriNAOutputPolygonSimplified";

    public bool MergeSimilarPolygonRanges { get; init; } = true;

    public string? TravelMode { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public IReadOnlyDictionary<string, string?>? AdditionalParameters { get; init; }
}

/// <summary>
/// Service-area / isochrone request centered on one location.
/// </summary>
public sealed class ServiceAreaRequest
{
    public required RoutingLocation Center { get; init; }

    public required TimeSpan TravelTime { get; init; }

    public ServiceAreaOptions Options { get; init; } = new();
}

/// <summary>
/// Options for closest-facility network-analysis requests.
/// </summary>
public sealed class ClosestFacilityOptions
{
    public string? ServiceId { get; init; }

    public string? ClosestFacilityLayerName { get; init; }

    public string ResponseFormat { get; init; } = "json";

    public int? TargetFacilityCount { get; init; }

    public string TravelDirection { get; init; } = "esriNATravelDirectionToFacility";

    public bool ReturnDirections { get; init; } = true;

    public bool ReturnRoutes { get; init; } = true;

    public string? TravelMode { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    public IReadOnlyDictionary<string, string?>? AdditionalParameters { get; init; }
}

/// <summary>
/// Finds the nearest facilities for one or more incident locations.
/// </summary>
public sealed class ClosestFacilityRequest
{
    public required IReadOnlyList<RoutingLocation> Incidents { get; init; }

    public required IReadOnlyList<RoutingLocation> Facilities { get; init; }

    public ClosestFacilityOptions Options { get; init; } = new();
}

/// <summary>
/// Summary information parsed from a routing response when present.
/// </summary>
public sealed record RouteSummary(string? Name, double? TotalDistance, TimeSpan? TotalTime);

/// <summary>
/// Turn-by-turn instruction parsed from a routing response when present.
/// </summary>
public sealed record RouteDirectionStep(string? Text, double? Distance, TimeSpan? Time, string? ManeuverType);

/// <summary>
/// Result returned from directions and optimized-route requests.
/// </summary>
public sealed class RouteResult
{
    internal RouteResult(JsonElement rawResponse, IReadOnlyList<RouteSummary> routes, IReadOnlyList<RouteDirectionStep> directions)
    {
        RawResponse = rawResponse;
        Routes = routes;
        Directions = directions;
    }

    /// <summary>
    /// Raw server response, cloned from the response document.
    /// </summary>
    public JsonElement RawResponse { get; }

    /// <summary>
    /// Parsed route summaries, when the response uses GeoServices-compatible route features or direction summaries.
    /// </summary>
    public IReadOnlyList<RouteSummary> Routes { get; }

    /// <summary>
    /// Parsed turn-by-turn directions, when available.
    /// </summary>
    public IReadOnlyList<RouteDirectionStep> Directions { get; }
}

/// <summary>
/// Result returned from service-area requests.
/// </summary>
public sealed class ServiceAreaResult
{
    internal ServiceAreaResult(JsonElement rawResponse)
    {
        RawResponse = rawResponse;
    }

    public JsonElement RawResponse { get; }
}

/// <summary>
/// Result returned from closest-facility requests.
/// </summary>
public sealed class ClosestFacilityResult
{
    internal ClosestFacilityResult(JsonElement rawResponse, IReadOnlyList<RouteSummary> routes, IReadOnlyList<RouteDirectionStep> directions)
    {
        RawResponse = rawResponse;
        Routes = routes;
        Directions = directions;
    }

    public JsonElement RawResponse { get; }

    public IReadOnlyList<RouteSummary> Routes { get; }

    public IReadOnlyList<RouteDirectionStep> Directions { get; }
}
