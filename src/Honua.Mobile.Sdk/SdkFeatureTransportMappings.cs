using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Mobile.Sdk.Models;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.GeoServices.FeatureServer.Models;
using Honua.Sdk.OgcFeatures.Models;

namespace Honua.Mobile.Sdk;

internal static class SdkFeatureTransportMappings
{
    public static FeatureServerEditRequest ToFeatureServerEditRequest(ApplyEditsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new FeatureServerEditRequest
        {
            Adds = ResolveFeatureServerFeatures(request.Adds, request.AddsJson, "adds"),
            Updates = ResolveFeatureServerFeatures(request.Updates, request.UpdatesJson, "updates"),
            Deletes = ResolveFeatureServerDeletes(request.Deletes, request.DeletesCsv),
            RollbackOnFailure = request.RollbackOnFailure,
            ForceWrite = request.ForceWrite,
        };
    }

    public static IReadOnlyDictionary<string, string> ToFeatureServerEditFormParameters(ApplyEditsRequest request)
    {
        var edit = ToFeatureServerEditRequest(request);
        var body = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["adds"] = edit.Adds is { Count: > 0 } ? SerializeFeatureServerFeatures(edit.Adds) : null,
            ["updates"] = edit.Updates is { Count: > 0 } ? SerializeFeatureServerFeatures(edit.Updates) : null,
            ["deletes"] = edit.Deletes is { Count: > 0 } ? JoinInvariant(edit.Deletes) : null,
            ["rollbackOnFailure"] = edit.RollbackOnFailure ? "true" : "false",
            ["forceWrite"] = edit.ForceWrite ? "true" : null,
        };

