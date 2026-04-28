using System.Globalization;
using System.Text.Json;

namespace Honua.Mobile.Sdk.Routing;

/// <summary>
/// Client for Honua routing and network-analysis operations.
/// </summary>
public sealed class HonuaRoutingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly HonuaMobileClient _client;
    private readonly HonuaMobileClientOptions _options;

    internal HonuaRoutingClient(HonuaMobileClient client, HonuaMobileClientOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Creates a fluent directions builder.
    /// </summary>
    public RouteDirectionsBuilder Route() => new(this);

    /// <summary>
    /// Gets directions from an origin to a destination with optional waypoints.
    /// </summary>
    public Task<RouteResult> GetDirectionsAsync(
        RoutingLocation origin,
        RoutingLocation destination,
        IReadOnlyList<RoutingLocation>? waypoints = null,
        RouteSolveOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(destination);

        return GetDirectionsAsync(
            new RouteDirectionsRequest
            {
                Origin = origin,
                Destination = destination,
                Waypoints = waypoints,
                Options = options ?? new RouteSolveOptions(),
            },
            ct);
    }

    /// <summary>
    /// Gets directions from the current location resolved by <paramref name="locationProvider"/>.
    /// </summary>
    public async Task<RouteResult> GetDirectionsFromCurrentLocationAsync(
        IRoutingLocationProvider locationProvider,
        RoutingLocation destination,
        IReadOnlyList<RoutingLocation>? waypoints = null,
        RouteSolveOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(locationProvider);
        ArgumentNullException.ThrowIfNull(destination);

        var origin = await locationProvider.GetCurrentLocationAsync(ct).ConfigureAwait(false);
        return await GetDirectionsAsync(origin, destination, waypoints, options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets directions from an origin to a destination with optional waypoints.
    /// </summary>
    public async Task<RouteResult> GetDirectionsAsync(RouteDirectionsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Origin);
        ArgumentNullException.ThrowIfNull(request.Destination);

        var stops = new List<RoutingLocation> { request.Origin };
        if (request.Waypoints is { Count: > 0 })
        {
            stops.AddRange(request.Waypoints);
        }

        stops.Add(request.Destination);
        ValidateLocations(stops, minCount: 2, nameof(request));

        var options = request.Options ?? new RouteSolveOptions();
        var form = CreateRouteSolveForm(options);
        form["stops"] = SerializeFeatureSet(stops);
        form["findBestSequence"] = "false";

        using var response = await _client.SendJsonAsync(
            HttpMethod.Post,
            BuildNasPath(options.ServiceId, options.RouteLayerName, "solve", _options.RoutingRouteLayerName),
            query: null,
            new FormUrlEncodedContent(WithoutNullValues(form)),
            ct).ConfigureAwait(false);

        return RoutingResultParser.ParseRoute(response);
    }

    /// <summary>
    /// Gets the service area reachable from <paramref name="center"/> within <paramref name="travelTime"/>.
    /// </summary>
    public Task<ServiceAreaResult> GetServiceAreaAsync(
        RoutingLocation center,
        TimeSpan travelTime,
        ServiceAreaOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(center);

        return GetServiceAreaAsync(
            new ServiceAreaRequest
            {
                Center = center,
                TravelTime = travelTime,
                Options = options ?? new ServiceAreaOptions(),
            },
            ct);
    }

    /// <summary>
    /// Gets a service area / isochrone polygon around a center location.
    /// </summary>
    public async Task<ServiceAreaResult> GetServiceAreaAsync(ServiceAreaRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Center);

        if (request.TravelTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Travel time must be greater than zero.");
        }

        ValidateLocations(new[] { request.Center }, minCount: 1, nameof(request));

        var options = request.Options ?? new ServiceAreaOptions();
        var form = new Dictionary<string, string?>
        {
            ["f"] = options.ResponseFormat,
            ["facilities"] = SerializeFeatureSet(new[] { request.Center }),
            ["defaultBreaks"] = request.TravelTime.TotalMinutes.ToString("0.###", CultureInfo.InvariantCulture),
            ["travelDirection"] = options.TravelDirection,
            ["outputPolygons"] = options.OutputPolygons,
            ["mergeSimilarPolygonRanges"] = options.MergeSimilarPolygonRanges ? "true" : "false",
            ["travelMode"] = options.TravelMode,
            ["startTime"] = FormatStartTime(options.StartTime),
        };
        AddAdditionalParameters(form, options.AdditionalParameters);

        using var response = await _client.SendJsonAsync(
            HttpMethod.Post,
            BuildNasPath(options.ServiceId, options.ServiceAreaLayerName, "solveServiceArea", _options.RoutingServiceAreaLayerName),
            query: null,
            new FormUrlEncodedContent(WithoutNullValues(form)),
            ct).ConfigureAwait(false);

        return RoutingResultParser.ParseServiceArea(response);
    }

    /// <summary>
    /// Finds the closest facilities for one or more incident locations.
    /// </summary>
    public Task<ClosestFacilityResult> FindClosestFacilityAsync(
        IReadOnlyList<RoutingLocation> incidents,
        IReadOnlyList<RoutingLocation> facilities,
        ClosestFacilityOptions? options = null,
        CancellationToken ct = default)
    {
        return FindClosestFacilityAsync(
            new ClosestFacilityRequest
            {
                Incidents = incidents,
                Facilities = facilities,
                Options = options ?? new ClosestFacilityOptions(),
            },
            ct);
    }

    /// <summary>
    /// Finds the closest facilities for one or more incident locations.
    /// </summary>
    public async Task<ClosestFacilityResult> FindClosestFacilityAsync(ClosestFacilityRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Incidents);
        ArgumentNullException.ThrowIfNull(request.Facilities);

        ValidateLocations(request.Incidents, minCount: 1, nameof(request.Incidents));
        ValidateLocations(request.Facilities, minCount: 1, nameof(request.Facilities));

        var options = request.Options ?? new ClosestFacilityOptions();
        var form = new Dictionary<string, string?>
        {
            ["f"] = options.ResponseFormat,
            ["incidents"] = SerializeFeatureSet(request.Incidents),
            ["facilities"] = SerializeFeatureSet(request.Facilities),
            ["defaultTargetFacilityCount"] = options.TargetFacilityCount?.ToString(CultureInfo.InvariantCulture),
            ["travelDirection"] = options.TravelDirection,
            ["returnDirections"] = options.ReturnDirections ? "true" : "false",
            ["returnRoutes"] = options.ReturnRoutes ? "true" : "false",
            ["travelMode"] = options.TravelMode,
            ["startTime"] = FormatStartTime(options.StartTime),
        };
        AddAdditionalParameters(form, options.AdditionalParameters);

        using var response = await _client.SendJsonAsync(
            HttpMethod.Post,
            BuildNasPath(
                options.ServiceId,
                options.ClosestFacilityLayerName,
                "solveClosestFacility",
                _options.RoutingClosestFacilityLayerName),
            query: null,
            new FormUrlEncodedContent(WithoutNullValues(form)),
            ct).ConfigureAwait(false);

        return RoutingResultParser.ParseClosestFacility(response);
    }

    /// <summary>
    /// Optimizes the order of route stops using NAServer best-sequence routing.
    /// </summary>
    public Task<RouteResult> OptimizeRouteAsync(
        IReadOnlyList<RoutingLocation> stops,
        RouteOptimizationOptions? options = null,
        CancellationToken ct = default)
    {
        return OptimizeRouteAsync(
            new RouteOptimizationRequest
            {
                Stops = stops,
                Options = options ?? new RouteOptimizationOptions(),
            },
            ct);
    }

    /// <summary>
    /// Optimizes the order of route stops using NAServer best-sequence routing.
    /// </summary>
    public async Task<RouteResult> OptimizeRouteAsync(RouteOptimizationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Stops);
        ValidateLocations(request.Stops, minCount: 2, nameof(request.Stops));

        var options = request.Options ?? new RouteOptimizationOptions();
        var form = CreateRouteOptimizationForm(options);
        form["stops"] = SerializeFeatureSet(request.Stops);

        using var response = await _client.SendJsonAsync(
            HttpMethod.Post,
            BuildNasPath(options.ServiceId, options.RouteLayerName, "solve", _options.RoutingRouteLayerName),
            query: null,
            new FormUrlEncodedContent(WithoutNullValues(form)),
            ct).ConfigureAwait(false);

        return RoutingResultParser.ParseRoute(response);
    }

    private Dictionary<string, string?> CreateRouteSolveForm(RouteSolveOptions options)
    {
        var form = new Dictionary<string, string?>
        {
            ["f"] = options.ResponseFormat,
            ["returnDirections"] = options.ReturnDirections ? "true" : "false",
            ["returnRoutes"] = options.ReturnRoutes ? "true" : "false",
            ["useTraffic"] = options.UseTraffic ? "true" : null,
            ["avoidTolls"] = options.AvoidTolls ? "true" : null,
            ["avoidHighways"] = options.AvoidHighways ? "true" : null,
            ["travelMode"] = options.TravelMode,
            ["startTime"] = FormatStartTime(options.StartTime),
            ["directionsLengthUnits"] = options.DirectionsLengthUnits,
            ["outputLines"] = options.OutputLines,
        };
        AddAdditionalParameters(form, options.AdditionalParameters);
        return form;
    }

    private Dictionary<string, string?> CreateRouteOptimizationForm(RouteOptimizationOptions options)
    {
        var form = new Dictionary<string, string?>
        {
            ["f"] = options.ResponseFormat,
            ["returnDirections"] = options.ReturnDirections ? "true" : "false",
            ["returnRoutes"] = options.ReturnRoutes ? "true" : "false",
            ["findBestSequence"] = "true",
            ["preserveFirstStop"] = options.PreserveFirstStop ? "true" : "false",
            ["preserveLastStop"] = options.PreserveLastStop ? "true" : "false",
            ["useTraffic"] = options.UseTraffic ? "true" : null,
            ["avoidTolls"] = options.AvoidTolls ? "true" : null,
            ["avoidHighways"] = options.AvoidHighways ? "true" : null,
            ["travelMode"] = options.TravelMode,
            ["startTime"] = FormatStartTime(options.StartTime),
            ["directionsLengthUnits"] = options.DirectionsLengthUnits,
            ["outputLines"] = options.OutputLines,
        };
        AddAdditionalParameters(form, options.AdditionalParameters);
        return form;
    }

    private string BuildNasPath(string? serviceId, string? layerName, string operation, string defaultLayerName)
    {
        var resolvedServiceId = string.IsNullOrWhiteSpace(serviceId) ? _options.RoutingServiceId : serviceId;
        var resolvedLayerName = string.IsNullOrWhiteSpace(layerName) ? defaultLayerName : layerName;

        return $"/rest/services/{Uri.EscapeDataString(resolvedServiceId)}/NAServer/{Uri.EscapeDataString(resolvedLayerName)}/{operation}";
    }

    private static string SerializeFeatureSet(IReadOnlyList<RoutingLocation> locations)
    {
        var features = locations.Select(location =>
        {
            var attributes = new Dictionary<string, object?>();
            if (!string.IsNullOrWhiteSpace(location.Id))
            {
                attributes["Id"] = location.Id;
            }

            if (!string.IsNullOrWhiteSpace(location.Name))
            {
                attributes["Name"] = location.Name;
            }

            if (location.Attributes is not null)
            {
                foreach (var attribute in location.Attributes)
                {
                    attributes[attribute.Key] = attribute.Value;
                }
            }

            var feature = new Dictionary<string, object?>
            {
                ["geometry"] = new Dictionary<string, object?>
                {
                    ["x"] = location.Coordinate.Longitude,
                    ["y"] = location.Coordinate.Latitude,
                    ["spatialReference"] = new Dictionary<string, object?> { ["wkid"] = 4326 },
                },
            };

            if (attributes.Count > 0)
            {
                feature["attributes"] = attributes;
            }

            return feature;
        }).ToArray();

        var featureSet = new Dictionary<string, object?>
        {
            ["features"] = features,
            ["spatialReference"] = new Dictionary<string, object?> { ["wkid"] = 4326 },
        };

        return JsonSerializer.Serialize(featureSet, JsonOptions);
    }

    private static void ValidateLocations(IReadOnlyList<RoutingLocation> locations, int minCount, string parameterName)
    {
        if (locations.Count < minCount)
        {
            throw new ArgumentException($"At least {minCount} routing location(s) are required.", parameterName);
        }

        foreach (var location in locations)
        {
            ArgumentNullException.ThrowIfNull(location);
            if (!double.IsFinite(location.Coordinate.Longitude) || !double.IsFinite(location.Coordinate.Latitude))
            {
                throw new ArgumentOutOfRangeException(parameterName, "Routing coordinates must be finite numbers.");
            }

            if (location.Coordinate.Latitude is < -90 or > 90)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Routing latitude must be between -90 and 90.");
            }

            if (location.Coordinate.Longitude is < -180 or > 180)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Routing longitude must be between -180 and 180.");
            }
        }
    }

    private static void AddAdditionalParameters(Dictionary<string, string?> form, IReadOnlyDictionary<string, string?>? additionalParameters)
    {
        if (additionalParameters is null)
        {
            return;
        }

        foreach (var parameter in additionalParameters)
        {
            form[parameter.Key] = parameter.Value;
        }
    }

    private static string? FormatStartTime(DateTimeOffset? startTime)
        => startTime?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static Dictionary<string, string> WithoutNullValues(Dictionary<string, string?> values)
        => values.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);
}

