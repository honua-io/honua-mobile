using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
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

// SDK-owned conversion shims (live in honua-sdk-dotnet train >= 0.1.17-alpha.1).
// Aliased so the call-site identifiers stay close to the original local mappings.
using FeatureServerRequestConverters = Honua.Sdk.GeoServices.FeatureServer.Conversion.RequestConverters;
using GrpcRequestConverters = Honua.Sdk.Grpc.Conversion.MobileRequestConverters;
using OgcRequestConverters = Honua.Sdk.OgcFeatures.Conversion.RequestConverters;

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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    public async IAsyncEnumerable<JsonDocument> QueryFeaturesStreamAsync(
        QueryFeaturesRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CanUseGrpcForQueries)
        {
            var yieldedGrpcPage = false;
            var grpcSucceeded = false;
            await using var grpcEnumerator = QueryFeaturesGrpcPagesAsync(request, ct).GetAsyncEnumerator(ct);

            while (true)
            {
                JsonDocument? nextPage = null;
                try
                {
                    if (!await grpcEnumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        grpcSucceeded = true;
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

            if (grpcSucceeded)
            {
                yield break;
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
        catch (HonuaFeatureServerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await ListAttachmentsRestCompatAsync(request, ct).ConfigureAwait(false);
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
            var yieldedGrpcPage = false;
            var grpcSucceeded = false;
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
                catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcFailure && !yieldedGrpcPage)
                {
                    break;
                }

                yieldedGrpcPage = true;
                yield return nextPage!;
            }

            if (grpcSucceeded)
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

        using var raw = await ApplyFeatureServerSdkEditsRestAsync(request, ct).ConfigureAwait(false);
        return ToFeatureServerFeatureEditResponse(raw.RootElement);
    }

    private async Task<JsonDocument> ApplyFeatureServerSdkEditsRestAsync(FeatureEditRequest request, CancellationToken ct)
    {
        var source = request.Source;
        var serviceId = source.ServiceId
            ?? throw new InvalidOperationException("FeatureServer feature edits require a service ID.");
        var layerId = source.LayerId
            ?? throw new InvalidOperationException("FeatureServer feature edits require a layer ID.");

        // The SDK's ApplyEditsRequest accepts FeatureEditFeature directly; the
        // legacy mobile-side path that pre-converted to FeatureServerFeature is
        // no longer required because Honua.Sdk.GeoServices owns the per-protocol
        // conversion as part of ToFeatureServerEditFormParameters.
        var editRequest = new ApplyEditsRequest
        {
            ServiceId = serviceId,
            LayerId = layerId,
            Adds = request.Adds.Count > 0 ? [.. request.Adds] : null,
            Updates = request.Updates.Count > 0 ? [.. request.Updates] : null,
            Deletes = FeatureServerRequestConverters.ToFeatureServerDeleteObjectIds(request),
            RollbackOnFailure = request.RollbackOnFailure,
            ForceWrite = request.ForceWrite,
        };

        var path = $"/rest/services/{Uri.EscapeDataString(serviceId)}/FeatureServer/{layerId}/applyEdits";
        return await SendJsonAsync(
            HttpMethod.Post,
            path,
            query: null,
            new FormUrlEncodedContent(FeatureServerRequestConverters.ToFeatureServerEditFormParameters(editRequest)),
            ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsRestCompatAsync(
        FeatureAttachmentListRequest request,
        CancellationToken ct)
    {
        var serviceId = request.Source.ServiceId
            ?? throw new InvalidOperationException("FeatureServer attachments require a service ID.");
        var layerId = request.Source.LayerId
            ?? throw new InvalidOperationException("FeatureServer attachments require a layer ID.");
        var path = $"/rest/services/{Uri.EscapeDataString(serviceId)}/FeatureServer/{layerId}/queryAttachments";

        using var response = await SendJsonAsync(
            HttpMethod.Get,
            path,
            new Dictionary<string, string?>
            {
                ["f"] = "json",
                ["objectIds"] = request.ObjectId.ToString(CultureInfo.InvariantCulture),
                ["returnUrl"] = "true",
            },
            content: null,
            ct).ConfigureAwait(false);

        return ToFeatureAttachmentInfos(response.RootElement, request.Source, request.ObjectId);
    }

    private static FeatureEditResponse ToFeatureServerFeatureEditResponse(JsonElement root)
    {
        var error = TryGetJsonProperty(root, out var errorElement, "error") && errorElement.ValueKind == JsonValueKind.Object
            ? ToFeatureEditError(errorElement)
            : null;
        var addResults = ToFeatureEditResults(root, "addResults", "adds");
        var updateResults = ToFeatureEditResults(root, "updateResults", "updates");
        var deleteResults = ToFeatureEditResults(root, "deleteResults", "deletes");

        if (error is null &&
            addResults.Count == 0 &&
            updateResults.Count == 0 &&
            deleteResults.Count == 0)
        {
            error = new FeatureEditError
            {
                Message = "FeatureServer applyEdits response is malformed: missing edit result arrays.",
            };
        }

        return new FeatureEditResponse
        {
            ProviderName = "geoservices-featureserver",
            AddResults = addResults,
            UpdateResults = updateResults,
            DeleteResults = deleteResults,
            Error = error,
        };
    }

    private static IReadOnlyList<FeatureEditResult> ToFeatureEditResults(JsonElement root, params string[] propertyNames)
    {
        if (!TryGetJsonProperty(root, out var results, propertyNames))
        {
            return [];
        }

        if (results.ValueKind == JsonValueKind.Object)
        {
            return [ToFeatureEditResult(results)];
        }

        if (results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return results.EnumerateArray()
            .Where(result => result.ValueKind == JsonValueKind.Object)
            .Select(ToFeatureEditResult)
            .ToArray();
    }

    private static FeatureEditResult ToFeatureEditResult(JsonElement element)
    {
        var hasSuccess = TryGetBool(element, out var succeeded, "success", "succeeded");
        var error = TryGetJsonProperty(element, out var errorElement, "error") && errorElement.ValueKind == JsonValueKind.Object
            ? ToFeatureEditError(errorElement)
            : null;

        var hasId = TryGetString(element, out var id, "globalId", "globalid", "id");
        var hasObjectId = TryGetInt64(element, out var objectId, "objectId", "objectid", "objectID", "oid");

        return new FeatureEditResult
        {
            Id = hasId ? id : null,
            ObjectId = hasObjectId ? objectId : null,
            Succeeded = hasSuccess ? succeeded : error is null && (hasId || hasObjectId),
            Error = error,
        };
    }

    private static FeatureEditError ToFeatureEditError(JsonElement element)
        => new()
        {
            Code = TryGetInt32(element, out var code, "code")
                ? code
                : null,
            Message = TryGetString(element, out var message, "message", "description") && message is not null
                ? message
                : "Unknown FeatureServer edit error.",
        };

    private static IReadOnlyList<FeatureAttachmentInfo> ToFeatureAttachmentInfos(
        JsonElement root,
        FeatureSource source,
        long parentObjectId)
    {
        var attachments = new List<FeatureAttachmentInfo>();
        if (TryGetJsonProperty(root, out var groups, "attachmentGroups") && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var groupParentObjectId = TryGetInt64(group, out var parsedParentObjectId, "parentObjectId", "objectId", "objectid")
                    ? parsedParentObjectId
                    : parentObjectId;
                AddAttachmentInfos(attachments, group, source, groupParentObjectId);
            }
        }

        AddAttachmentInfos(attachments, root, source, parentObjectId);
        return attachments;
    }

    private static void AddAttachmentInfos(
        List<FeatureAttachmentInfo> attachments,
        JsonElement element,
        FeatureSource source,
        long parentObjectId)
    {
        if (!TryGetJsonProperty(element, out var infos, "attachmentInfos") || infos.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var info in infos.EnumerateArray())
        {
            if (info.ValueKind == JsonValueKind.Object)
            {
                attachments.Add(ToFeatureAttachmentInfo(info, source, parentObjectId));
            }
        }
    }

    private static FeatureAttachmentInfo ToFeatureAttachmentInfo(JsonElement element, FeatureSource source, long parentObjectId)
    {
        var parsedParentObjectId = TryGetInt64(element, out var attachmentParentObjectId, "parentObjectId", "objectId", "objectid")
            ? attachmentParentObjectId
            : parentObjectId;
        var url = TryGetString(element, out var rawUrl, "url") && !string.IsNullOrWhiteSpace(rawUrl)
            ? new Uri(rawUrl, UriKind.RelativeOrAbsolute)
            : null;

        return new FeatureAttachmentInfo
        {
            Source = source,
            ParentObjectId = parsedParentObjectId,
            AttachmentId = TryGetInt64(element, out var attachmentId, "id", "attachmentId")
                ? attachmentId
                : 0,
            GlobalId = TryGetString(element, out var globalId, "globalId", "globalid")
                ? globalId
                : null,
            Name = TryGetString(element, out var name, "name")
                ? name
                : string.Empty,
            ContentType = TryGetString(element, out var contentType, "contentType")
                ? contentType
                : "application/octet-stream",
            Size = TryGetInt64(element, out var size, "size")
                ? size
                : 0,
            Keywords = TryGetString(element, out var keywords, "keywords")
                ? keywords
                : null,
            Url = url,
        };
    }

    private static bool TryGetJsonProperty(JsonElement element, out JsonElement property, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out property))
            {
                return true;
            }
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (propertyNames.Any(name => string.Equals(name, candidate.Name, StringComparison.OrdinalIgnoreCase)))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, out string? value, params string[] propertyNames)
    {
        if (TryGetJsonProperty(element, out var property, propertyNames))
        {
            if (property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return !string.IsNullOrWhiteSpace(value);
            }

            if (property.ValueKind == JsonValueKind.Number)
            {
                value = property.GetRawText();
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryGetInt32(JsonElement element, out int value, params string[] propertyNames)
    {
        if (TryGetInt64(element, out var parsed, propertyNames) &&
            parsed >= int.MinValue &&
            parsed <= int.MaxValue)
        {
            value = (int)parsed;
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetInt64(JsonElement element, out long value, params string[] propertyNames)
    {
        if (TryGetJsonProperty(element, out var property, propertyNames))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.String &&
                long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetBool(JsonElement element, out bool value, params string[] propertyNames)
    {
        if (TryGetJsonProperty(element, out var property, propertyNames))
        {
            if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.GetBoolean();
                return true;
            }

            if (property.ValueKind == JsonValueKind.String &&
                bool.TryParse(property.GetString(), out value))
            {
                return true;
            }
        }

        value = default;
        return false;
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
        using var request = new HttpRequestMessage(method, BuildAbsoluteUri(relativePath, query));
        request.Content = content;
        request.Headers.UserAgent.Clear();
        request.Headers.UserAgent.Add(_userAgent);
        await ApplyHttpAuthenticationAsync(request, ct).ConfigureAwait(false);

        // Apply per-request timeout via a linked cancellation token so the caller's
        // HttpClient (potentially owned by IHttpClientFactory) is not mutated.
        using var timeoutCts = new CancellationTokenSource(_requestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TaskCanceledException("Honua mobile request timed out.");
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                throw new HonuaMobileApiException(
                    response.StatusCode,
                    $"Honua mobile request failed with status {(int)response.StatusCode} {response.ReasonPhrase}",
                    raw);
            }

            if (response.Content.Headers.ContentLength == 0)
            {
                return JsonDocument.Parse("{}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
            try
            {
                return await JsonDocument.ParseAsync(stream, default, linkedCts.Token).ConfigureAwait(false);
            }
            catch (JsonException ex)
            {
                // Preserve the prior contract: whitespace-only bodies parse as an empty object,
                // anything else surfaces as a HonuaMobileApiException with the invalid payload.
                throw new HonuaMobileApiException("Honua mobile request returned invalid JSON.", ex);
            }
        }
        finally
        {
            response.Dispose();
        }
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

        return _grpcClient.Value;
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

        return new Uri(_baseUri, request.RequestUri);
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

    private Uri BuildAbsoluteUri(string relativePath, IReadOnlyDictionary<string, string?>? query)
    {
        var relative = BuildUri(relativePath, query);
        return relative.IsAbsoluteUri ? relative : new Uri(_baseUri, relative);
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
