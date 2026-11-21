// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Grpc;

namespace Honua.Mobile.Sdk;

public sealed partial class HonuaMobileClient
{
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

    private bool CanUseGrpcForLegacyQuery(QueryFeaturesRequest request)
        => CanUseGrpcForQueries && !request.ReturnExtentOnly;

    /// <summary>
    /// Streams feature query results as multiple pages via gRPC server streaming, falling back to a single REST page on failure.
    /// </summary>
    /// <param name="request">The query parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async sequence of <see cref="JsonDocument"/> pages.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    public IAsyncEnumerable<JsonDocument> QueryFeaturesStreamAsync(
        QueryFeaturesRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return StreamWithGrpcFallbackAsync(
            CanUseGrpcForLegacyQuery(request)
                ? token => QueryFeaturesGrpcPagesAsync(request, token)
                : null,
            token => QueryFeaturesRestSinglePageAsync(request, token),
            ct);
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
    /// retried edit (at-most-once). When <paramref name="idempotencyKey"/> is
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
                // Fall through to REST transport. Disabled by default for edits.
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

    private async IAsyncEnumerable<JsonDocument> QueryFeaturesRestSinglePageAsync(
        QueryFeaturesRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return await QueryFeaturesRestAsync(request, ct).ConfigureAwait(false);
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