/// <summary>
/// Fluent builder for common directions requests.
/// </summary>
public sealed class RouteDirectionsBuilder
{
    private readonly HonuaRoutingClient _client;
    private readonly List<RoutingLocation> _waypoints = new();
    private RoutingLocation? _origin;
    private RoutingLocation? _destination;
    private RouteSolveOptions _options = new();

    internal RouteDirectionsBuilder(HonuaRoutingClient client)
    {
        _client = client;
    }

    public RouteDirectionsBuilder From(RoutingLocation origin)
    {
        _origin = origin ?? throw new ArgumentNullException(nameof(origin));
        return this;
    }

    public async Task<RouteDirectionsBuilder> FromCurrentLocationAsync(IRoutingLocationProvider locationProvider, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(locationProvider);
        _origin = await locationProvider.GetCurrentLocationAsync(ct).ConfigureAwait(false);
        return this;
    }

    public RouteDirectionsBuilder To(RoutingLocation destination)
    {
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
        return this;
    }

    public RouteDirectionsBuilder Via(RoutingLocation waypoint)
    {
        _waypoints.Add(waypoint ?? throw new ArgumentNullException(nameof(waypoint)));
        return this;
    }

    public RouteDirectionsBuilder WithTraffic()
    {
        _options = CopyOptions(useTraffic: true);
        return this;
    }

