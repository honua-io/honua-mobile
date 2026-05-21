using System.Net;
using System.Net.Http.Headers;
using Honua.Mobile.Sdk.Auth;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Abstractions.Scenes;
using Honua.Sdk.GeoServices;
using Honua.Sdk.GeoServices.FeatureServer;
using Honua.Sdk.GeoServices.FeatureServer.Exceptions;
using Honua.Sdk.GeoServices.Routing;
using Honua.Sdk.Grpc;
using Honua.Sdk.OgcFeatures;
using Honua.Sdk.OgcFeatures.Exceptions;
using Honua.Sdk.Scenes;
using Microsoft.Extensions.Options;

namespace Honua.Mobile.Sdk;

/// <summary>
/// Client for the Honua platform REST and gRPC APIs.
/// Provides feature query/edit operations, OGC Features API access,
/// and automatic transport negotiation between gRPC and REST.
/// </summary>
public sealed partial class HonuaMobileClient : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _sdkHttp;
    private readonly HonuaFeatureServerClient _featureServerClient;
    private readonly HonuaOgcFeaturesClient _ogcFeaturesClient;
    private readonly HonuaMobileClientOptions _options;
    private readonly IAuthTokenProvider? _authTokenProvider;
    private readonly Uri _baseUri;
    private readonly TimeSpan _requestTimeout;
    private readonly ProductInfoHeaderValue _userAgent;
    private readonly bool _canUseGrpcEndpoint;
    private readonly Lazy<HonuaGrpcClient> _grpcClient;
    private int _disposed;

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileClient"/> with the supplied HTTP client and options.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> used for REST requests.</param>
    /// <param name="options">Configuration controlling endpoints, authentication, and transport preferences.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public HonuaMobileClient(HttpClient httpClient, HonuaMobileClientOptions options)
        : this(httpClient, options, authTokenProvider: null)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileClient"/> with the supplied HTTP client, options, and auth token provider.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> used for REST requests.</param>
    /// <param name="options">Configuration controlling endpoints, authentication, and transport preferences.</param>
    /// <param name="authTokenProvider">Optional provider that resolves API-key or bearer-token credentials.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public HonuaMobileClient(HttpClient httpClient, HonuaMobileClientOptions options, IAuthTokenProvider? authTokenProvider)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _authTokenProvider = authTokenProvider ?? options.AuthTokenProvider;
        _baseUri = options.BaseUri;
        _requestTimeout = options.Timeout;
        _userAgent = options.UserAgent;

        _sdkHttp = new HttpClient(
            new AuthenticatedSdkHttpMessageHandler(_http, ApplyHttpAuthenticationAsync),
            disposeHandler: true)
        {
            BaseAddress = options.BaseUri,
            Timeout = options.Timeout,
        };
        _sdkHttp.DefaultRequestHeaders.UserAgent.Clear();
        _sdkHttp.DefaultRequestHeaders.UserAgent.Add(options.UserAgent);

        _featureServerClient = new HonuaFeatureServerClient(_sdkHttp);
        _ogcFeaturesClient = new HonuaOgcFeaturesClient(_sdkHttp);

        var grpcAddress = options.GrpcEndpoint ?? options.BaseUri;
        _canUseGrpcEndpoint = grpcAddress.Scheme is "http" or "https";

        _grpcClient = new Lazy<HonuaGrpcClient>(
            () => new HonuaGrpcClient(Options.Create(BuildGrpcClientOptions())),
            LazyThreadSafetyMode.ExecutionAndPublication);

        Routing = new HonuaRoutingClient(_sdkHttp, BuildGeoServicesRoutingOptions(options));
        Scenes = new HonuaSceneClient(_sdkHttp, BuildSceneClientOptions(options));
    }

    /// <summary>
    /// Routing and network-analysis client for directions, service areas, closest facility, and route optimization.
    /// </summary>
    public HonuaRoutingClient Routing { get; }

    /// <summary>
    /// Scene metadata discovery client for 3D Tiles, terrain, and related render-ready endpoints.
    /// </summary>
    public IHonuaSceneClient Scenes { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_grpcClient.IsValueCreated)
        {
            _grpcClient.Value.Dispose();
        }

        _sdkHttp.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private bool CanUseGrpcForQueries => _options.PreferGrpcForFeatureQueries && _canUseGrpcEndpoint;

    private bool CanUseGrpcForEdits => _options.PreferGrpcForFeatureEdits && _canUseGrpcEndpoint;

    private static HonuaGeoServicesClientOptions BuildGeoServicesRoutingOptions(HonuaMobileClientOptions options)
        => new()
        {
            BaseAddress = options.BaseUri,
            Timeout = options.Timeout,
            EnableRetry = false,
            RoutingServiceId = options.RoutingServiceId,
            RoutingRouteLayerName = options.RoutingRouteLayerName,
            RoutingServiceAreaLayerName = options.RoutingServiceAreaLayerName,
            RoutingClosestFacilityLayerName = options.RoutingClosestFacilityLayerName,
        };

    internal static HonuaSceneClientOptions BuildSceneClientOptions(HonuaMobileClientOptions options)
        => new()
        {
            BaseAddress = options.BaseUri,
            SceneApiPath = options.SceneApiPath,
            Timeout = options.Timeout,
        };

    private static bool HasOgcSource(FeatureSource source)
        => !string.IsNullOrWhiteSpace(source.CollectionId);

    private static bool HasFeatureServerSource(FeatureSource source)
        => !string.IsNullOrWhiteSpace(source.ServiceId) && source.LayerId.HasValue;

    private static HonuaMobileApiException ToMobileApiException(string provider, HonuaFeatureServerException ex)
        => new(
            ex.StatusCode,
            $"{provider} request failed with status {(int)ex.StatusCode} {ex.Message}",
            ex.ResponseBody);

    private static HonuaMobileApiException ToMobileApiException(string provider, HonuaOgcFeaturesException ex)
        => new(
            ex.StatusCode,
            $"{provider} request failed with status {(int)ex.StatusCode} {ex.Message}",
            ex.ResponseBody);

    private sealed class AuthenticatedSdkHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpClient _inner;
        private readonly Func<HttpRequestMessage, CancellationToken, ValueTask> _authenticate;

        public AuthenticatedSdkHttpMessageHandler(
            HttpClient inner,
            Func<HttpRequestMessage, CancellationToken, ValueTask> authenticate)
        {
            _inner = inner;
            _authenticate = authenticate;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Forward via a stream-wrapping clone so large request bodies (e.g. attachment
            // uploads) are not buffered into a byte[] before being sent.
            using var forwarded = CloneRequestForForwarding(request);
            await _authenticate(forwarded, cancellationToken).ConfigureAwait(false);
            return await _inner.SendAsync(forwarded, cancellationToken).ConfigureAwait(false);
        }

        private static HttpRequestMessage CloneRequestForForwarding(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                VersionPolicy = request.VersionPolicy,
            };

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (request.Content is not null)
            {
                clone.Content = new StreamForwardingContent(request.Content);
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }

        private sealed class StreamForwardingContent : HttpContent
        {
            private readonly HttpContent _inner;

            public StreamForwardingContent(HttpContent inner)
            {
                _inner = inner;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
                => _inner.CopyToAsync(stream, context, CancellationToken.None);

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
                => _inner.CopyToAsync(stream, context, cancellationToken);

            protected override bool TryComputeLength(out long length)
            {
                var headerLength = _inner.Headers.ContentLength;
                if (headerLength.HasValue)
                {
                    length = headerLength.Value;
                    return true;
                }

                length = 0;
                return false;
            }
        }
    }
}
