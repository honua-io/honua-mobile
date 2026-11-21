// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.GeoServices.FeatureServer;
using Honua.Sdk.GeoServices.FeatureServer.Exceptions;
using Honua.Sdk.Grpc;
using Honua.Sdk.OgcFeatures.Exceptions;
using FeatureServerRequestConverters = Honua.Sdk.GeoServices.FeatureServer.Conversion.RequestConverters;

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
    public Task<FeatureEditResponse> ApplyEditsAsync(FeatureEditRequest request, CancellationToken ct = default)
        => ApplyEditsAsync(request, idempotencyKey: null, ct);

    /// <summary>
    /// Applies feature edits through the SDK provider-neutral feature contract, attaching a stable
    /// <c>Idempotency-Key</c> so the server can dedupe a retried edit (at-most-once).
    /// </summary>
    /// <remarks>
    /// When <paramref name="idempotencyKey"/> is supplied the FeatureServer edit is sent over REST even
    /// if gRPC is preferred, because the gRPC edit contract does not yet carry the idempotency header;
    /// routing over REST is what makes a post-crash re-upload safe. Callers that do not need at-most-once
    /// semantics can pass <see langword="null"/> to keep the default (gRPC-preferred) transport selection.
    /// </remarks>
    /// <param name="request">SDK feature edit request.</param>
    /// <param name="idempotencyKey">Stable client-generated key (≤200 chars, no control characters), or <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A provider-neutral feature edit response.</returns>
    public async Task<FeatureEditResponse> ApplyEditsAsync(
        FeatureEditRequest request,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (HasOgcSource(request.Source))
        {
            return await ApplyOgcSdkEditsAsync(request, ct).ConfigureAwait(false);
        }

        if (HasFeatureServerSource(request.Source))
        {
            return await ApplyFeatureServerSdkEditsAsync(request, idempotencyKey, ct).ConfigureAwait(false);
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

    private async IAsyncEnumerable<T> StreamWithGrpcFallbackAsync<T>(
        Func<CancellationToken, IAsyncEnumerable<T>>? grpcStreamFactory,
        Func<CancellationToken, IAsyncEnumerable<T>> restFallbackFactory,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (grpcStreamFactory is not null)
        {
            var yieldedGrpcPage = false;
            var grpcSucceeded = false;
            await using var grpcEnumerator = grpcStreamFactory(ct).GetAsyncEnumerator(ct);

            while (true)
            {
                T? nextPage = default;
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

        await foreach (var page in restFallbackFactory(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            yield return page;
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

    private IAsyncEnumerable<FeatureQueryResult> QueryFeatureServerSdkPagesAsync(
        FeatureQueryRequest request,
        CancellationToken ct)
        => StreamWithGrpcFallbackAsync(
            CanUseGrpcForQueries
                ? token => GetGrpcClient().QueryPagesAsync(request, token)
                : null,
            token => QueryFeatureServerSdkRestPagesAsync(request, token),
            ct);

    private async IAsyncEnumerable<FeatureQueryResult> QueryFeatureServerSdkRestPagesAsync(
        FeatureQueryRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
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

    private async Task<FeatureEditResponse> ApplyFeatureServerSdkEditsAsync(
        FeatureEditRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) && CanUseGrpcForEdits)
        {
            try
            {
                return await GetGrpcClient().ApplyEditsAsync(request, ct).ConfigureAwait(false);
            }
            catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcEditFailure)
            {
                // Fall through to REST transport. Disabled by default for edits.
            }
        }

        using var raw = await ApplyFeatureServerSdkEditsRestAsync(request, idempotencyKey, ct).ConfigureAwait(false);
        return ToFeatureServerFeatureEditResponse(raw.RootElement);
    }

    private async Task<JsonDocument> ApplyFeatureServerSdkEditsRestAsync(
        FeatureEditRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        var source = request.Source;
        var serviceId = source.ServiceId
            ?? throw new InvalidOperationException("FeatureServer feature edits require a service ID.");
        var layerId = source.LayerId
            ?? throw new InvalidOperationException("FeatureServer feature edits require a layer ID.");

        var editRequest = new ApplyEditsRequest
        {
            ServiceId = serviceId,
            LayerId = layerId,
            Adds = request.Adds.Count > 0 ? [.. request.Adds] : null,
            Updates = request.Updates.Count > 0 ? [.. request.Updates] : null,
            Deletes = ResolveFeatureServerDeleteObjectIds(request),
            RollbackOnFailure = request.RollbackOnFailure,
            ForceWrite = request.ForceWrite,
        };

        var path = $"/rest/services/{EscapePathSegments(serviceId)}/FeatureServer/{layerId}/applyEdits";
        return await SendJsonAsync(
            HttpMethod.Post,
            path,
            query: null,
            new FormUrlEncodedContent(FeatureServerRequestConverters.ToFeatureServerEditFormParameters(editRequest)),
            ct,
            idempotencyKey).ConfigureAwait(false);
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

    private static IReadOnlyList<long> ResolveFeatureServerDeleteObjectIds(FeatureEditRequest request)
    {
        if (request.DeleteObjectIds is { Count: > 0 } objectIds)
        {
            return objectIds;
        }

        if (request.DeleteIds is not { Count: > 0 } ids)
        {
            return [];
        }

        return ids
            .Select(id => long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId)
                ? objectId
                : (long?)null)
            .Where(objectId => objectId.HasValue)
            .Select(objectId => objectId!.Value)
            .ToArray();
    }
}
