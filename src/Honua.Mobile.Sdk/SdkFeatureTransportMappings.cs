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
    public static IReadOnlyDictionary<string, string?> ToFeatureServerQueryParameters(QueryFeaturesRequest request)
    {
        var query = ToFeatureServerQueryParams(request);
        return new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["where"] = query.Where ?? "1=1",
            ["objectIds"] = query.ObjectIds is { Count: > 0 } ? JoinInvariant(query.ObjectIds) : null,
            ["outFields"] = query.OutFields,
            ["returnGeometry"] = query.ReturnGeometry is false ? "false" : "true",
            ["resultOffset"] = query.ResultOffset?.ToString(CultureInfo.InvariantCulture),
            ["resultRecordCount"] = query.ResultRecordCount?.ToString(CultureInfo.InvariantCulture),
            ["orderByFields"] = query.OrderByFields,
            ["returnDistinctValues"] = query.ReturnDistinctValues is true ? "true" : null,
            ["returnCountOnly"] = request.ReturnCountOnly ? "true" : null,
            ["returnIdsOnly"] = request.ReturnIdsOnly ? "true" : null,
            ["returnExtentOnly"] = request.ReturnExtentOnly ? "true" : null,
        };
    }

    public static IReadOnlyDictionary<string, string> ToFeatureServerEditFormParameters(ApplyEditsRequest request)
    {
        var edit = new FeatureServerEditRequest
        {
            Adds = request.Adds,
            Updates = request.Updates,
            Deletes = request.Deletes,
            RollbackOnFailure = request.RollbackOnFailure,
        };

        var body = new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["adds"] = edit.Adds is { Count: > 0 } ? SerializeFeatureServerFeatures(edit.Adds) : request.AddsJson,
            ["updates"] = edit.Updates is { Count: > 0 } ? SerializeFeatureServerFeatures(edit.Updates) : request.UpdatesJson,
            ["deletes"] = edit.Deletes is { Count: > 0 } ? JoinInvariant(edit.Deletes) : request.DeletesCsv,
            ["rollbackOnFailure"] = edit.RollbackOnFailure ? "true" : "false",
            ["forceWrite"] = request.ForceWrite ? "true" : null,
        };

        return body
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value!);
    }

    public static IReadOnlyDictionary<string, string?> ToOgcItemsQueryParameters(OgcItemsRequest request)
    {
        var query = ToOgcItemsParams(request);
        return new Dictionary<string, string?>
        {
            ["f"] = request.ResponseFormat,
            ["limit"] = query.Limit?.ToString(CultureInfo.InvariantCulture),
            ["offset"] = query.Offset?.ToString(CultureInfo.InvariantCulture),
            ["properties"] = query.Properties,
            ["filter"] = query.Filter,
        };
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
            Id = id is null ? null : JsonSerializer.SerializeToElement(id),
            Geometry = feature.Geometry.HasValue ? feature.Geometry.Value.Clone() : null,
            Properties = feature.Attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone()),
        };
    }

    public static string ToOgcFeatureId(long objectId)
        => objectId.ToString(CultureInfo.InvariantCulture);

    public static string SerializeOgcFeature(object feature)
        => feature is OgcFeature ogcFeature
            ? JsonSerializer.Serialize(ogcFeature, HonuaMobileSdkTransportJsonContext.Default.OgcFeature)
            : JsonSerializer.Serialize(feature);

    private static FeatureServerQueryParams ToFeatureServerQueryParams(QueryFeaturesRequest request)
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
            Format = ToFeatureServerFormat(request.ResponseFormat),
        };

    private static OgcItemsParams ToOgcItemsParams(OgcItemsRequest request)
        => new()
        {
            Limit = request.Limit,
            Offset = request.Offset,
            Properties = request.PropertyNames is { Count: > 0 } ? string.Join(',', request.PropertyNames) : null,
            Filter = request.CqlFilter,
            Format = ToOgcFeaturesFormat(request.ResponseFormat),
        };

    private static string SerializeFeatureServerFeatures(IReadOnlyList<FeatureServerFeature> features)
        => JsonSerializer.Serialize(
            features.ToArray(),
            HonuaMobileSdkTransportJsonContext.Default.FeatureServerFeatureArray);

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

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(FeatureServerFeature[]))]
[JsonSerializable(typeof(OgcFeature))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class HonuaMobileSdkTransportJsonContext : JsonSerializerContext
{
}
