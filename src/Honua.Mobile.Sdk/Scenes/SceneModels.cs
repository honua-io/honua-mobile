using System.Text.Json;

namespace Honua.Mobile.Sdk.Scenes;

/// <summary>
/// Well-known scene capability identifiers advertised by Honua scene metadata.
/// </summary>
public static class HonuaSceneCapabilities
{
    /// <summary>
    /// Scene exposes an OGC 3D Tiles / Cesium-compatible tileset endpoint.
    /// </summary>
    public const string ThreeDimensionalTiles = "3d-tiles";

    /// <summary>
    /// Scene exposes a terrain provider endpoint.
    /// </summary>
    public const string Terrain = "terrain";

    /// <summary>
    /// Scene exposes an elevation query or profile endpoint.
    /// </summary>
    public const string ElevationProfile = "elevation-profile";

    /// <summary>
    /// Scene exposes an Esri I3S-compatible scene layer endpoint.
    /// </summary>
    public const string I3s = "i3s";
}

/// <summary>
/// Client contract for discovering Honua 3D scene metadata and render-ready endpoint URLs.
/// </summary>
public interface IHonuaSceneService
{
    /// <summary>
    /// Lists scenes visible to the current SDK credentials.
    /// </summary>
    Task<IReadOnlyList<HonuaSceneSummary>> ListScenesAsync(
        HonuaSceneListRequest? request = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets metadata for a single scene.
    /// </summary>
    Task<HonuaSceneMetadata> GetSceneAsync(string sceneId, CancellationToken ct = default);

    /// <summary>
    /// Resolves a scene into client-ready URLs for renderers such as CesiumJS or <c>&lt;honua-scene&gt;</c>.
    /// </summary>
    Task<HonuaSceneResolution> ResolveSceneAsync(
        string sceneId,
        HonuaSceneResolveRequest? request = null,
        CancellationToken ct = default);
}

/// <summary>
/// Filters used when listing scenes from the server.
/// </summary>
public sealed class HonuaSceneListRequest
{
    /// <summary>
    /// Optional capabilities required by the caller, for example <see cref="HonuaSceneCapabilities.ThreeDimensionalTiles"/>.
    /// </summary>
    public IReadOnlyList<string>? Capabilities { get; init; }

    /// <summary>
    /// When set, asks the server whether disabled scenes should be included in the list.
    /// </summary>
    public bool? IncludeDisabled { get; init; }

    /// <summary>
    /// REST response format. Defaults to JSON.
    /// </summary>
    public string ResponseFormat { get; init; } = "json";

    /// <summary>
    /// Additional raw query parameters for server-specific scene registry extensions.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? AdditionalParameters { get; init; }
}

/// <summary>
/// Options used when resolving render-ready scene endpoints.
/// </summary>
public sealed class HonuaSceneResolveRequest
{
    /// <summary>
    /// Capabilities the resolved scene must expose before it is returned to the caller.
    /// </summary>
    public IReadOnlyList<string>? RequiredCapabilities { get; init; }

    /// <summary>
    /// Requests terrain endpoint metadata when the scene has terrain. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IncludeTerrain { get; init; } = true;

    /// <summary>
    /// REST response format. Defaults to JSON.
    /// </summary>
    public string ResponseFormat { get; init; } = "json";

    /// <summary>
    /// Additional raw query parameters for server-specific resolution extensions.
    /// </summary>
    public IReadOnlyDictionary<string, string?>? AdditionalParameters { get; init; }
}

/// <summary>
/// Summary metadata for a scene in a catalog list response.
/// </summary>
public sealed class HonuaSceneSummary
{
    /// <summary>
    /// Stable scene identifier used by SDK calls.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional scene description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Spatial bounds for the scene when advertised by the server.
    /// </summary>
    public HonuaSceneBounds? Bounds { get; init; }

    /// <summary>
    /// Capabilities available for this scene, such as <c>3d-tiles</c> or <c>terrain</c>.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Attribution lines callers should display with rendered scene content.
    /// </summary>
    public IReadOnlyList<string> Attribution { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Authentication requirements advertised for the scene.
    /// </summary>
    public HonuaSceneAuthRequirements Auth { get; init; } = HonuaSceneAuthRequirements.None;

    /// <summary>
    /// Server update timestamp when included in metadata.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Raw JSON object for callers that need server-specific metadata not modeled by the SDK yet.
    /// </summary>
    public JsonElement RawResponse { get; init; }
}

/// <summary>
/// Detailed metadata for a single scene.
/// </summary>
public sealed class HonuaSceneMetadata
{
    /// <summary>
    /// Stable scene identifier used by SDK calls.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional scene description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Primary 3D Tiles endpoint, when the scene exposes one.
    /// </summary>
    public HonuaSceneEndpoint? Tileset { get; init; }

    /// <summary>
    /// Terrain endpoint, when the scene exposes one.
    /// </summary>
    public HonuaSceneEndpoint? Terrain { get; init; }

    /// <summary>
    /// Suggested initial camera center for renderers.
    /// </summary>
    public HonuaSceneCoordinate? Center { get; init; }

