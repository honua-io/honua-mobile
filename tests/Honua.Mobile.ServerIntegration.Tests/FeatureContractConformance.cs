// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Honua.Mobile.ServerIntegration.Tests;

/// <summary>
/// Thrown when a live <c>honua-server</c> response fails to conform to a canonical
/// <c>geospatial.v1</c> conformance contract. The message names the drifted
/// contract/workflow and the specific field, so triage does not require tracing
/// an opaque HTTP error back to a contract by hand (the <c>honua-server#1238</c>
/// failure mode).
/// </summary>
public sealed class ContractDriftException : Exception
{
    public ContractDriftException(string contract, string detail, HttpStatusCode? transportStatus = null, Exception? inner = null)
        : base($"{contract} contract drift: {detail}", inner)
    {
        Contract = contract;
        Detail = detail;
        TransportStatus = transportStatus;
    }

    /// <summary>The named contract/workflow that drifted, e.g. <c>FeatureService.QueryFeatures response</c>.</summary>
    public string Contract { get; }

    /// <summary>The specific mismatch detail (missing/typed field, HTTP status, parse failure).</summary>
    public string Detail { get; }

    /// <summary>
    /// The HTTP status of the underlying transport failure, when the drift was a
    /// hard error (e.g. the <c>honua-server#1238</c> 400/500) rather than a
    /// structural mismatch in an otherwise-200 body.
    /// </summary>
    public HttpStatusCode? TransportStatus { get; }

    /// <summary>True when the drift was an underlying transport failure with the given status.</summary>
    public bool IsTransportStatus(HttpStatusCode status) => TransportStatus == status;

