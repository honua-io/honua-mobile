using System.Runtime.CompilerServices;
using System.Text.Json;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.GeoServices.FeatureServer;
using Honua.Sdk.GeoServices.FeatureServer.Exceptions;
using Honua.Sdk.Grpc;
using Honua.Sdk.OgcFeatures.Exceptions;

// SDK-owned conversion shims (live in honua-sdk-dotnet train >= 0.1.17-alpha.1).
// Aliased so the call-site identifiers stay close to the original local mappings.
using FeatureServerRequestConverters = Honua.Sdk.GeoServices.FeatureServer.Conversion.RequestConverters;
using GrpcRequestConverters = Honua.Sdk.Grpc.Conversion.MobileRequestConverters;

namespace Honua.Mobile.Sdk;

public sealed partial class HonuaMobileClient
{
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

    private static bool IsDefaultJsonResponseFormat(string? responseFormat)
        => string.IsNullOrWhiteSpace(responseFormat) ||
            string.Equals(responseFormat, "json", StringComparison.OrdinalIgnoreCase);
}