    public RouteDirectionsBuilder AvoidTolls()
    {
        _options = CopyOptions(avoidTolls: true);
        return this;
    }

    public RouteDirectionsBuilder AvoidHighways()
    {
        _options = CopyOptions(avoidHighways: true);
        return this;
    }

    public RouteDirectionsBuilder WithTravelMode(string travelMode)
    {
        if (string.IsNullOrWhiteSpace(travelMode))
        {
            throw new ArgumentException("Travel mode cannot be empty.", nameof(travelMode));
        }

        _options = CopyOptions(travelMode: travelMode);
        return this;
    }

    public RouteDirectionsBuilder WithOptions(RouteSolveOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    public Task<RouteResult> ExecuteAsync(CancellationToken ct = default)
    {
        if (_origin is null)
        {
            throw new InvalidOperationException("Route origin has not been set.");
        }

        if (_destination is null)
        {
            throw new InvalidOperationException("Route destination has not been set.");
        }

        return _client.GetDirectionsAsync(_origin, _destination, _waypoints, _options, ct);
    }

    private RouteSolveOptions CopyOptions(
        bool? useTraffic = null,
        bool? avoidTolls = null,
        bool? avoidHighways = null,
        string? travelMode = null)
        => new()
        {
            ServiceId = _options.ServiceId,
            RouteLayerName = _options.RouteLayerName,
            ResponseFormat = _options.ResponseFormat,
            ReturnDirections = _options.ReturnDirections,
            ReturnRoutes = _options.ReturnRoutes,
            UseTraffic = useTraffic ?? _options.UseTraffic,
            AvoidTolls = avoidTolls ?? _options.AvoidTolls,
            AvoidHighways = avoidHighways ?? _options.AvoidHighways,
            TravelMode = travelMode ?? _options.TravelMode,
            StartTime = _options.StartTime,
            DirectionsLengthUnits = _options.DirectionsLengthUnits,
            OutputLines = _options.OutputLines,
            AdditionalParameters = _options.AdditionalParameters,
        };
}

internal static class RoutingResultParser
{
    public static RouteResult ParseRoute(JsonDocument document)
    {
        var directions = ParseDirections(document.RootElement);
        var routes = ParseRouteSummaries(document.RootElement);
        if (routes.Count == 0)
        {
            routes = ParseDirectionSummaries(document.RootElement);
        }

        return new RouteResult(document.RootElement.Clone(), routes, directions);
    }

