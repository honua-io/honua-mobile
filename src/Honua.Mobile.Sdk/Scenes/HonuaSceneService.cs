using System.Globalization;
using System.Text.Json;

namespace Honua.Mobile.Sdk.Scenes;

/// <summary>
/// Client for Honua scene metadata discovery and render endpoint resolution.
/// </summary>
public sealed class HonuaSceneService : IHonuaSceneService
{
    private readonly HonuaMobileClient _client;
    private readonly HonuaMobileClientOptions _options;

    internal HonuaSceneService(HonuaMobileClient client, HonuaMobileClientOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HonuaSceneSummary>> ListScenesAsync(
        HonuaSceneListRequest? request = null,
        CancellationToken ct = default)
    {
        request ??= new HonuaSceneListRequest();
        var query = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["capabilities"] = JoinCsv(request.Capabilities),
            ["includeDisabled"] = request.IncludeDisabled?.ToString().ToLowerInvariant(),
        };
        AddAdditionalParameters(query, request.AdditionalParameters);

        using var response = await _client.SendJsonAsync(
            HttpMethod.Get,
            ScenePath(),
            query,
            content: null,
            ct).ConfigureAwait(false);

        return HonuaSceneJsonParser.ParseSceneList(response);
    }

    /// <inheritdoc />
    public async Task<HonuaSceneMetadata> GetSceneAsync(string sceneId, CancellationToken ct = default)
    {
        var resolvedSceneId = RequireSceneId(sceneId);
        using var response = await _client.SendJsonAsync(
            HttpMethod.Get,
            ScenePath(resolvedSceneId),
            new Dictionary<string, string?> { ["f"] = "json" },
            content: null,
            ct).ConfigureAwait(false);

        return HonuaSceneJsonParser.ParseSceneMetadata(response);
    }

    /// <inheritdoc />
    public async Task<HonuaSceneResolution> ResolveSceneAsync(
        string sceneId,
        HonuaSceneResolveRequest? request = null,
        CancellationToken ct = default)
    {
        var resolvedSceneId = RequireSceneId(sceneId);
        request ??= new HonuaSceneResolveRequest();
        var query = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["capabilities"] = JoinCsv(request.RequiredCapabilities),
            ["includeTerrain"] = request.IncludeTerrain.ToString().ToLowerInvariant(),
        };
        AddAdditionalParameters(query, request.AdditionalParameters);

        using var response = await _client.SendJsonAsync(
            HttpMethod.Get,
            ScenePath(resolvedSceneId, "resolve"),
            query,
            content: null,
            ct).ConfigureAwait(false);

        var resolution = HonuaSceneJsonParser.ParseSceneResolution(response, resolvedSceneId);
        EnsureCapabilities(resolution.SceneId, resolution.Capabilities, request.RequiredCapabilities);
        return resolution;
    }

    private string ScenePath(params string[] segments)
    {
        var basePath = string.IsNullOrWhiteSpace(_options.SceneApiPath)
            ? "/api/scenes"
            : _options.SceneApiPath;

        var path = basePath.StartsWith("/", StringComparison.Ordinal) ? basePath : $"/{basePath}";
        path = path.TrimEnd('/');

        foreach (var segment in segments.Where(segment => !string.IsNullOrWhiteSpace(segment)))
        {
            path += $"/{Uri.EscapeDataString(segment)}";
        }

        return path;
    }

    private static string RequireSceneId(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            throw new ArgumentException("Scene id is required.", nameof(sceneId));
        }

        return sceneId.Trim();
    }

    private static void EnsureCapabilities(
        string sceneId,
        IReadOnlyList<string> availableCapabilities,
        IReadOnlyList<string>? requiredCapabilities)
    {
        if (requiredCapabilities is not { Count: > 0 })
        {
            return;
        }

        var available = new HashSet<string>(availableCapabilities, StringComparer.OrdinalIgnoreCase);
        var missing = requiredCapabilities
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Where(capability => !available.Contains(capability))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new HonuaMobileApiException(
                $"Scene '{sceneId}' does not expose required capability: {string.Join(", ", missing)}.");
        }
    }

    private static string? JoinCsv(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
            ? string.Join(',', values.Where(value => !string.IsNullOrWhiteSpace(value)))
            : null;

    private static void AddAdditionalParameters(
        IDictionary<string, string?> query,
        IReadOnlyDictionary<string, string?>? additionalParameters)
    {
        if (additionalParameters is null)
        {
            return;
        }

        foreach (var (key, value) in additionalParameters)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                query[key] = value;
            }
        }
    }
}

internal static class HonuaSceneJsonParser
{
    public static IReadOnlyList<HonuaSceneSummary> ParseSceneList(JsonDocument document)
    {
        try
        {
            var items = EnumerateSceneItems(document.RootElement).ToArray();
            return items.Select(ParseSceneSummary).ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException)
        {
            throw Malformed(ex);
        }
    }

