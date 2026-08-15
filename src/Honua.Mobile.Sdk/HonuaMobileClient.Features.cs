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
    /// <c>Idempotency-Key</c> so the server can dedupe a retried edit (at-most-once, honua-server #2250).
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
            // OGC sources delegate to the SDK OGC client, which has no idempotency-header surface,
            // so the key cannot be honored on this path. OGC update (PATCH/PUT by id) and delete
            // (by id) are naturally idempotent; for an at-most-once OGC create use
            // CreateOgcItemAsync(request, idempotencyKey, ct), which carries the Idempotency-Key
            // header on the POST.
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

        if (CanUseGrpcForLegacyQuery(request))
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
    /// gRPC eligibility for the legacy Esri-JSON query surface. The metadata-only response
    /// modes are forced onto REST because the gRPC-to-legacy converter
    /// (<see cref="ToLegacyFeatureQueryJsonDocument"/>) always emits a <c>features</c> array,
    /// which is non-conformant for these modes:
    /// <list type="bullet">
    /// <item><c>returnExtentOnly</c> must return an extent envelope, not features.</item>
    /// <item><c>returnIdsOnly</c> must return <c>objectIdFieldName</c> + <c>objectIds</c> and omit features.</item>
    /// <item><c>returnCountOnly</c> must return only a <c>count</c>.</item>
    /// </list>
    /// REST honors all three.
    /// </summary>
    private bool CanUseGrpcForLegacyQuery(QueryFeaturesRequest request)
        => CanUseGrpcForQueries
            && !request.ReturnExtentOnly
            && !request.ReturnIdsOnly
            && !request.ReturnCountOnly;

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

        if (CanUseGrpcForLegacyQuery(request))
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
    /// Prefers gRPC transport when available. Unlike queries, a failed gRPC edit does
    /// <em>not</em> fall back to REST unless <see cref="HonuaMobileClientOptions.AllowRestFallbackOnGrpcEditFailure"/>
    /// is enabled, because re-issuing a non-idempotent edit could double-apply it.
    /// </summary>
    /// <param name="request">The edit payload including adds, updates, and deletes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing per-feature edit results.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="HonuaMobileApiException">Thrown when the server returns a non-success HTTP status code.</exception>
    public Task<JsonDocument> ApplyEditsAsync(ApplyEditsRequest request, CancellationToken ct = default)
        => ApplyEditsAsync(request, idempotencyKey: null, ct);

    /// <summary>
    /// Applies feature edits, attaching a stable <c>Idempotency-Key</c> so the server can dedupe a
    /// retried edit (at-most-once, honua-server #2250). When <paramref name="idempotencyKey"/> is
    /// supplied the edit is sent over REST even if gRPC is preferred, because the gRPC edit contract
    /// does not yet carry the idempotency header.
    /// </summary>
    /// <param name="request">The edit payload including adds, updates, and deletes.</param>
    /// <param name="idempotencyKey">Stable client-generated key (≤200 chars, no control characters), or <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="JsonDocument"/> containing per-feature edit results.</returns>
    public async Task<JsonDocument> ApplyEditsAsync(
        ApplyEditsRequest request,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(idempotencyKey) && CanUseGrpcForEdits)
        {
            try
            {
                return await ApplyEditsGrpcAsync(request, ct).ConfigureAwait(false);
            }
            catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcEditFailure)
            {
                // Fall through to REST transport. Disabled by default for edits:
                // re-issuing a non-idempotent edit over REST could double-apply it.
            }
        }

        return await ApplyEditsRestAsync(request, idempotencyKey, ct).ConfigureAwait(false);
    }

    private async Task<JsonDocument> QueryFeaturesGrpcAsync(QueryFeaturesRequest request, CancellationToken ct)
    {
        var response = await GetGrpcClient()
            .QueryAsync(ToSdkFeatureQueryRequest(request), ct)
            .ConfigureAwait(false);
        return ToLegacyFeatureQueryJsonDocument(response, request.ReturnCountOnly);
    }

    private async IAsyncEnumerable<JsonDocument> QueryFeaturesGrpcPagesAsync(
        QueryFeaturesRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var page in GetGrpcClient()
            .QueryPagesAsync(ToSdkFeatureQueryRequest(request), ct)
            .ConfigureAwait(false))
        {
            yield return ToLegacyFeatureQueryJsonDocument(page, request.ReturnCountOnly);
        }
    }

    private async Task<JsonDocument> ApplyEditsGrpcAsync(ApplyEditsRequest request, CancellationToken ct)
    {
        var response = await GetGrpcClient()
            .ApplyEditsAsync(ToSdkFeatureEditRequest(request), ct)
            .ConfigureAwait(false);
        return ToLegacyFeatureEditJsonDocument(response);
    }

    private async Task<JsonDocument> QueryFeaturesRestAsync(QueryFeaturesRequest request, CancellationToken ct)
    {
        var path = $"/rest/services/{EscapePathSegments(request.ServiceId)}/FeatureServer/{request.LayerId}/query";
        return await SendJsonAsync(
            HttpMethod.Get,
            path,
            BuildFeatureServerQueryParameters(request),
            content: null,
            ct).ConfigureAwait(false);
    }

    private async Task<JsonDocument> ApplyEditsRestAsync(ApplyEditsRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var path = $"/rest/services/{EscapePathSegments(request.ServiceId)}/FeatureServer/{request.LayerId}/applyEdits";
        return await SendJsonAsync(
            HttpMethod.Post,
            path,
            query: null,
            new FormUrlEncodedContent(BuildFeatureServerEditFormParameters(request)),
            ct,
            idempotencyKey).ConfigureAwait(false);
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

    private async Task<FeatureEditResponse> ApplyFeatureServerSdkEditsAsync(
        FeatureEditRequest request,
        string? idempotencyKey,
        CancellationToken ct)
    {
        // gRPC cannot carry the Idempotency-Key header, so when an idempotency key is supplied we go
        // straight to REST: that is the only transport that delivers the at-most-once guarantee the
        // caller is asking for. Without a key, keep the default gRPC-preferred path (fallback to REST
        // is still gated off by default for non-idempotent edits).
        if (string.IsNullOrWhiteSpace(idempotencyKey) && CanUseGrpcForEdits)
        {
            try
            {
                return await GetGrpcClient().ApplyEditsAsync(request, ct).ConfigureAwait(false);
            }
            catch (HonuaGrpcException) when (_options.AllowRestFallbackOnGrpcEditFailure)
            {
                // Fall through to REST transport. Disabled by default for edits:
                // re-issuing a non-idempotent edit over REST could double-apply it.
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

    private static FeatureQueryRequest ToSdkFeatureQueryRequest(QueryFeaturesRequest request)
        => new()
        {
            Source = new FeatureSource
            {
                ServiceId = request.ServiceId,
                LayerId = request.LayerId,
            },
            Filter = request.Where,
            ObjectIds = request.ObjectIds is { Count: > 0 } objectIds ? [.. objectIds] : [],
            OutFields = request.OutFields is { Count: > 0 } outFields ? [.. outFields] : [],
            ReturnGeometry = request.ReturnGeometry,
            Offset = request.ResultOffset,
            Limit = request.ResultRecordCount,
            OrderBy = request.OrderBy,
            ReturnDistinct = request.ReturnDistinct,
            ReturnCountOnly = request.ReturnCountOnly,
            ReturnIdsOnly = request.ReturnIdsOnly,
            ReturnExtentOnly = request.ReturnExtentOnly,
        };

    private static FeatureEditRequest ToSdkFeatureEditRequest(ApplyEditsRequest request)
        => new()
        {
            Source = new FeatureSource
            {
                ServiceId = request.ServiceId,
                LayerId = request.LayerId,
            },
            Adds = request.Adds is { Count: > 0 } adds ? [.. adds] : [],
            Updates = request.Updates is { Count: > 0 } updates ? [.. updates] : [],
            DeleteObjectIds = ResolveFeatureServerDeleteObjectIds(request),
            RollbackOnFailure = request.RollbackOnFailure,
            ForceWrite = request.ForceWrite,
        };

    private static IReadOnlyDictionary<string, string?> BuildFeatureServerQueryParameters(QueryFeaturesRequest request)
    {
        var query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["where"] = string.IsNullOrWhiteSpace(request.Where) ? "1=1" : request.Where,
            ["outFields"] = request.OutFields is { Count: > 0 } outFields ? string.Join(',', outFields) : "*",
            ["returnGeometry"] = request.ReturnGeometry.ToString().ToLowerInvariant(),
            ["f"] = string.IsNullOrWhiteSpace(request.ResponseFormat) ? "json" : request.ResponseFormat,
        };

        if (request.ObjectIds is { Count: > 0 } objectIds)
        {
            query["objectIds"] = string.Join(',', objectIds);
        }

        if (request.ResultOffset is { } resultOffset)
        {
            query["resultOffset"] = resultOffset.ToString(CultureInfo.InvariantCulture);
        }

        if (request.ResultRecordCount is { } resultRecordCount)
        {
            query["resultRecordCount"] = resultRecordCount.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(request.OrderBy))
        {
            query["orderByFields"] = request.OrderBy;
        }

        if (request.ReturnDistinct)
        {
            query["returnDistinctValues"] = "true";
        }

        if (request.ReturnCountOnly)
        {
            query["returnCountOnly"] = "true";
        }

        if (request.ReturnIdsOnly)
        {
            query["returnIdsOnly"] = "true";
        }

        if (request.ReturnExtentOnly)
        {
            query["returnExtentOnly"] = "true";
        }

        return query;
    }

    private static IReadOnlyDictionary<string, string?> BuildFeatureServerEditFormParameters(ApplyEditsRequest request)
    {
        var form = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["rollbackOnFailure"] = request.RollbackOnFailure.ToString().ToLowerInvariant(),
            ["forceWrite"] = request.ForceWrite.ToString().ToLowerInvariant(),
            ["f"] = string.IsNullOrWhiteSpace(request.ResponseFormat) ? "json" : request.ResponseFormat,
        };

        if (!string.IsNullOrWhiteSpace(request.AddsJson))
        {
            form["adds"] = request.AddsJson;
        }
        else if (request.Adds is { Count: > 0 } adds)
        {
            form["adds"] = SerializeFeatureServerEditFeatures(adds);
        }

        if (!string.IsNullOrWhiteSpace(request.UpdatesJson))
        {
            form["updates"] = request.UpdatesJson;
        }
        else if (request.Updates is { Count: > 0 } updates)
        {
            form["updates"] = SerializeFeatureServerEditFeatures(updates);
        }

        if (request.Deletes is { Count: > 0 } deletes)
        {
            form["deletes"] = string.Join(',', deletes);
        }
        else if (!string.IsNullOrWhiteSpace(request.DeletesCsv))
        {
            form["deletes"] = request.DeletesCsv;
        }

        return form;
    }

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

    private static IReadOnlyList<long> ResolveFeatureServerDeleteObjectIds(ApplyEditsRequest request)
    {
        if (request.Deletes is { Count: > 0 } deletes)
        {
            return deletes;
        }

        if (string.IsNullOrWhiteSpace(request.DeletesCsv))
        {
            return [];
        }

        return request.DeletesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId)
                ? objectId
                : (long?)null)
            .Where(objectId => objectId.HasValue)
            .Select(objectId => objectId!.Value)
            .ToArray();
    }

    private static string SerializeFeatureServerEditFeatures(IReadOnlyList<FeatureEditFeature> features)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var feature in features)
            {
                WriteFeatureServerEditFeature(writer, feature);
            }

            writer.WriteEndArray();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.GetRawText();
    }

    private static void WriteFeatureServerEditFeature(Utf8JsonWriter writer, FeatureEditFeature feature)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("attributes");
        WriteAttributesObject(writer, feature.Attributes, feature.ObjectId);

        if (feature.Geometry is { } geometry && geometry.ValueKind != JsonValueKind.Undefined)
        {
            writer.WritePropertyName("geometry");
            geometry.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Projects a gRPC/SDK <see cref="FeatureQueryResult"/> into the legacy Esri-style query JSON.
    /// </summary>
    /// <remarks>
    /// Transport-parity note: the gRPC <see cref="FeatureQueryResult"/> contract does not currently
    /// carry <c>spatialReference</c>, <c>geometryType</c>, or the layer <c>fields</c> that the raw
    /// REST FeatureServer response passes through. Callers that need the spatial reference or field
    /// schema must read them from layer metadata when the gRPC transport is used. Closing this gap
    /// fully requires the SDK/server feature-query contract to surface those values.
    /// <paramref name="returnCountOnly"/> gates the top-level <c>count</c>: Esri reserves it for
    /// <c>returnCountOnly</c> responses, so it is omitted for normal feature queries.
    /// </remarks>
    internal static JsonDocument ToLegacyFeatureQueryJsonDocument(FeatureQueryResult response, bool returnCountOnly)
        => CreateJsonDocument(writer =>
        {
            writer.WriteStartObject();

            if (!string.IsNullOrWhiteSpace(response.ObjectIdFieldName))
            {
                writer.WriteString("objectIdFieldName", response.ObjectIdFieldName);
            }

            if (returnCountOnly && response.NumberMatched is { } numberMatched)
            {
                writer.WriteNumber("count", numberMatched);
            }

            if (response.ObjectIds is { Count: > 0 } objectIds)
            {
                writer.WritePropertyName("objectIds");
                writer.WriteStartArray();
                foreach (var objectId in objectIds)
                {
                    writer.WriteNumberValue(objectId);
                }

                writer.WriteEndArray();
            }

            if (response.HasMoreResults is true)
            {
                writer.WriteBoolean("exceededTransferLimit", true);
            }

            writer.WritePropertyName("features");
            writer.WriteStartArray();
            foreach (var feature in response.Features)
            {
                writer.WriteStartObject();

                if (!string.IsNullOrWhiteSpace(feature.Id))
                {
                    writer.WriteString("id", feature.Id);
                }

                writer.WritePropertyName("attributes");
                WriteAttributesObject(writer, feature.Attributes);

                if (feature.Geometry is { } geometry && geometry.ValueKind != JsonValueKind.Undefined)
                {
                    writer.WritePropertyName("geometry");
                    geometry.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    private static JsonDocument ToLegacyFeatureEditJsonDocument(FeatureEditResponse response)
        => CreateJsonDocument(writer =>
        {
            writer.WriteStartObject();

            WriteFeatureEditResults(writer, "addResults", response.AddResults);
            WriteFeatureEditResults(writer, "updateResults", response.UpdateResults);
            WriteFeatureEditResults(writer, "deleteResults", response.DeleteResults);

            if (response.PatchResults is { Count: > 0 } patchResults)
            {
                WriteFeatureEditResults(writer, "patchResults", patchResults);
            }

            if (response.Error is { } error)
            {
                writer.WritePropertyName("error");
                WriteFeatureEditError(writer, error);
            }

            writer.WriteEndObject();
        });

    private static JsonDocument CreateJsonDocument(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            write(writer);
        }

        return JsonDocument.Parse(stream.ToArray());
    }

    private static void WriteAttributesObject(
        Utf8JsonWriter writer,
        IReadOnlyDictionary<string, JsonElement> attributes,
        long? fallbackObjectId = null)
    {
        writer.WriteStartObject();
        foreach (var attribute in attributes)
        {
            writer.WritePropertyName(attribute.Key);
            attribute.Value.WriteTo(writer);
        }

        if (fallbackObjectId is { } objectId &&
            !HasAttribute(attributes, "objectId", "objectid", "OBJECTID"))
        {
            writer.WriteNumber("objectId", objectId);
        }

        writer.WriteEndObject();
    }

    private static bool HasAttribute(IReadOnlyDictionary<string, JsonElement> attributes, params string[] names)
        => names.Any(attributes.ContainsKey);

    private static void WriteFeatureEditResults(
        Utf8JsonWriter writer,
        string propertyName,
        IReadOnlyList<FeatureEditResult> results)
    {
        writer.WritePropertyName(propertyName);
        writer.WriteStartArray();
        foreach (var result in results)
        {
            writer.WriteStartObject();
            if (result.ObjectId is { } objectId)
            {
                writer.WriteNumber("objectId", objectId);
            }

            if (!string.IsNullOrWhiteSpace(result.Id))
            {
                writer.WriteString("globalId", result.Id);
            }

            writer.WriteBoolean("success", result.Succeeded);
            if (result.Error is { } error)
            {
                writer.WritePropertyName("error");
                WriteFeatureEditError(writer, error);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteFeatureEditError(Utf8JsonWriter writer, FeatureEditError error)
    {
        writer.WriteStartObject();
        if (error.Code is { } code)
        {
            writer.WriteNumber("code", code);
        }

        writer.WriteString("message", error.Message);
        writer.WriteEndObject();
    }
}
