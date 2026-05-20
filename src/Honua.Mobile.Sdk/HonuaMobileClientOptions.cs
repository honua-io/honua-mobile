using System.Net.Http.Headers;
using System.Reflection;
using Honua.Mobile.Sdk.Auth;

namespace Honua.Mobile.Sdk;

/// <summary>
/// Configuration options for <see cref="HonuaMobileClient"/>.
/// </summary>
public sealed class HonuaMobileClientOptions
{
    private static readonly string SdkAssemblyVersion =
        typeof(HonuaMobileClientOptions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static readonly ProductInfoHeaderValue DefaultUserAgent =
        new("honua-mobile-sdk", SdkAssemblyVersion);

    private readonly ProductInfoHeaderValue? _userAgent;

    /// <summary>
    /// Base URI for the Honua REST API. Defaults to <c>https://api.honua.io</c>.
    /// </summary>
    public Uri BaseUri { get; init; } = new("https://api.honua.io");

    /// <summary>
    /// Optional separate endpoint for gRPC connections. When <see langword="null"/>, <see cref="BaseUri"/> is used.
    /// </summary>
    public Uri? GrpcEndpoint { get; init; }

    /// <summary>
    /// When <see langword="true"/>, feature queries use gRPC if a channel is available. Defaults to <see langword="true"/>.
    /// </summary>
    public bool PreferGrpcForFeatureQueries { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, feature edits use gRPC if a channel is available. Defaults to <see langword="true"/>.
    /// </summary>
    public bool PreferGrpcForFeatureEdits { get; init; } = true;

    /// <summary>
    /// When <see langword="true"/>, the client automatically falls back to REST when a gRPC call fails. Defaults to <see langword="true"/>.
    /// </summary>
    public bool AllowRestFallbackOnGrpcFailure { get; init; } = true;

    /// <summary>
    /// Optional API key sent via the <c>X-API-Key</c> header.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Static bearer token used for authentication. Superseded by <see cref="AccessTokenProvider"/> when set.
    /// </summary>
    public string? BearerToken { get; init; }

    /// <summary>
    /// Async callback that resolves a fresh bearer token on each request. Takes precedence over <see cref="BearerToken"/>.
    /// </summary>
    public Func<CancellationToken, ValueTask<string?>>? AccessTokenProvider { get; init; }

    /// <summary>
    /// Optional first-class auth token provider. When set, the provider can supply API-key or bearer-token credentials
    /// and can refresh bearer tokens before requests.
    /// </summary>
    public IAuthTokenProvider? AuthTokenProvider { get; init; }

    /// <summary>
    /// When <see langword="true"/>, authentication credentials are allowed over plain HTTP.
    /// Use only for local development; never in production.
    /// </summary>
    public bool AllowInsecureTransportForDevelopment { get; init; }

    /// <summary>
    /// HTTP request timeout. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// User-Agent product header sent with every request. Defaults to <c>honua-mobile-sdk/&lt;assembly-version&gt;</c>.
    /// </summary>
    public ProductInfoHeaderValue UserAgent
    {
        get => _userAgent ?? DefaultUserAgent;
        init => _userAgent = value;
    }

    /// <summary>
    /// GeoServices-compatible NAServer service id used by routing APIs.
    /// </summary>
    public string RoutingServiceId { get; init; } = "Routing";

    /// <summary>
    /// NAServer route layer name used for directions and route optimization.
    /// </summary>
    public string RoutingRouteLayerName { get; init; } = "Route";

    /// <summary>
    /// NAServer service-area layer name used for isochrone requests.
    /// </summary>
    public string RoutingServiceAreaLayerName { get; init; } = "ServiceArea";

    /// <summary>
    /// NAServer closest-facility layer name used for nearest-facility requests.
    /// </summary>
    public string RoutingClosestFacilityLayerName { get; init; } = "ClosestFacility";

    /// <summary>
    /// Base REST path for Honua scene metadata discovery endpoints.
    /// </summary>
    public string SceneApiPath { get; init; } = "/api/scenes";
}