        return body
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value!);
    }

    public static FeatureServerFeature ToFeatureServerFeature(FeatureEditFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        return new FeatureServerFeature
        {
            Attributes = feature.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone()),
            Geometry = feature.Geometry.HasValue ? feature.Geometry.Value.Clone() : null,
        };
    }

    public static IReadOnlyList<long>? ToFeatureServerDeleteObjectIds(FeatureEditRequest request)
    {
        var objectIds = new List<long>(request.DeleteObjectIds);
        foreach (var id in request.DeleteIds)
        {
            if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId))
            {
                throw new ArgumentException("FeatureServer feature deletes require numeric feature IDs.", nameof(request));
            }

            objectIds.Add(objectId);
        }

        return objectIds.Count == 0 ? null : objectIds;
    }

    public static OgcFeature ToOgcFeature(FeatureEditFeature feature, string? featureId = null)
    {
        ArgumentNullException.ThrowIfNull(feature);

        var id = featureId ?? ResolveOptionalFeatureId(feature);
        return new OgcFeature
        {
            Id = id is null
                ? null
                : JsonSerializer.SerializeToElement(id, HonuaMobileSdkTransportJsonContext.Default.String),
            Geometry = feature.Geometry.HasValue ? feature.Geometry.Value.Clone() : null,
            Properties = feature.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone()),
        };
    }

    public static string ToOgcFeatureId(long objectId)
        => objectId.ToString(CultureInfo.InvariantCulture);

    public static FeatureServerQueryParams ToFeatureServerQueryParams(QueryFeaturesRequest request)
        => new()
        {
            Where = request.Where,
            ObjectIds = request.ObjectIds,
            OutFields = request.OutFields is { Count: > 0 } ? string.Join(',', request.OutFields) : "*",
            ReturnGeometry = request.ReturnGeometry,
            ResultOffset = request.ResultOffset,
            ResultRecordCount = request.ResultRecordCount,
            OrderByFields = request.OrderBy,
            ReturnDistinctValues = request.ReturnDistinct ? true : null,
            ReturnCountOnly = request.ReturnCountOnly ? true : null,
            ReturnIdsOnly = request.ReturnIdsOnly ? true : null,
            ReturnExtentOnly = request.ReturnExtentOnly ? true : null,
            Format = ToFeatureServerFormat(request.ResponseFormat),
        };

    public static OgcItemsParams ToOgcItemsParams(OgcItemsRequest request)
        => new()
        {
            Limit = request.Limit,
            Offset = request.Offset,
            Properties = request.PropertyNames is { Count: > 0 } ? string.Join(',', request.PropertyNames) : null,
            Filter = request.CqlFilter,
            Format = ToOgcFeaturesFormat(request.ResponseFormat),
        };

    public static OgcFeature ToOgcFeature(object feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (feature is OgcFeature ogcFeature)
        {
            return ogcFeature;
        }

        var json = feature switch
        {
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            FeatureEditFeature editFeature => JsonSerializer.Serialize(
                ToOgcFeature(editFeature),
                HonuaMobileSdkTransportJsonContext.Default.OgcFeature),
            _ => SerializeWithContext(feature),
        };

        return JsonSerializer.Deserialize(json, HonuaMobileSdkTransportJsonContext.Default.OgcFeature)
            ?? throw new ArgumentException("OGC feature payload could not be deserialized.", nameof(feature));
    }

    public static JsonElement ToJsonElement(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value switch
        {
            JsonElement element => element.Clone(),
            JsonDocument document => document.RootElement.Clone(),
            _ => SerializeToElementWithContext(value),
        };
    }

    public static JsonDocument ToJsonDocument(FeatureServerQueryResponse response)
        => JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            HonuaMobileSdkTransportJsonContext.Default.FeatureServerQueryResponse));

    public static JsonDocument ToJsonDocument(FeatureServerEditResponse response)
        => JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            HonuaMobileSdkTransportJsonContext.Default.FeatureServerEditResponse));

    public static JsonDocument ToJsonDocument(IReadOnlyList<OgcCollection> collections)
        => JsonDocument.Parse(JsonSerializer.Serialize(
            new OgcCollectionsEnvelope { Collections = collections },
            HonuaMobileSdkTransportJsonContext.Default.OgcCollectionsEnvelope));

    public static JsonDocument ToJsonDocument(OgcFeatureCollection response)
        => JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            HonuaMobileSdkTransportJsonContext.Default.OgcFeatureCollection));

    public static JsonDocument ToJsonDocument(OgcFeature response)
        => JsonDocument.Parse(JsonSerializer.Serialize(
            response,
            HonuaMobileSdkTransportJsonContext.Default.OgcFeature));

    private static IReadOnlyList<FeatureServerFeature>? ResolveFeatureServerFeatures(
        IReadOnlyList<FeatureServerFeature>? features,
        string? json,
        string payloadName)
    {
        if (features is { Count: > 0 })
        {
            return features;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return features;
        }

        try
        {
            return JsonSerializer.Deserialize(json, HonuaMobileSdkTransportJsonContext.Default.FeatureServerFeatureArray);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"FeatureServer {payloadName} JSON payload is invalid.", payloadName, ex);
        }
    }

    private static IReadOnlyList<long>? ResolveFeatureServerDeletes(IReadOnlyList<long>? deletes, string? deletesCsv)
    {
        if (deletes is { Count: > 0 })
        {
            return deletes;
        }

        if (string.IsNullOrWhiteSpace(deletesCsv))
        {
            return deletes;
        }

        var objectIds = new List<long>();
        foreach (var value in deletesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId))
            {
                throw new ArgumentException("FeatureServer deletesCsv payload must contain numeric object IDs.", nameof(deletesCsv));
            }

            objectIds.Add(objectId);
        }

        return objectIds;
    }

    private static string SerializeFeatureServerFeatures(IReadOnlyList<FeatureServerFeature> features)
        => JsonSerializer.Serialize(
            features.ToArray(),
            HonuaMobileSdkTransportJsonContext.Default.FeatureServerFeatureArray);

    private static string SerializeWithContext(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, value.GetType(), HonuaMobileSdkTransportJsonContext.Default);
        }
        catch (NotSupportedException ex)
        {
            throw new ArgumentException(
                "OGC feature payload must be an OgcFeature, FeatureEditFeature, JsonElement, JsonDocument, or a source-generated JSON type.",
                nameof(value),
                ex);
        }
    }

    private static JsonElement SerializeToElementWithContext(object value)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, value.GetType(), HonuaMobileSdkTransportJsonContext.Default);
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (NotSupportedException ex)
        {
            throw new ArgumentException(
                "OGC patch payload must be a JsonElement, JsonDocument, or a source-generated JSON type.",
                nameof(value),
                ex);
        }
    }

    private static string JoinInvariant(IEnumerable<long> values)
        => string.Join(',', values.Select(value => value.ToString(CultureInfo.InvariantCulture)));

    private static string? ResolveOptionalFeatureId(FeatureEditFeature feature)
        => !string.IsNullOrWhiteSpace(feature.Id)
            ? feature.Id
            : feature.ObjectId?.ToString(CultureInfo.InvariantCulture);

    private static FeatureServerFormat? ToFeatureServerFormat(string? responseFormat)
        => responseFormat?.Trim().ToLowerInvariant() switch
        {
            null or "" or "json" => FeatureServerFormat.Json,
            "geojson" => FeatureServerFormat.GeoJson,
            "pbf" => FeatureServerFormat.Pbf,
            "flatgeobuf" => FeatureServerFormat.FlatGeobuf,
            "parquet" => FeatureServerFormat.Parquet,
            _ => null,
        };

    private static OgcFeaturesFormat? ToOgcFeaturesFormat(string? responseFormat)
        => responseFormat?.Trim().ToLowerInvariant() switch
        {
            null or "" or "json" => OgcFeaturesFormat.Json,
            "geojson" => OgcFeaturesFormat.GeoJson,
            "html" => OgcFeaturesFormat.Html,
            "gml" => OgcFeaturesFormat.Gml,
            "csv" => OgcFeaturesFormat.Csv,
            "flatgeobuf" => OgcFeaturesFormat.FlatGeobuf,
            "parquet" => OgcFeaturesFormat.Parquet,
            _ => null,
        };
}

internal sealed class OgcCollectionsEnvelope
{
    [JsonPropertyName("collections")]
    public IReadOnlyList<OgcCollection> Collections { get; init; } = [];
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FeatureServerFeature[]))]
[JsonSerializable(typeof(FeatureServerQueryResponse))]
[JsonSerializable(typeof(FeatureServerEditResponse))]
[JsonSerializable(typeof(OgcCollectionsEnvelope))]
[JsonSerializable(typeof(OgcFeatureCollection))]
[JsonSerializable(typeof(OgcFeature))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string))]
internal sealed partial class HonuaMobileSdkTransportJsonContext : JsonSerializerContext
{
}
