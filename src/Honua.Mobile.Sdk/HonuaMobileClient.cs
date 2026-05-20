using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
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
using FeatureServerRequestConverters = Honua.Sdk.GeoServices.FeatureServer.Conversion.RequestConverters;
using GrpcRequestConverters = Honua.Sdk.Grpc.Conversion.MobileRequestConverters;
using OgcRequestConverters = Honua.Sdk.OgcFeatures.Conversion.RequestConverters;
using Microsoft.Extensions.Options;

namespace Honua.Mobile.Sdk;

/// <summary>
/// Client for the Honua platform REST and gRPC APIs.
/// Provides feature query/edit operations, OGC Features API access,
/// and automatic transport negotiation between gRPC and REST.
/// </summary>
public sealed class HonuaMobileClient : IDisposable, IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClient _sdkHttp;
    private readonly HonuaFeatureServerClient _featureServerClient;
    private readonly HonuaOgcFeaturesClient _ogcFeaturesClient;
    private readonly HonuaMobileClientOptions _options;
    private readonly IAuthTokenProvider? _authTokenProvider;
    private readonly object _grpcClientSync = new();
    private readonly bool _canUseGrpcEndpoint;
    private readonly Uri? _baseUri;
    private readonly TimeSpan _requestTimeout;
    private readonly ProductInfoHeaderValue _userAgent;
    private Lazy<HonuaGrpcClient> _grpcClient;
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

        // Do NOT mutate the injected HttpClient (it may be pooled by IHttpClientFactory).
        // Capture configuration locally and apply per-request where needed.
        _baseUri = options.BaseUri;
        _requestTimeout = options.Timeout;
        _userAgent = options.UserAgent;

        // The SDK-owned HttpClient backs internal sub-clients (FeatureServer, OGC, Routing,
        // Scenes). It wraps the caller-supplied HttpClient via AuthenticatedSdkHttpMessageHandler
        // so we share the caller's transport pipeline (mocks/handlers in tests, IHttpClientFactory
        // pooled handlers in production) without mutating the caller's HttpClient instance.
        _sdkHttp = new HttpClient(
            new AuthenticatedSdkHttpMessageHandler(_http, ApplyHttpAuthenticationAsync, _userAgent),
            disposeHandler: true)
        {
            BaseAddress = options.BaseUri,
            Timeout = options.Timeout,
        };
        _sdkHttp.DefaultRequestHeaders.UserAgent.Clear();
        _sdkHttp.DefaultRequestHeaders.UserAgent.Add(_userAgent);

        _featureServerClient = new HonuaFeatureServerClient(_sdkHttp);
        _ogcFeaturesClient = new HonuaOgcFeaturesClient(_sdkHttp);

        var grpcAddress = options.GrpcEndpoint ?? options.BaseUri;
        _canUseGrpcEndpoint = grpcAddress.Scheme is "http" or "https";

        _grpcClient = CreateGrpcClientLazy();

        Routing = new HonuaRoutingClient(_sdkHttp, BuildGeoServicesRoutingOptions(options));
        Scenes = new HonuaSceneClient(_sdkHttp, BuildSceneClientOptions(options));
    }

    private Lazy<HonuaGrpcClient> CreateGrpcClientLazy()
        => new(
            () => new HonuaGrpcClient(Options.Create(BuildGrpcClientOptions())),
            LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Routing and network-analysis client for directions, service areas, closest facility, and route optimization.
    /// </summary>
    public HonuaRoutingClient Routing { get; }

    /// <summary>
    /// Scene metadata discovery client for 3D Tiles, terrain, and related render-ready endpoints.
    /// </summary>
    public IHonuaSceneClient Scenes { get; }

    /// <summary>
    /// Queries features through the SDK provider-neutral feature contract.
    /// OGC collection sources are handled by <see cref="HonuaOgcFeaturesClient"/>;
    /// FeatureServer sources prefer gRPC when configured and otherwise use <see cref="HonuaFeatureServerClient"/>.
    /// </summary>
    /// <param name="request">SDK feature query request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A provider-neutral feature query result.</returns>
    public async Task<FeatureQueryResult> QueryAsync(FeatureQueryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasOgcSource(request.Source))
        {
            return await QueryOgcFeaturesSdkAsync(request, ct).ConfigureAwait(false);
        }

        if (HasFeatureServerSource(request.Source))
        {
            return await QueryFeatureServerSdkAsync(request, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Feature query requires either an OGC collection ID or FeatureServer service/layer identifiers.");
    }

    /// <summary>
    /// Streams feature query pages through the SDK provider-neutral feature contract.
    /// </summary>
    /// <param name="request">SDK feature query request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of provider-neutral feature query pages.</returns>
    public async IAsyncEnumerable<FeatureQueryResult> QueryPagesAsync(
        FeatureQueryRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasOgcSource(request.Source))
        {
            await foreach (var page in QueryOgcFeaturesSdkPagesAsync(request, ct).ConfigureAwait(false))
            {
                yield return page;
            }

            yield break;
        }

        if (HasFeatureServerSource(request.Source))
        {
            await foreach (var page in QueryFeatureServerSdkPagesAsync(request, ct).ConfigureAwait(false))
            {
                yield return page;
            }

            yield break;
        }

        throw new InvalidOperationException(
            "Feature query requires either an OGC collection ID or FeatureServer service/layer identifiers.");
    }

    /// <summary>
    /// Applies feature edits through the SDK provider-neutral feature contract.
    /// OGC collection sources are handled by <see cref="HonuaOgcFeaturesClient"/>;
    /// FeatureServer sources prefer gRPC when configured and otherwise use <see cref="HonuaFeatureServerClient"/>.
    /// </summary>
    /// <param name="request">SDK feature edit request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A provider-neutral feature edit response.</returns>
    public async Task<FeatureEditResponse> ApplyEditsAsync(FeatureEditRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasOgcSource(request.Source))
        {
            return await ApplyOgcSdkEditsAsync(request, ct).ConfigureAwait(false);
        }

        if (HasFeatureServerSource(request.Source))
        {
            return await ApplyFeatureServerSdkEditsAsync(request, ct).ConfigureAwait(false);
        }

        return new FeatureEditResponse
        {
            ProviderName = "honua-mobile",
            Error = new FeatureEditError
            {
                Message = "Feature edit requires either an OGC collection ID or FeatureServer service/layer identifiers.",
            },
        };
    }

    /// <summary>
    /// Queries features from a feature service layer, preferring gRPC when available.
    /// Falls back to REST if gRPC fails and <see cref="HonuaMobileClientOptions.AllowRestFallbackOnGrpcFailure"/> is enabled.
    /// </summary>
    /// <param name="request">The query parameters including service ID, layer, WHERE clause, and output fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the query result with features, fields, and metadata.</returns>
    /// <remarks>
    /// The caller takes ownership of the returned <see cref="JsonDocument"/> and must dispose it
    /// to release pooled buffers (typically via <c>using</c>). TODO: switch this surface to return
    /// a cloned <see cref="JsonElement"/> to remove the disposal contract.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> QueryFeaturesAsync(QueryFeaturesRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CanUseGrpcForQueries)
        {
            try
            {
                return await QueryFeaturesGrpcAsync(request, ct).ConfigureAwait(false);
            }
            catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcFailure)
            {
                // Fall through to REST transport.
            }
        }

        return await QueryFeaturesRestAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Streams feature query results as multiple pages via gRPC server streaming, falling back to a single REST page on failure.
    /// </summary>
    /// <param name="request">The query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of <see cref="JsonDocument"/> pages.</returns>
    /// <remarks>
    /// The caller takes ownership of each yielded <see cref="JsonDocument"/> and must dispose it to
    /// release pooled buffers.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    public async IAsyncEnumerable<JsonDocument> QueryFeaturesStreamAsync(
        QueryFeaturesRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CanUseGrpcForQueries)
        {
            var yieldedGrpcPage = false;
            await using var grpcEnumerator = QueryFeaturesGrpcPagesAsync(request, ct).GetAsyncEnumerator(ct);

            while (true)
            {
                JsonDocument? nextPage = null;
                try
                {
                    if (!await grpcEnumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        if (yieldedGrpcPage)
                        {
                            yield break;
                        }

                        break;
                    }

                    nextPage = grpcEnumerator.Current;
                }
                catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcFailure && !yieldedGrpcPage)
                {
                    break;
                }

                yieldedGrpcPage = true;
                yield return nextPage!;
            }
        }

        yield return await QueryFeaturesRestAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies feature edits (adds, updates, deletes) to a feature service layer.
    /// Prefers gRPC transport when available and falls back to REST on failure.
    /// </summary>
    /// <param name="request">The edit payload including adds, updates, and deletes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing per-feature edit results.</returns>
    /// <remarks>
    /// The caller takes ownership of the returned <see cref="JsonDocument"/> and must dispose it to
    /// release pooled buffers.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> ApplyEditsAsync(ApplyEditsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CanUseGrpcForEdits)
        {
            try
            {
                return await ApplyEditsGrpcAsync(request, ct).ConfigureAwait(false);
            }
            catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcFailure)
            {
                // Fall through to REST transport.
            }
        }

        return await ApplyEditsRestAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists FeatureServer attachments using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment list request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Attachment metadata for the requested feature.</returns>
    public async Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsAsync(
        FeatureAttachmentListRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.ListAttachmentsAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Downloads one FeatureServer attachment using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment download request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Downloaded attachment content. Dispose the returned content stream when finished.</returns>
    public async Task<FeatureAttachmentContent> DownloadAttachmentAsync(
        FeatureAttachmentDownloadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.DownloadAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Adds one FeatureServer attachment using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment add request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Attachment edit result.</returns>
    public async Task<FeatureAttachmentResult> AddAttachmentAsync(
        FeatureAttachmentAddRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.AddAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Updates one FeatureServer attachment using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Attachment edit result.</returns>
    public async Task<FeatureAttachmentResult> UpdateAttachmentAsync(
        FeatureAttachmentUpdateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.UpdateAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Deletes one FeatureServer attachment using the shared SDK attachment contract.
    /// </summary>
    /// <param name="request">SDK attachment delete request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Attachment edit result.</returns>
    public async Task<FeatureAttachmentResult> DeleteAttachmentAsync(
        FeatureAttachmentDeleteRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await _featureServerClient.DeleteAttachmentAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer attachments", ex);
        }
    }

    /// <summary>
    /// Retrieves the list of OGC Features API collections available on the server.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> describing available collections.</returns>
    /// <remarks>The caller owns and must dispose the returned <see cref="JsonDocument"/>.</remarks>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> GetOgcCollectionsAsync(CancellationToken ct = default)
    {
        try
        {
            var collections = await _ogcFeaturesClient.ListCollectionsAsync(ct).ConfigureAwait(false);
            return OgcRequestConverters.ToJsonDocument(collections);
        }
        catch (HonuaOgcFeaturesException ex)
        {
            throw ToMobileApiException("OGC Features", ex);
        }
    }

    /// <summary>
    /// Retrieves items from an OGC Features API collection with optional filtering and pagination.
    /// </summary>
    /// <param name="request">Parameters including collection ID, CQL filter, limit, and offset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the matched items.</returns>
    /// <remarks>The caller owns and must dispose the returned <see cref="JsonDocument"/>.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> GetOgcItemsAsync(OgcItemsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var response = await _ogcFeaturesClient
                .GetItemsAsync(request.CollectionId, OgcRequestConverters.ToOgcItemsParams(request), ct)
                .ConfigureAwait(false);
            return OgcRequestConverters.ToJsonDocument(response);
        }
        catch (HonuaOgcFeaturesException ex)
        {
            throw ToMobileApiException("OGC Features", ex);
        }
    }

    /// <summary>
    /// Creates a new feature item in an OGC Features API collection.
    /// </summary>
    /// <param name="request">The collection ID and GeoJSON feature to create.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response for the created item.</returns>
    /// <remarks>The caller owns and must dispose the returned <see cref="JsonDocument"/>.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> CreateOgcItemAsync(OgcCreateItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var response = await _ogcFeaturesClient
                .CreateItemAsync(request.CollectionId, OgcRequestConverters.ToOgcFeature(request.Feature), ct)
                .ConfigureAwait(false);
            return OgcRequestConverters.ToJsonDocument(response);
        }
        catch (HonuaOgcFeaturesException ex)
        {
            throw ToMobileApiException("OGC Features", ex);
        }
    }

    /// <summary>
    /// Replaces an existing feature item in an OGC Features API collection (HTTP PUT).
    /// </summary>
    /// <param name="request">The collection ID, feature ID, and full replacement feature.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response.</returns>
    /// <remarks>The caller owns and must dispose the returned <see cref="JsonDocument"/>.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> ReplaceOgcItemAsync(OgcReplaceItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var response = await _ogcFeaturesClient
                .UpdateItemAsync(request.CollectionId, request.FeatureId, OgcRequestConverters.ToOgcFeature(request.Feature), ct)
                .ConfigureAwait(false);
            return OgcRequestConverters.ToJsonDocument(response);
        }
        catch (HonuaOgcFeaturesException ex)
        {
            throw ToMobileApiException("OGC Features", ex);
        }
    }

    /// <summary>
    /// Partially updates an existing feature item using JSON Merge Patch (RFC 7396).
    /// </summary>
    /// <param name="request">The collection ID, feature ID, and merge-patch document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response.</returns>
    /// <remarks>The caller owns and must dispose the returned <see cref="JsonDocument"/>.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> PatchOgcItemAsync(OgcPatchItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var response = await _ogcFeaturesClient
                .PatchItemAsync(request.CollectionId, request.FeatureId, OgcRequestConverters.ToJsonElement(request.Patch), ct)
                .ConfigureAwait(false);
            return OgcRequestConverters.ToJsonDocument(response);
        }
        catch (HonuaOgcFeaturesException ex)
        {
            throw ToMobileApiException("OGC Features", ex);
        }
    }

    /// <summary>
    /// Deletes a feature item from an OGC Features API collection.
    /// </summary>
    /// <param name="request">The collection ID and feature ID to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing the server response.</returns>
    /// <remarks>The caller owns and must dispose the returned <see cref="JsonDocument"/>.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public async Task<JsonDocument> DeleteOgcItemAsync(OgcDeleteItemRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = $"/ogc/features/collections/{Uri.EscapeDataString(request.CollectionId)}/items/{Uri.EscapeDataString(request.FeatureId)}";
        return await SendJsonAsync(HttpMethod.Delete, path, null, null, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_grpcClientSync)
        {
            if (_grpcClient.IsValueCreated)
            {
                _grpcClient.Value.Dispose();
            }
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

    private async Task<JsonDocument> QueryFeaturesGrpcAsync(QueryFeaturesRequest request, CancellationToken ct)
    {
        var response = await GetGrpcClient()
            .QueryFeaturesAsync(GrpcRequestConverters.ToGrpcQueryRequest(request), ct)
            .ConfigureAwait(false);
        return GrpcRequestConverters.ToJsonDocument(response);
    }

    private async IAsyncEnumerable<JsonDocument> QueryFeaturesGrpcPagesAsync(
        QueryFeaturesRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var page in GetGrpcClient()
            .QueryFeaturesStreamAsync(GrpcRequestConverters.ToGrpcQueryRequest(request), ct)
            .ConfigureAwait(false))
        {
            yield return GrpcRequestConverters.ToJsonDocument(page);
        }
    }

    private async Task<JsonDocument> ApplyEditsGrpcAsync(ApplyEditsRequest request, CancellationToken ct)
    {
        var response = await GetGrpcClient()
            .ApplyEditsAsync(GrpcRequestConverters.ToGrpcApplyEditsRequest(request), ct)
            .ConfigureAwait(false);
        return GrpcRequestConverters.ToJsonDocument(response);
    }

    private async Task<JsonDocument> QueryFeaturesRestAsync(QueryFeaturesRequest request, CancellationToken ct)
    {
        try
        {
            var response = await _featureServerClient
                .QueryAsync(request.ServiceId, request.LayerId, FeatureServerRequestConverters.ToFeatureServerQueryParams(request), ct)
                .ConfigureAwait(false);
            return FeatureServerRequestConverters.ToJsonDocument(response);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer", ex);
        }
    }

    private async Task<JsonDocument> ApplyEditsRestAsync(ApplyEditsRequest request, CancellationToken ct)
    {
        if (!IsDefaultJsonResponseFormat(request.ResponseFormat))
        {
            var path = $"/rest/services/{Uri.EscapeDataString(request.ServiceId)}/FeatureServer/{request.LayerId}/applyEdits";
            return await SendJsonAsync(
                HttpMethod.Post,
                path,
                query: null,
                new FormUrlEncodedContent(FeatureServerRequestConverters.ToFeatureServerEditFormParameters(request)),
                ct).ConfigureAwait(false);
        }

        try
        {
            var response = await _featureServerClient
                .ApplyEditsAsync(request.ServiceId, request.LayerId, FeatureServerRequestConverters.ToFeatureServerEditRequest(request), ct)
                .ConfigureAwait(false);
            return FeatureServerRequestConverters.ToJsonDocument(response);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer", ex);
        }
    }

    private async Task<FeatureQueryResult> QueryOgcFeaturesSdkAsync(FeatureQueryRequest request, CancellationToken ct)
    {
        try
        {
            return await _ogcFeaturesClient.QueryAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaOgcFeaturesException ex)
        {
            throw ToMobileApiException("OGC Features", ex);
        }
    }

    private async IAsyncEnumerable<FeatureQueryResult> QueryOgcFeaturesSdkPagesAsync(
        FeatureQueryRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var enumerator = _ogcFeaturesClient.QueryPagesAsync(request, ct).GetAsyncEnumerator(ct);
        while (true)
        {
            FeatureQueryResult? page;
            try
            {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    yield break;
                }

                page = enumerator.Current;
            }
            catch (HonuaOgcFeaturesException ex)
            {
                throw ToMobileApiException("OGC Features", ex);
            }

            yield return page;
        }
    }

    private async Task<FeatureQueryResult> QueryFeatureServerSdkAsync(FeatureQueryRequest request, CancellationToken ct)
    {
        if (CanUseGrpcForQueries)
        {
            try
            {
                return await GetGrpcClient().QueryAsync(request, ct).ConfigureAwait(false);
            }
            catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcFailure)
            {
                // Fall through to REST transport.
            }
        }

        try
        {
            return await _featureServerClient.QueryAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer", ex);
        }
    }

    private async IAsyncEnumerable<FeatureQueryResult> QueryFeatureServerSdkPagesAsync(
        FeatureQueryRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (CanUseGrpcForQueries)
        {
            var grpcSucceeded = false;
            var grpcFailed = false;
            await using var grpcEnumerator = GetGrpcClient().QueryPagesAsync(request, ct).GetAsyncEnumerator(ct);

            while (true)
            {
                FeatureQueryResult? nextPage = null;
                try
                {
                    if (!await grpcEnumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        grpcSucceeded = true;
                        break;
                    }

                    nextPage = grpcEnumerator.Current;
                }
                catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcFailure && !grpcSucceeded)
                {
                    grpcFailed = true;
                    break;
                }

                grpcSucceeded = true;
                yield return nextPage!;
            }

            // If gRPC produced any page (or completed cleanly with zero pages), do not
            // duplicate by falling through to REST. Only run REST as a fallback when the
            // gRPC stream failed before yielding any page.
            if (!grpcFailed)
            {
                yield break;
            }
        }

        await using var restEnumerator = _featureServerClient.QueryPagesAsync(request, ct).GetAsyncEnumerator(ct);
        while (true)
        {
            FeatureQueryResult? page;
            try
            {
                if (!await restEnumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    yield break;
                }

                page = restEnumerator.Current;
            }
            catch (HonuaFeatureServerException ex)
            {
                throw ToMobileApiException("FeatureServer", ex);
            }

            yield return page;
        }
    }

    private async Task<FeatureEditResponse> ApplyOgcSdkEditsAsync(FeatureEditRequest request, CancellationToken ct)
    {
        try
        {
            return await _ogcFeaturesClient.ApplyEditsAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaOgcFeaturesException ex)
        {
            throw ToMobileApiException("OGC Features", ex);
        }
    }

    private async Task<FeatureEditResponse> ApplyFeatureServerSdkEditsAsync(FeatureEditRequest request, CancellationToken ct)
    {
        if (CanUseGrpcForEdits)
        {
            try
            {
                return await GetGrpcClient().ApplyEditsAsync(request, ct).ConfigureAwait(false);
            }
            catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcFailure)
            {
                // Fall through to REST transport.
            }
        }

        try
        {
            return await _featureServerClient.ApplyEditsAsync(request, ct).ConfigureAwait(false);
        }
        catch (HonuaFeatureServerException ex)
        {
            throw ToMobileApiException("FeatureServer", ex);
        }
    }

    private static bool IsDefaultJsonResponseFormat(string? responseFormat)
        => string.IsNullOrWhiteSpace(responseFormat) ||
            string.Equals(responseFormat, "json", StringComparison.OrdinalIgnoreCase);

    private static bool HasOgcSource(FeatureSource source)
        => !string.IsNullOrWhiteSpace(source.CollectionId);

    private static bool HasFeatureServerSource(FeatureSource source)
        => !string.IsNullOrWhiteSpace(source.ServiceId) && source.LayerId.HasValue;

    internal async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        IReadOnlyDictionary<string, string?>? query,
        HttpContent? content,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, BuildAbsoluteOrRelativeUri(relativePath, query));
        request.Content = content;

        // Apply per-request headers so we never mutate the caller-supplied HttpClient
        // (which may be pooled by IHttpClientFactory).
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(_userAgent);
        await ApplyHttpAuthenticationAsync(request, ct).ConfigureAwait(false);

        // Apply the configured timeout per request rather than mutating HttpClient.Timeout.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_requestTimeout > TimeSpan.Zero && _requestTimeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            timeoutCts.CancelAfter(_requestTimeout);
        }

        using var response = await _http.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Buffer only the (already-bounded) error payload to surface a useful message.
            var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            throw new HonuaMobileApiException(
                response.StatusCode,
                $"Honua mobile request failed with status {(int)response.StatusCode} {response.ReasonPhrase}",
                errorBody);
        }

        // Stream the body directly into JsonDocument.ParseAsync to avoid the
        // double-allocation incurred by ReadAsStringAsync + JsonDocument.Parse on large
        // feature payloads.
        await using var contentStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false);
        if (contentStream.CanSeek && contentStream.Length == 0)
        {
            return JsonDocument.Parse("{}");
        }

        try
        {
            return await JsonDocument.ParseAsync(contentStream, default, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new HonuaMobileApiException("Honua mobile request returned invalid JSON.", ex);
        }
    }

    private Uri BuildAbsoluteOrRelativeUri(string relativePath, IReadOnlyDictionary<string, string?>? query)
    {
        var relative = BuildUri(relativePath, query);
        if (_http.BaseAddress is not null || _baseUri is null)
        {
            return relative;
        }

        // The injected HttpClient has no BaseAddress (because we deliberately don't mutate
        // it). Compose an absolute URI from the configured BaseUri for transport.
        return new Uri(_baseUri, relative);
    }

    private async ValueTask ApplyHttpAuthenticationAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var apiKey = _options.ApiKey;
        var token = _options.BearerToken;
        var providerToken = await ResolveProviderTokenAsync(ct).ConfigureAwait(false);
        if (providerToken is { Scheme: HonuaAuthScheme.ApiKey })
        {
            apiKey = providerToken.AccessToken;
        }
        else if (providerToken is { Scheme: HonuaAuthScheme.Bearer })
        {
            token = providerToken.AccessToken;
        }
        else
        {
            token = await ResolveBearerTokenAsync(ct).ConfigureAwait(false);
        }

        var hasApiKey = !string.IsNullOrWhiteSpace(apiKey);
        var hasBearerToken = !string.IsNullOrWhiteSpace(token);

        if (hasApiKey || hasBearerToken)
        {
            EnsureSecureTransport(ResolveAbsoluteRequestUri(request));
        }

        if (hasApiKey)
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async ValueTask<string?> ResolveBearerTokenAsync(CancellationToken ct)
    {
        var token = _options.BearerToken;
        if (_options.AccessTokenProvider is not null)
        {
            token = await _options.AccessTokenProvider(ct).ConfigureAwait(false) ?? token;
        }

        return token;
    }

    private async ValueTask<HonuaAuthToken?> ResolveProviderTokenAsync(CancellationToken ct)
        => _authTokenProvider is null
            ? null
            : await _authTokenProvider.GetTokenAsync(ct).ConfigureAwait(false);

    internal HonuaGrpcClient GetGrpcClient()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(HonuaMobileClient));
        }

        // Lazy<T> with ExecutionAndPublication guarantees single-construction even
        // under concurrent access. We re-check the disposed flag after materialization
        // to avoid resurrecting a value that races with Dispose().
        var client = _grpcClient.Value;
        if (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(HonuaMobileClient));
        }

        return client;
    }

    internal HonuaGrpcClientOptions BuildGrpcClientOptions()
    {
        var address = _options.GrpcEndpoint ?? _options.BaseUri;
        if (HasConfiguredGrpcAuthentication)
        {
            EnsureSecureTransport(address);
        }

        return new HonuaGrpcClientOptions
        {
            Address = address.ToString(),
            ApiKey = _options.ApiKey,
            ApiKeyProvider = BuildGrpcApiKeyProvider(),
            BearerToken = _options.BearerToken,
            BearerTokenProvider = BuildGrpcBearerTokenProvider(),
            Timeout = _options.Timeout,
        };
    }

    private Func<CancellationToken, Task<string?>>? BuildGrpcApiKeyProvider()
    {
        if (_authTokenProvider is null)
        {
            return null;
        }

        return async ct =>
        {
            var token = await _authTokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
            return token is { Scheme: HonuaAuthScheme.ApiKey }
                ? token.AccessToken
                : _options.ApiKey;
        };
    }

    private Func<CancellationToken, Task<string?>>? BuildGrpcBearerTokenProvider()
    {
        if (_authTokenProvider is not null)
        {
            return async ct =>
            {
                var token = await _authTokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
                if (token is { Scheme: HonuaAuthScheme.Bearer })
                {
                    return token.AccessToken;
                }

                return token is null
                    ? await ResolveBearerTokenAsync(ct).ConfigureAwait(false)
                    : _options.BearerToken;
            };
        }

        return _options.AccessTokenProvider is null
            ? null
            : async ct => await _options.AccessTokenProvider(ct).ConfigureAwait(false) ?? _options.BearerToken;
    }

    internal static HonuaSceneClientOptions BuildSceneClientOptions(HonuaMobileClientOptions options)
        => new()
        {
            BaseAddress = options.BaseUri,
            SceneApiPath = options.SceneApiPath,
            Timeout = options.Timeout,
        };

    private bool HasConfiguredGrpcAuthentication =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) ||
        !string.IsNullOrWhiteSpace(_options.BearerToken) ||
        _options.AccessTokenProvider is not null ||
        _authTokenProvider is not null;

    private Uri ResolveAbsoluteRequestUri(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
        {
            throw new InvalidOperationException("Request URI cannot be null.");
        }

        if (request.RequestUri.IsAbsoluteUri)
        {
            return request.RequestUri;
        }

        var baseAddress = _http.BaseAddress ?? _baseUri;
        if (baseAddress is null)
        {
            throw new InvalidOperationException("HonuaMobileClient requires an absolute BaseUri.");
        }

        return new Uri(baseAddress, request.RequestUri);
    }

    private void EnsureSecureTransport(Uri targetUri)
    {
        if (_options.AllowInsecureTransportForDevelopment)
        {
            return;
        }

        if (!string.Equals(targetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Refusing to send authentication over non-HTTPS transport. " +
                "Set AllowInsecureTransportForDevelopment=true only for local development.");
        }
    }

    private static Uri BuildUri(string relativePath, IReadOnlyDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return new Uri(relativePath, UriKind.Relative);
        }

        var queryText = string.Join(
            '&',
            query.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));

        if (string.IsNullOrWhiteSpace(queryText))
        {
            return new Uri(relativePath, UriKind.Relative);
        }

        return new Uri($"{relativePath}?{queryText}", UriKind.Relative);
    }

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

    // Forwards SDK sub-client requests through the caller-supplied HttpClient so we share
    // its transport pipeline (test stubs, IHttpClientFactory pooled handlers, etc.). We
    // forward via a separate HttpClient because we have no other access to its handler
    // pipeline; the alternative (a pure DelegatingHandler over a fresh HttpClientHandler)
    // would bypass the caller's configured pipeline entirely.
    //
    // The previous implementation buffered every request body into a byte[] via
    // ReadAsByteArrayAsync, which caused OOM for large uploads. This implementation
    // shallow-clones the request and wraps the original content via StreamForwardingContent
    // so the body streams once from its source. Auth headers and User-Agent are applied to
    // the cloned request.
    private sealed class AuthenticatedSdkHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpClient _inner;
        private readonly Func<HttpRequestMessage, CancellationToken, ValueTask> _authenticate;
        private readonly ProductInfoHeaderValue _userAgent;

        public AuthenticatedSdkHttpMessageHandler(
            HttpClient inner,
            Func<HttpRequestMessage, CancellationToken, ValueTask> authenticate,
            ProductInfoHeaderValue userAgent)
        {
            _inner = inner;
            _authenticate = authenticate;
            _userAgent = userAgent;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // A given HttpRequestMessage can only be sent through HttpClient.SendAsync once
            // (HttpClient marks it as sent), so we clone before forwarding. The clone
            // wraps — rather than buffers — the original content stream.
            var forwarded = CloneWithoutBufferingContent(request);
            forwarded.Headers.UserAgent.Clear();
            forwarded.Headers.UserAgent.Add(_userAgent);
            await _authenticate(forwarded, cancellationToken).ConfigureAwait(false);
            return await _inner.SendAsync(forwarded, cancellationToken).ConfigureAwait(false);
        }

        private static HttpRequestMessage CloneWithoutBufferingContent(HttpRequestMessage request)
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
    }

    // HttpContent that defers serialization to an inner HttpContent, so large request
    // bodies stream through to the transport instead of being buffered into a byte[].
    private sealed class StreamForwardingContent : HttpContent
    {
        private readonly HttpContent _inner;

        public StreamForwardingContent(HttpContent inner)
        {
            _inner = inner;
        }

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => _inner.CopyToAsync(stream);

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context, CancellationToken cancellationToken)
            => _inner.CopyToAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            var contentLength = _inner.Headers.ContentLength;
            if (contentLength.HasValue)
            {
                length = contentLength.Value;
                return true;
            }

            length = 0;
            return false;
        }
    }
}