    public static ServiceAreaResult ParseServiceArea(JsonDocument document)
        => new(document.RootElement.Clone());

    public static ClosestFacilityResult ParseClosestFacility(JsonDocument document)
    {
        var routes = ParseRouteSummaries(document.RootElement);
        if (routes.Count == 0)
        {
            routes = ParseDirectionSummaries(document.RootElement);
        }

        return new ClosestFacilityResult(document.RootElement.Clone(), routes, ParseDirections(document.RootElement));
    }

    private static IReadOnlyList<RouteDirectionStep> ParseDirections(JsonElement root)
    {
        var steps = new List<RouteDirectionStep>();
        if (!TryGetProperty(root, "directions", out var directions))
        {
            return steps;
        }

        foreach (var directionSet in EnumerateObjectOrArray(directions))
        {
            if (!TryGetProperty(directionSet, "features", out var features))
            {
                continue;
            }

            foreach (var feature in EnumerateObjectOrArray(features))
            {
                if (!TryGetProperty(feature, "attributes", out var attributes))
                {
                    continue;
                }

                steps.Add(new RouteDirectionStep(
                    ReadString(attributes, "text", "Text", "maneuverText", "driveInstruction"),
                    ReadDouble(attributes, "length", "Length", "distance", "Distance"),
                    ReadMinutes(attributes, "time", "Time", "minutes", "Minutes"),
                    ReadString(attributes, "maneuverType", "ManeuverType", "type")));
            }
        }

        return steps;
    }