    /// <summary>
    /// Spatial bounds for the scene when advertised by the server.
    /// </summary>
    public HonuaSceneBounds? Bounds { get; init; }

    /// <summary>
    /// Capabilities available for this scene, such as <c>3d-tiles</c> or <c>terrain</c>.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Attribution lines callers should display with rendered scene content.
    /// </summary>
    public IReadOnlyList<string> Attribution { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Authentication requirements advertised for the scene.
    /// </summary>
    public HonuaSceneAuthRequirements Auth { get; init; } = HonuaSceneAuthRequirements.None;

    /// <summary>
    /// Related metadata or endpoint links advertised by the server.
    /// </summary>
    public IReadOnlyList<HonuaSceneLink> Links { get; init; } = Array.Empty<HonuaSceneLink>();

    /// <summary>
    /// Server update timestamp when included in metadata.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Raw JSON object for callers that need server-specific metadata not modeled by the SDK yet.
    /// </summary>
    public JsonElement RawResponse { get; init; }
}

/// <summary>
/// Render-ready scene URLs resolved by the server for a specific client request.
/// </summary>
public sealed class HonuaSceneResolution
{
    /// <summary>
    /// Stable scene identifier used by SDK calls.
    /// </summary>
    public required string SceneId { get; init; }

    /// <summary>
    /// 3D Tiles tileset URL for CesiumJS clients.
    /// </summary>
    public Uri? TilesetUrl { get; init; }

    /// <summary>
    /// Terrain provider URL for CesiumJS clients.
    /// </summary>
    public Uri? TerrainUrl { get; init; }

    /// <summary>
    /// Expanded endpoint metadata for the resolved scene.
    /// </summary>
    public IReadOnlyList<HonuaSceneEndpoint> Endpoints { get; init; } = Array.Empty<HonuaSceneEndpoint>();

    /// <summary>
    /// Capabilities available in this resolution response.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Authentication requirements advertised for the resolved endpoints.
    /// </summary>
    public HonuaSceneAuthRequirements Auth { get; init; } = HonuaSceneAuthRequirements.None;

    /// <summary>
    /// Optional server-issued expiry for signed render URLs.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Renderer-safe access envelope describing how resolved scene URLs may be used.
    /// </summary>
    public HonuaSceneAccessEnvelope? Access { get; init; }

    /// <summary>
    /// Raw JSON object for callers that need server-specific metadata not modeled by the SDK yet.
    /// </summary>
    public JsonElement RawResponse { get; init; }
}

/// <summary>
/// URL and format metadata for a scene resource such as a 3D Tiles root or terrain provider.
/// </summary>
public sealed class HonuaSceneEndpoint
{
    /// <summary>
    /// Endpoint kind, for example <c>3d-tiles</c>, <c>terrain</c>, or <c>i3s</c>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Absolute or server-relative endpoint URL.
    /// </summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Media type returned by the endpoint when known.
    /// </summary>
    public string? MediaType { get; init; }

    /// <summary>
    /// Format identifier advertised by the endpoint, such as <c>3d-tiles</c> or <c>quantized-mesh</c>.
    /// </summary>
    public string? Format { get; init; }

    /// <summary>
    /// Whether the endpoint requires authentication beyond public URL access.
    /// </summary>
    public bool RequiresAuthentication { get; init; }

    /// <summary>
    /// Server-supplied request headers for clients that support custom resource headers.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Access envelope for this endpoint. When <see langword="null"/>, use the parent scene resolution access envelope.
    /// </summary>
    public HonuaSceneAccessEnvelope? Access { get; init; }
}

/// <summary>
/// Well-known access modes for scene render endpoints.
/// </summary>
public static class HonuaSceneAccessModes
{
    /// <summary>
    /// Public URL that does not require extra renderer authentication.
    /// </summary>
    public const string Public = "public";

    /// <summary>
    /// Short-lived signed URL or signed asset prefix.
    /// </summary>
    public const string SignedUrl = "signed-url";

    /// <summary>
    /// First-party proxy URL used to serve protected scene assets.
    /// </summary>
    public const string Proxy = "proxy";

    /// <summary>
    /// Native-only mode where every nested renderer request can attach custom headers.
    /// </summary>
    public const string Headers = "headers";