    public static HonuaSceneMetadata ParseSceneMetadata(JsonDocument document)
    {
        try
        {
            return ParseSceneMetadata(document.RootElement);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException)
        {
            throw Malformed(ex);
        }
    }

    public static HonuaSceneResolution ParseSceneResolution(JsonDocument document, string fallbackSceneId)
    {
        try
        {
            var root = document.RootElement;
            var sceneId = GetString(root, "sceneId", "id") ?? fallbackSceneId;
            var tileset = ParseEndpoint(root, HonuaSceneCapabilities.ThreeDimensionalTiles, "tileset", "tilesetUrl");
            var terrain = ParseEndpoint(root, HonuaSceneCapabilities.Terrain, "terrain", "terrainUrl");
            var endpoints = ParseEndpointArray(root)
                .Concat(
                    new[] { tileset, terrain }
                        .Where(endpoint => endpoint is not null)
                        .Cast<HonuaSceneEndpoint>())
                .DistinctBy(endpoint => $"{endpoint.Kind}\n{endpoint.Url}")
                .ToArray();
            var capabilities = ParseCapabilities(root, endpoints);

            return new HonuaSceneResolution
            {
                SceneId = sceneId,
                TilesetUrl = GetUri(root, "tilesetUrl") ?? tileset?.Url ?? FindEndpointUrl(endpoints, HonuaSceneCapabilities.ThreeDimensionalTiles),
                TerrainUrl = GetUri(root, "terrainUrl") ?? terrain?.Url ?? FindEndpointUrl(endpoints, HonuaSceneCapabilities.Terrain),
                Endpoints = endpoints,
                Capabilities = capabilities,
                Auth = ParseAuth(root),
                ExpiresAt = GetDateTimeOffset(root, "expiresAt", "expiration", "validUntil"),
                RawResponse = root.Clone(),
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException)
        {
            throw Malformed(ex);
        }
    }

    private static HonuaSceneSummary ParseSceneSummary(JsonElement element)
    {
        var id = GetString(element, "id", "sceneId")
            ?? throw new InvalidOperationException("Scene item is missing an id.");
        var tileset = ParseEndpoint(element, HonuaSceneCapabilities.ThreeDimensionalTiles, "tileset", "tilesetUrl");
        var terrain = ParseEndpoint(element, HonuaSceneCapabilities.Terrain, "terrain", "terrainUrl");
        var endpoints = new[] { tileset, terrain }.Where(endpoint => endpoint is not null).Cast<HonuaSceneEndpoint>().ToArray();

        return new HonuaSceneSummary
        {
            Id = id,
            Name = GetString(element, "name", "title") ?? id,
            Description = GetString(element, "description"),
            Bounds = ParseBounds(element),
            Capabilities = ParseCapabilities(element, endpoints),
            Attribution = ParseAttribution(element),
            Auth = ParseAuth(element),
            UpdatedAt = GetDateTimeOffset(element, "updatedAt", "modifiedAt", "lastModified"),
            RawResponse = element.Clone(),
        };
    }

    private static HonuaSceneMetadata ParseSceneMetadata(JsonElement element)
    {
        var id = GetString(element, "id", "sceneId")
            ?? throw new InvalidOperationException("Scene metadata is missing an id.");
        var tileset = ParseEndpoint(element, HonuaSceneCapabilities.ThreeDimensionalTiles, "tileset", "tilesetUrl");
        var terrain = ParseEndpoint(element, HonuaSceneCapabilities.Terrain, "terrain", "terrainUrl");
        var endpoints = new[] { tileset, terrain }
            .Where(endpoint => endpoint is not null)
            .Cast<HonuaSceneEndpoint>()
            .ToArray();

        return new HonuaSceneMetadata
        {
            Id = id,
            Name = GetString(element, "name", "title") ?? id,
            Description = GetString(element, "description"),
            Tileset = tileset,
            Terrain = terrain,
            Center = ParseCoordinate(element, "center"),
            Bounds = ParseBounds(element),
            Capabilities = ParseCapabilities(element, endpoints),
            Attribution = ParseAttribution(element),
            Auth = ParseAuth(element),
            Links = ParseLinks(element),
            UpdatedAt = GetDateTimeOffset(element, "updatedAt", "modifiedAt", "lastModified"),
            RawResponse = element.Clone(),
        };
    }

    private static IEnumerable<JsonElement> EnumerateSceneItems(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        foreach (var propertyName in new[] { "scenes", "items", "features" })
        {
            if (TryGetProperty(root, propertyName, out var items) && items.ValueKind == JsonValueKind.Array)
            {
                return items.EnumerateArray();
            }
        }

        throw new InvalidOperationException("Scene list response must contain a scenes array.");
    }

    private static HonuaSceneEndpoint? ParseEndpoint(
        JsonElement root,
        string defaultKind,
        string objectPropertyName,
        string urlPropertyName)
    {
        var inheritedRequiresAuthentication = ParseAuth(root).RequiresAuthentication;

        if (TryGetProperty(root, objectPropertyName, out var endpoint) && endpoint.ValueKind == JsonValueKind.Object)
        {
            return ParseEndpointObject(endpoint, defaultKind, inheritedRequiresAuthentication);
        }

        if (TryGetProperty(root, "endpoints", out var endpoints) &&
            endpoints.ValueKind == JsonValueKind.Object &&
            TryGetProperty(endpoints, objectPropertyName, out endpoint) &&
            endpoint.ValueKind == JsonValueKind.Object)
        {
            return ParseEndpointObject(endpoint, defaultKind, inheritedRequiresAuthentication);
        }

        var url = GetUri(root, urlPropertyName);
        if (url is null)
        {
            return null;
        }

        return new HonuaSceneEndpoint
        {
            Kind = defaultKind,
            Url = url,
            MediaType = defaultKind == HonuaSceneCapabilities.ThreeDimensionalTiles
                ? "application/json"
                : null,
            Format = defaultKind,
            RequiresAuthentication = ParseAuth(root).RequiresAuthentication,
        };
    }

    private static HonuaSceneEndpoint ParseEndpointObject(
        JsonElement endpoint,
        string defaultKind,
        bool inheritedRequiresAuthentication)
    {
        var url = GetUri(endpoint, "url", "href")
            ?? throw new InvalidOperationException($"Scene endpoint '{defaultKind}' is missing a url.");

        return new HonuaSceneEndpoint
        {
            Kind = GetString(endpoint, "kind", "type") ?? defaultKind,
            Url = url,
            MediaType = GetString(endpoint, "mediaType", "contentType"),
            Format = GetString(endpoint, "format") ?? defaultKind,
            RequiresAuthentication = GetBool(endpoint, "requiresAuthentication", "requiresAuth") ?? inheritedRequiresAuthentication,
            Headers = ParseHeaders(endpoint),
        };
    }

    private static IReadOnlyList<HonuaSceneEndpoint> ParseEndpointArray(JsonElement root)
    {
        if (!TryGetProperty(root, "endpoints", out var endpoints) || endpoints.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<HonuaSceneEndpoint>();
        }

        var inheritedRequiresAuthentication = ParseAuth(root).RequiresAuthentication;
        return endpoints.EnumerateArray()
            .Where(endpoint => endpoint.ValueKind == JsonValueKind.Object)
            .Select(endpoint => ParseEndpointObject(
                endpoint,
                GetString(endpoint, "kind", "type") ?? "resource",
                inheritedRequiresAuthentication))
            .ToArray();
    }

    private static Uri? FindEndpointUrl(IReadOnlyList<HonuaSceneEndpoint> endpoints, string kind)
        => endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Kind, kind, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(endpoint.Format, kind, StringComparison.OrdinalIgnoreCase))?.Url;