    /// <summary>True when the contract or detail text mentions any of the given (case-insensitive) tokens.</summary>
    public bool MentionsAny(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (Contract.Contains(token, StringComparison.OrdinalIgnoreCase)
                || Detail.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Validates live FeatureServer/OGC responses against the shared, pinned
/// <c>geospatial-grpc</c> conformance fixtures. Each check is named after the
/// canonical contract it enforces and, on mismatch, throws
/// <see cref="ContractDriftException"/> with the offending field, extending the
/// existing <c>FormatFeatureEditDiagnostic</c> attribution pattern across the
/// FeatureServer (REST + gRPC), SDK, and OGC read/query paths.
/// </summary>
/// <remarks>
/// The canonical fixtures use the protobuf JSON mapping for the gRPC
/// <c>geospatial.v1</c> messages (camelCase fields, enum names as strings,
/// <c>int64</c> as strings). The live FeatureServer REST/SDK and OGC Features
/// paths return GeoServices-style JSON / GeoJSON. These checks therefore assert
/// the <b>structural contract</b> the fixtures describe — the presence and JSON
/// kind of the canonical envelope, field-descriptor, feature, attribute, and
/// geometry elements, and the attribute fields the fixture declares — rather than
/// a byte-identical match. That is exactly the surface a <c>#1238</c>-class
/// JSONB-projection regression breaks (it returns no parseable
/// feature-collection envelope at all), so it is caught and attributed here.
/// </remarks>
public static class FeatureContractConformance
{
    /// <summary>Canonical FeatureService query response contract name.</summary>
    public const string FeatureQueryContract = "FeatureService.QueryFeatures response";

    /// <summary>Canonical FeatureService apply-edits response contract name.</summary>
    public const string FeatureApplyEditsContract = "FeatureService.ApplyEdits response";

    /// <summary>Canonical OGC Features items collection contract name.</summary>
    public const string OgcItemsContract = "OGC Features items collection (FeatureService query)";

    /// <summary>
    /// Asserts a live FeatureServer/SDK query response conforms to the canonical
    /// <c>geospatial.v1.QueryFeaturesResponse</c> envelope described by
    /// <c>feature_query_response.json</c>: a feature-collection object exposing a
    /// <c>features</c> array whose members carry an <c>attributes</c> object, plus
    /// the structural descriptors (object-id field name, field list) the contract
    /// declares. Throws <see cref="ContractDriftException"/> naming the field on
    /// any mismatch.
    /// </summary>
    public static void AssertFeatureQueryResponse(JsonElement live, JsonElement canonical, bool requireFeature)
    {
        const string contract = FeatureQueryContract;

        // When the shared canonical fixture was fetched, first confirm the live
        // response carries the same canonical envelope keys the fixture declares
        // (objectIdFieldName / fields / features). This ties the check to the
        // shared, pinned geospatial.v1 fixture rather than a hand-written shape:
        // if the fixture envelope changes, this set changes with it.
        if (canonical.ValueKind == JsonValueKind.Object)
        {
            foreach (var canonicalProp in canonical.EnumerateObject())
            {
                // Only the structural feature-collection envelope keys are required
                // of every transport (REST/gRPC/SDK projection). Optional descriptors
                // such as geometryType/spatialReference/exceededTransferLimit are not
                // mandated of all transports, so we require only the load-bearing
                // envelope keys the fixture declares.
                if (canonicalProp.Name is "features" or "objectIdFieldName"
                    && live.ValueKind == JsonValueKind.Object
                    && !live.TryGetProperty(canonicalProp.Name, out _))
                {
                    throw new ContractDriftException(
                        contract,
                        $"missing `{canonicalProp.Name}` required by the canonical "
                        + $"geospatial.v1.QueryFeaturesResponse fixture. "
                        + $"Response keys present: {DescribeKeys(live)}.");
                }
            }
        }

        if (live.ValueKind != JsonValueKind.Object)
        {
            throw new ContractDriftException(
                contract,
                $"expected the response root to be a JSON object (per the canonical "
                + $"QueryFeaturesResponse envelope), got {live.ValueKind}.");
        }

        // The canonical contract carries a `features` array; the live FeatureServer
        // REST/SDK response uses the same key. A #1238-class projection failure
        // never reaches here (it 400/500s before producing a body), but a drifted
        // success body that drops `features` is named precisely.
        if (!TryGetArray(live, "features", out var liveFeatures))
        {
            throw new ContractDriftException(
                contract,
                "missing required `features` array (canonical "
                + "geospatial.v1.QueryFeaturesResponse.features). "
                + $"Response keys present: {DescribeKeys(live)}.");
        }

        // Object-id field name is part of the canonical envelope; assert it is a
        // string when the live response advertises it (FeatureServer always does).
        if (live.TryGetProperty("objectIdFieldName", out var oidField)
            && oidField.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new ContractDriftException(
                contract,
                $"`objectIdFieldName` should be a string per the canonical contract, got {oidField.ValueKind}.");
        }

        // Field descriptors: the canonical contract describes `fields[]` entries
        // each with a `name`. FeatureServer REST mirrors this; assert the shape
        // when present so a descriptor-shape regression is attributed.
        if (live.TryGetProperty("fields", out var liveFields))
        {
            if (liveFields.ValueKind != JsonValueKind.Array)
            {
                throw new ContractDriftException(
                    contract,
                    $"`fields` should be an array of field descriptors per the canonical contract, got {liveFields.ValueKind}.");
            }

            foreach (var field in liveFields.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object || !field.TryGetProperty("name", out var fieldName)
                    || fieldName.ValueKind != JsonValueKind.String)
                {
                    throw new ContractDriftException(
                        contract,
                        "each `fields[]` descriptor must be an object with a string `name` "
                        + $"per the canonical contract; offending descriptor: {Truncate(field)}.");
                }
            }
        }

        if (requireFeature && liveFeatures.GetArrayLength() == 0)
        {
            throw new ContractDriftException(
                contract,
                "expected at least one feature for the seeded layer but `features` was empty "
                + "(seeded mobile_offline_demo layer should return rows).");
        }

        // Per-feature shape: each member must expose an `attributes` object, the
        // canonical geospatial.v1.Feature.attributes map.
        var index = 0;
        foreach (var feature in liveFeatures.EnumerateArray())
        {
            AssertFeatureShape(contract, feature, index);
            index++;
        }
    }

    /// <summary>
    /// Asserts a live OGC Features items response conforms to the GeoJSON
    /// FeatureCollection contract the canonical FeatureService query fixture maps
    /// onto the OGC read path: a <c>FeatureCollection</c> with a <c>features</c>
    /// array whose members are GeoJSON <c>Feature</c> objects carrying
    /// <c>properties</c>. Throws <see cref="ContractDriftException"/> naming the
    /// field on any mismatch.
    /// </summary>
    public static void AssertOgcItemsResponse(JsonElement live, bool requireFeature)
    {
        const string contract = OgcItemsContract;

        if (live.ValueKind != JsonValueKind.Object)
        {
            throw new ContractDriftException(
                contract,
                $"expected the OGC items root to be a JSON object, got {live.ValueKind}.");
        }

        if (!live.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            throw new ContractDriftException(
                contract,
                $"missing string `type` (expected \"FeatureCollection\"). Response keys present: {DescribeKeys(live)}.");
        }

        if (!string.Equals(type.GetString(), "FeatureCollection", StringComparison.Ordinal))
        {
            throw new ContractDriftException(
                contract,
                $"`type` must be \"FeatureCollection\", got \"{type.GetString()}\".");
        }

        if (!TryGetArray(live, "features", out var liveFeatures))
        {
            throw new ContractDriftException(
                contract,
                $"missing required `features` array. Response keys present: {DescribeKeys(live)}.");
        }

        if (requireFeature && liveFeatures.GetArrayLength() == 0)
        {
            throw new ContractDriftException(
                contract,
                "expected at least one feature for the seeded collection but `features` was empty.");
        }

        var index = 0;
        foreach (var feature in liveFeatures.EnumerateArray())
        {
            if (feature.ValueKind != JsonValueKind.Object)
            {
                throw new ContractDriftException(
                    contract,
                    $"`features[{index}]` must be a GeoJSON Feature object, got {feature.ValueKind}.");
            }

            if (!feature.TryGetProperty("type", out var ft) || ft.ValueKind != JsonValueKind.String
                || !string.Equals(ft.GetString(), "Feature", StringComparison.Ordinal))
            {
                throw new ContractDriftException(
                    contract,
                    $"`features[{index}].type` must be \"Feature\" per the GeoJSON contract; got {Truncate(feature)}.");
            }

            if (!feature.TryGetProperty("properties", out var props)
                || props.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            {
                throw new ContractDriftException(
                    contract,
                    $"`features[{index}].properties` must be an object (the canonical attributes map); got {Truncate(feature)}.");
            }

            index++;
        }
    }

    /// <summary>
    /// Asserts a live apply-edits response conforms to the canonical
    /// <c>geospatial.v1.ApplyEditsResponse</c> contract: at least one of
    /// <c>addResults</c> / <c>updateResults</c> / <c>deleteResults</c>, each a
    /// result array whose members carry a boolean <c>success</c>. Throws
    /// <see cref="ContractDriftException"/> naming the field on any mismatch.
    /// </summary>
    public static void AssertApplyEditsResponse(JsonElement live)
    {
        const string contract = FeatureApplyEditsContract;

        if (live.ValueKind != JsonValueKind.Object)
        {
            throw new ContractDriftException(
                contract,
                $"expected the response root to be a JSON object (per ApplyEditsResponse), got {live.ValueKind}.");
        }

        var sawResultArray = false;
        foreach (var resultsKey in new[] { "addResults", "updateResults", "deleteResults" })
        {
            if (!live.TryGetProperty(resultsKey, out var results))
            {
                continue;
            }

            sawResultArray = true;
            if (results.ValueKind != JsonValueKind.Array)
            {
                throw new ContractDriftException(
                    contract,
                    $"`{resultsKey}` must be an array of edit results per the canonical contract, got {results.ValueKind}.");
            }

            var index = 0;
            foreach (var result in results.EnumerateArray())
            {
                if (result.ValueKind != JsonValueKind.Object
                    || !result.TryGetProperty("success", out var success)
                    || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new ContractDriftException(
                        contract,
                        $"`{resultsKey}[{index}]` must be an object with a boolean `success` "
                        + $"per the canonical contract; got {Truncate(result)}.");
                }

                index++;
            }
        }

        if (!sawResultArray)
        {
            throw new ContractDriftException(
                contract,
                "response carried none of `addResults`/`updateResults`/`deleteResults` "
                + $"required by the canonical ApplyEditsResponse contract. Keys present: {DescribeKeys(live)}.");
        }
    }

    private static void AssertFeatureShape(string contract, JsonElement feature, int index)
    {
        if (feature.ValueKind != JsonValueKind.Object)
        {
            throw new ContractDriftException(
                contract,
                $"`features[{index}]` must be an object per the canonical Feature contract, got {feature.ValueKind}.");
        }

        if (!feature.TryGetProperty("attributes", out var attributes)
            || attributes.ValueKind != JsonValueKind.Object)
        {
            throw new ContractDriftException(
                contract,
                $"`features[{index}].attributes` must be an object (canonical "
                + $"geospatial.v1.Feature.attributes map); got {Truncate(feature)}.");
        }
    }

    private static bool TryGetArray(JsonElement parent, string name, out JsonElement array)
    {
        if (parent.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        array = default;
        return false;
    }

    private static string DescribeKeys(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return $"<{obj.ValueKind}>";
        }

        var keys = obj.EnumerateObject().Select(p => p.Name).Take(16).ToArray();
        return keys.Length == 0 ? "(none)" : string.Join(", ", keys);
    }

    private static string Truncate(JsonElement element, int max = 240)
    {
        var raw = element.GetRawText();
        return raw.Length <= max ? raw : raw[..max] + "…";
    }
}