    /// <summary>
    /// Returns whether <paramref name="mode"/> is a mode understood by this SDK version.
    /// </summary>
    public static bool IsSupported(string? mode)
        => string.Equals(mode, Public, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, SignedUrl, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, Proxy, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, Headers, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Renderer-safe access metadata for resolved scene URLs.
/// </summary>
public sealed class HonuaSceneAccessEnvelope
{
    /// <summary>
    /// Access mode. Known values are in <see cref="HonuaSceneAccessModes"/>.
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Time when the host should refresh scene access before a long render session reaches hard expiry.
    /// </summary>
    public DateTimeOffset? RefreshAfter { get; init; }

    /// <summary>
    /// Hard expiry for using the resolved renderer URLs.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// CORS policy descriptor advertised by the server, such as <c>public</c> or <c>registered-origins</c>.
    /// </summary>
    public string? CorsMode { get; init; }

    /// <summary>
    /// Renderer cache policy for this access envelope.
    /// </summary>
    public HonuaSceneAccessCachePolicy Cache { get; init; } = HonuaSceneAccessCachePolicy.Empty;

    /// <summary>
    /// Whether custom request headers are allowed for clients that own all nested renderer requests.
    /// </summary>
    public bool CustomHeadersAllowed { get; init; }

    /// <summary>
    /// Server revision or policy key used to revoke previously issued access.
    /// </summary>
    public string? RevocationKey { get; init; }

    /// <summary>
    /// Whether this SDK version understands the advertised mode.
    /// </summary>
    public bool IsSupportedMode => HonuaSceneAccessModes.IsSupported(Mode);

    /// <summary>
    /// Whether the access envelope can be passed to browser or WebView renderers without custom headers.
    /// </summary>
    public bool IsBrowserSafe =>
        string.Equals(Mode, HonuaSceneAccessModes.Public, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Mode, HonuaSceneAccessModes.SignedUrl, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Mode, HonuaSceneAccessModes.Proxy, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns whether the access envelope is expired at <paramref name="utcNow"/>.
    /// </summary>
    public bool IsExpired(DateTimeOffset utcNow) => ExpiresAt.HasValue && utcNow >= ExpiresAt.Value;

    /// <summary>
    /// Returns whether callers should refresh access at <paramref name="utcNow"/>.
    /// </summary>
    public bool ShouldRefresh(DateTimeOffset utcNow) => RefreshAfter.HasValue && utcNow >= RefreshAfter.Value;
}

/// <summary>
/// Cache policy advertised for a resolved scene access envelope.
/// </summary>
public sealed class HonuaSceneAccessCachePolicy
{
    /// <summary>
    /// Empty cache policy used when the server omits cache metadata.
    /// </summary>
    public static HonuaSceneAccessCachePolicy Empty { get; } = new();

    /// <summary>
    /// Whether responses can be stored in a shared/public cache.
    /// </summary>
    public bool? Public { get; init; }

    /// <summary>
    /// Maximum cache lifetime in seconds.
    /// </summary>
    public int? MaxAgeSeconds { get; init; }

    /// <summary>
    /// Stale-while-revalidate cache lifetime in seconds.
    /// </summary>
    public int? StaleWhileRevalidateSeconds { get; init; }

    /// <summary>
    /// Whether renderers and host apps should avoid persistent storage for these responses.
    /// </summary>
    public bool NoStore { get; init; }
}

/// <summary>
/// Authentication requirements for a scene or endpoint.
/// </summary>
public sealed class HonuaSceneAuthRequirements
{
    /// <summary>
    /// Represents a public scene that does not require extra authentication.
    /// </summary>
    public static HonuaSceneAuthRequirements None { get; } = new()
    {
        RequiresAuthentication = false,
        Schemes = Array.Empty<string>(),
    };

    /// <summary>
    /// Whether callers must authenticate to access scene metadata or assets.
    /// </summary>
    public bool RequiresAuthentication { get; init; }

    /// <summary>
    /// Authentication schemes accepted by the server, such as <c>Bearer</c> or <c>ApiKey</c>.
    /// </summary>
    public IReadOnlyList<string> Schemes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional access policy name or identifier.
    /// </summary>
    public string? Policy { get; init; }
}

/// <summary>
/// WGS84 scene coordinate used for suggested camera centers.
/// </summary>
public sealed class HonuaSceneCoordinate
{
    /// <summary>
    /// Latitude in decimal degrees.
    /// </summary>
    public required double Latitude { get; init; }

    /// <summary>
    /// Longitude in decimal degrees.
    /// </summary>
    public required double Longitude { get; init; }

    /// <summary>
    /// Optional height above the ellipsoid, in meters.
    /// </summary>
    public double? Height { get; init; }
}

/// <summary>
/// Spatial extent of a scene in WGS84 degrees, optionally including height range.
/// </summary>
public sealed class HonuaSceneBounds
{
    /// <summary>
    /// Western longitude bound.
    /// </summary>
    public required double MinLongitude { get; init; }

    /// <summary>
    /// Southern latitude bound.
    /// </summary>
    public required double MinLatitude { get; init; }

    /// <summary>
    /// Eastern longitude bound.
    /// </summary>
    public required double MaxLongitude { get; init; }

    /// <summary>
    /// Northern latitude bound.
    /// </summary>
    public required double MaxLatitude { get; init; }

    /// <summary>
    /// Optional minimum height in meters.
    /// </summary>
    public double? MinHeight { get; init; }

    /// <summary>
    /// Optional maximum height in meters.
    /// </summary>
    public double? MaxHeight { get; init; }
}

/// <summary>
/// Related link advertised by scene metadata.
/// </summary>
public sealed class HonuaSceneLink
{
    /// <summary>
    /// Link relation type.
    /// </summary>
    public required string Rel { get; init; }

    /// <summary>
    /// Link URL.
    /// </summary>
    public required Uri Href { get; init; }

    /// <summary>
    /// Media type for the target resource.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Human-readable link title.
    /// </summary>
    public string? Title { get; init; }
}