    private static IReadOnlyDictionary<string, string> ParseHeaders(JsonElement endpoint)
    {
        if (!TryGetProperty(endpoint, "headers", out var headers) || headers.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        return headers.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ParseCapabilities(JsonElement root, IReadOnlyList<HonuaSceneEndpoint> endpoints)
    {
        var capabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetProperty(root, "capabilities", out var value))
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String))
                {
                    AddCapability(capabilities, item.GetString());
                }
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.False)
                    {
                        AddCapability(capabilities, property.Name);
                    }
                }
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                foreach (var capability in SplitCsv(value.GetString()))
                {
                    AddCapability(capabilities, capability);
                }
            }
        }

        foreach (var endpoint in endpoints)
        {
            AddCapability(capabilities, endpoint.Kind);
            AddCapability(capabilities, endpoint.Format);
        }

        return capabilities.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ParseAttribution(JsonElement root)
    {
        foreach (var propertyName in new[] { "attribution", "attributions" })
        {
            if (!TryGetProperty(root, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return new[] { value.GetString() ?? string.Empty };
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
            }
        }

        return Array.Empty<string>();
    }

    private static HonuaSceneAuthRequirements ParseAuth(JsonElement root)
    {
        var auth = TryGetProperty(root, "auth", out var authElement) && authElement.ValueKind == JsonValueKind.Object
            ? authElement
            : root;

        var isPublic = GetBool(auth, "public", "isPublic");
        var requiresAuthentication =
            GetBool(auth, "requiresAuthentication", "requiresAuth", "required") ??
            GetBool(root, "requiresAuthentication", "requiresAuth") ??
            (isPublic.HasValue ? !isPublic.Value : false);

        return new HonuaSceneAuthRequirements
        {
            RequiresAuthentication = requiresAuthentication,
            Schemes = GetStringArray(auth, "schemes", "methods"),
            Policy = GetString(auth, "policy", "policyId"),
        };
    }

    private static HonuaSceneBounds? ParseBounds(JsonElement root)
    {
        if (TryGetProperty(root, "bounds", out var bounds) && bounds.ValueKind == JsonValueKind.Object)
        {
            var minLongitude = GetDouble(bounds, "minLongitude", "west", "xmin");
            var minLatitude = GetDouble(bounds, "minLatitude", "south", "ymin");
            var maxLongitude = GetDouble(bounds, "maxLongitude", "east", "xmax");
            var maxLatitude = GetDouble(bounds, "maxLatitude", "north", "ymax");

            if (minLongitude.HasValue && minLatitude.HasValue && maxLongitude.HasValue && maxLatitude.HasValue)
            {
                return new HonuaSceneBounds
                {
                    MinLongitude = minLongitude.Value,
                    MinLatitude = minLatitude.Value,
                    MaxLongitude = maxLongitude.Value,
                    MaxLatitude = maxLatitude.Value,
                    MinHeight = GetDouble(bounds, "minHeight", "zmin"),
                    MaxHeight = GetDouble(bounds, "maxHeight", "zmax"),
                };
            }
        }

        if (!TryGetProperty(root, "bbox", out var bbox) || bbox.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = bbox.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Number)
            .Select(item => item.GetDouble())
            .ToArray();

        return values.Length switch
        {
            >= 6 => new HonuaSceneBounds
            {
                MinLongitude = values[0],
                MinLatitude = values[1],
                MinHeight = values[2],
                MaxLongitude = values[3],
                MaxLatitude = values[4],
                MaxHeight = values[5],
            },
            >= 4 => new HonuaSceneBounds
            {
                MinLongitude = values[0],
                MinLatitude = values[1],
                MaxLongitude = values[2],
                MaxLatitude = values[3],
            },
            _ => null,
        };
    }

    private static HonuaSceneCoordinate? ParseCoordinate(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var coordinate))
        {
            return null;
        }

        if (coordinate.ValueKind == JsonValueKind.Object)
        {
            var latitude = GetDouble(coordinate, "latitude", "lat", "y");
            var longitude = GetDouble(coordinate, "longitude", "lon", "lng", "x");
            if (latitude.HasValue && longitude.HasValue)
            {
                return new HonuaSceneCoordinate
                {
                    Latitude = latitude.Value,
                    Longitude = longitude.Value,
                    Height = GetDouble(coordinate, "height", "z"),
                };
            }
        }

        if (coordinate.ValueKind == JsonValueKind.Array)
        {
            var values = coordinate.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Number)
                .Select(item => item.GetDouble())
                .ToArray();

            if (values.Length >= 2)
            {
                return new HonuaSceneCoordinate
                {
                    Longitude = values[0],
                    Latitude = values[1],
                    Height = values.Length >= 3 ? values[2] : null,
                };
            }
        }

        return null;
    }

    private static IReadOnlyList<HonuaSceneLink> ParseLinks(JsonElement root)
    {
        if (!TryGetProperty(root, "links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<HonuaSceneLink>();
        }

        return links.EnumerateArray()
            .Where(link => link.ValueKind == JsonValueKind.Object)
            .Select(link =>
            {
                var rel = GetString(link, "rel") ?? "related";
                var href = GetUri(link, "href", "url")
                    ?? throw new InvalidOperationException("Scene link is missing an href.");

                return new HonuaSceneLink
                {
                    Rel = rel,
                    Href = href,
                    Type = GetString(link, "type", "mediaType"),
                    Title = GetString(link, "title"),
                };
            })
            .ToArray();
    }

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return SplitCsv(value.GetString()).ToArray();
            }
        }

        return Array.Empty<string>();
    }

    private static Uri? GetUri(JsonElement element, params string[] propertyNames)
    {
        var value = GetString(element, propertyNames);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri)
            ? uri
            : throw new FormatException($"Invalid scene URL: {value}");
    }

    private static bool? GetBool(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return value.GetBoolean();
            }

            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? GetDouble(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDouble();
            }

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static IEnumerable<string> SplitCsv(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static void AddCapability(ISet<string> capabilities, string? capability)
    {
        if (!string.IsNullOrWhiteSpace(capability))
        {
            capabilities.Add(capability);
        }
    }

    private static HonuaMobileApiException Malformed(Exception ex)
        => new("Honua scene response was malformed.", ex);
}