    private static IReadOnlyList<RouteSummary> ParseRouteSummaries(JsonElement root)
    {
        var summaries = new List<RouteSummary>();
        if (!TryGetProperty(root, "routes", out var routes) || !TryGetProperty(routes, "features", out var features))
        {
            return summaries;
        }

        foreach (var feature in EnumerateObjectOrArray(features))
        {
            if (!TryGetProperty(feature, "attributes", out var attributes))
            {
                continue;
            }

            summaries.Add(new RouteSummary(
                ReadString(attributes, "Name", "name", "RouteName"),
                ReadDouble(attributes, "Total_Length", "totalLength", "TotalDistance", "totalDistance"),
                ReadMinutes(attributes, "Total_Time", "totalTime", "Total_TravelTime", "travelTime")));
        }

        return summaries;
    }

    private static IReadOnlyList<RouteSummary> ParseDirectionSummaries(JsonElement root)
    {
        var summaries = new List<RouteSummary>();
        if (!TryGetProperty(root, "directions", out var directions))
        {
            return summaries;
        }

        foreach (var directionSet in EnumerateObjectOrArray(directions))
        {
            if (!TryGetProperty(directionSet, "summary", out var summary))
            {
                continue;
            }

            summaries.Add(new RouteSummary(
                ReadString(summary, "routeName", "RouteName", "Name"),
                ReadDouble(summary, "totalLength", "Total_Length", "totalDistance"),
                ReadMinutes(summary, "totalTime", "Total_Time", "travelTime")));
        }

        return summaries;
    }

    private static IEnumerable<JsonElement> EnumerateObjectOrArray(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
        }
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static double? ReadDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetProperty(element, name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return null;
    }

    private static TimeSpan? ReadMinutes(JsonElement element, params string[] names)
    {
        var value = ReadDouble(element, names);
        return value is null ? null : TimeSpan.FromMinutes(value.Value);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
