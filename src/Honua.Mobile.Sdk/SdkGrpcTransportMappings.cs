using System.Globalization;
using System.Text.Json;
using Honua.Mobile.Sdk.Models;
using Honua.Sdk.GeoServices.FeatureServer.Models;
using GrpcModels = Honua.Sdk.Grpc.Models;

namespace Honua.Mobile.Sdk;

internal static class SdkGrpcTransportMappings
{
    public static GrpcModels.QueryFeaturesRequest ToGrpcQueryRequest(QueryFeaturesRequest request)
        => new()
        {
            ServiceId = request.ServiceId,
            LayerId = request.LayerId,
            Where = request.Where,
            ObjectIds = request.ObjectIds,
            OutFields = request.OutFields,
            ReturnGeometry = request.ReturnGeometry,
            ResultOffset = request.ResultOffset ?? 0,
            ResultRecordCount = request.ResultRecordCount ?? 0,
            OrderBy = request.OrderBy ?? string.Empty,
            ReturnDistinct = request.ReturnDistinct,
            ReturnCountOnly = request.ReturnCountOnly,
            ReturnIdsOnly = request.ReturnIdsOnly,
            ReturnExtentOnly = request.ReturnExtentOnly,
        };

    public static GrpcModels.ApplyEditsRequest ToGrpcApplyEditsRequest(ApplyEditsRequest request)
        => new()
        {
            ServiceId = request.ServiceId,
            LayerId = request.LayerId,
            Adds = ToGrpcFeatures(request.Adds, request.AddsJson),
            Updates = ToGrpcFeatures(request.Updates, request.UpdatesJson),
            Deletes = ToGrpcDeleteObjectIds(request.Deletes, request.DeletesCsv),
            RollbackOnFailure = request.RollbackOnFailure,
            ForceWrite = request.ForceWrite,
        };

    public static JsonDocument ToJsonDocument(GrpcModels.QueryFeaturesResponse response)
    {
        var payload = new Dictionary<string, object?>
        {
            ["objectIdFieldName"] = response.ObjectIdFieldName,
            ["geometryType"] = response.GeometryType.ToString(),
            ["spatialReference"] = ToSpatialReference(response.SpatialReference),
            ["fields"] = response.Fields.Select(ToField).ToArray(),
            ["features"] = response.Features.Select(ToFeature).ToArray(),
            ["exceededTransferLimit"] = response.ExceededTransferLimit,
            ["count"] = response.Count,
            ["objectIds"] = response.ObjectIds.ToArray(),
            ["extent"] = ToExtent(response.Extent),
        };

        return JsonSerializer.SerializeToDocument(payload);
    }

    public static JsonDocument ToJsonDocument(GrpcModels.FeaturePage page)
    {
        var payload = new Dictionary<string, object?>
        {
            ["objectIdFieldName"] = page.ObjectIdFieldName,
            ["geometryType"] = page.GeometryType.ToString(),
            ["spatialReference"] = ToSpatialReference(page.SpatialReference),
            ["fields"] = page.Fields.Select(ToField).ToArray(),
            ["features"] = page.Features.Select(ToFeature).ToArray(),
            ["isLastPage"] = page.IsLastPage,
        };

        return JsonSerializer.SerializeToDocument(payload);
    }

    public static JsonDocument ToJsonDocument(GrpcModels.ApplyEditsResponse response)
    {
        var payload = new Dictionary<string, object?>
        {
            ["addResults"] = response.AddResults.Select(ToEditResult).ToArray(),
            ["updateResults"] = response.UpdateResults.Select(ToEditResult).ToArray(),
            ["deleteResults"] = response.DeleteResults.Select(ToEditResult).ToArray(),
            ["error"] = response.Error is null ? null : ToEditError(response.Error),
        };

        return JsonSerializer.SerializeToDocument(payload);
    }

    private static IReadOnlyList<GrpcModels.Feature>? ToGrpcFeatures(
        IReadOnlyList<FeatureServerFeature>? features,
        string? json)
    {
        if (features is { Count: > 0 })
        {
            return features.Select(ToGrpcFeature).ToArray();
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            return document.RootElement.EnumerateArray().Select(ToGrpcFeature).ToArray();
        }

        return document.RootElement.ValueKind == JsonValueKind.Object
            ? [ToGrpcFeature(document.RootElement)]
            : [];
    }

    private static IReadOnlyList<long>? ToGrpcDeleteObjectIds(IReadOnlyList<long>? deletes, string? deletesCsv)
    {
        if (deletes is { Count: > 0 })
        {
            return deletes;
        }

        if (string.IsNullOrWhiteSpace(deletesCsv))
        {
            return null;
        }

        var objectIds = new List<long>();
        foreach (var token in deletesCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId))
            {
                objectIds.Add(objectId);
            }
        }

        return objectIds;
    }

    private static GrpcModels.Feature ToGrpcFeature(FeatureServerFeature feature)
        => new()
        {
            Id = TryReadObjectId(feature.Attributes, out var objectId) ? objectId : 0,
            Attributes = ToObjectDictionary(feature.Attributes),
            Geometry = feature.Geometry is { ValueKind: JsonValueKind.Object } geometry
                ? ToObjectDictionary(geometry)
                : null,
        };

    private static GrpcModels.Feature ToGrpcFeature(JsonElement feature)
    {
        var attributes = feature.TryGetProperty("attributes", out var attributesNode) &&
            attributesNode.ValueKind == JsonValueKind.Object
                ? ToObjectDictionary(attributesNode)
                : new Dictionary<string, object?>();

        return new GrpcModels.Feature
        {
            Id = TryReadObjectId(feature, attributes, out var objectId) ? objectId : 0,
            Attributes = attributes,
            Geometry = feature.TryGetProperty("geometry", out var geometryNode) &&
                geometryNode.ValueKind == JsonValueKind.Object
                    ? ToObjectDictionary(geometryNode)
                    : null,
        };
    }

    private static bool TryReadObjectId(
        JsonElement feature,
        IReadOnlyDictionary<string, object?> attributes,
        out long objectId)
    {
        if (TryReadObjectId(feature, "id", out objectId) ||
            TryReadObjectId(feature, "objectId", out objectId))
        {
            return true;
        }

        foreach (var key in new[] { "OBJECTID", "objectid", "ObjectID", "FID" })
        {
            if (attributes.TryGetValue(key, out var value) && TryConvertToInt64(value, out objectId))
            {
                return true;
            }
        }

        objectId = 0;
        return false;
    }

    private static bool TryReadObjectId(Dictionary<string, JsonElement>? attributes, out long objectId)
    {
        if (attributes is not null)
        {
            foreach (var key in new[] { "OBJECTID", "objectid", "ObjectID", "FID" })
            {
                if (attributes.TryGetValue(key, out var value) && value.TryGetInt64(out objectId))
                {
                    return true;
                }
            }
        }

        objectId = 0;
        return false;
    }

    private static bool TryReadObjectId(JsonElement feature, string propertyName, out long objectId)
    {
        if (feature.TryGetProperty(propertyName, out var node))
        {
            if (node.TryGetInt64(out objectId))
            {
                return true;
            }

            if (node.ValueKind == JsonValueKind.String &&
                long.TryParse(node.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId))
            {
                return true;
            }
        }

        objectId = 0;
        return false;
    }

    private static Dictionary<string, object?> ToObjectDictionary(Dictionary<string, JsonElement>? values)
        => values?.ToDictionary(kvp => kvp.Key, kvp => ToObject(kvp.Value)) ?? [];

    private static Dictionary<string, object?> ToObjectDictionary(JsonElement value)
        => value.EnumerateObject().ToDictionary(property => property.Name, property => ToObject(property.Value));

    private static object? ToObject(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var int64Value) => int64Value,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.Array => value.EnumerateArray().Select(ToObject).ToArray(),
            JsonValueKind.Object => ToObjectDictionary(value),
            _ => value.GetRawText(),
        };

    private static bool TryConvertToInt64(object? value, out long objectId)
    {
        switch (value)
        {
            case long longValue:
                objectId = longValue;
                return true;
            case int intValue:
                objectId = intValue;
                return true;
            case string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId):
                return true;
            case JsonElement element when element.TryGetInt64(out objectId):
                return true;
            default:
                objectId = 0;
                return false;
        }
    }

    private static Dictionary<string, object?> ToFeature(GrpcModels.Feature source)
        => new()
        {
            ["id"] = source.Id,
            ["attributes"] = source.Attributes,
            ["geometry"] = source.Geometry,
        };

    private static Dictionary<string, object?> ToField(GrpcModels.FieldDefinition source)
        => new()
        {
            ["name"] = source.Name,
            ["fieldType"] = source.FieldType.ToString(),
            ["length"] = source.Length,
            ["nullable"] = source.Nullable,
        };

    private static Dictionary<string, object?>? ToSpatialReference(GrpcModels.SpatialReference? source)
    {
        if (source is null ||
            source.Wkid == 0 && source.LatestWkid == 0 && string.IsNullOrWhiteSpace(source.Wkt))
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["wkid"] = source.Wkid,
            ["latestWkid"] = source.LatestWkid,
            ["wkt"] = source.Wkt,
        };
    }

    private static Dictionary<string, object?>? ToExtent(GrpcModels.Extent? source)
    {
        if (source is null)
        {
            return null;
        }

        return new Dictionary<string, object?>
        {
            ["xmin"] = source.Xmin,
            ["ymin"] = source.Ymin,
            ["xmax"] = source.Xmax,
            ["ymax"] = source.Ymax,
            ["spatialReference"] = ToSpatialReference(source.SpatialReference),
        };
    }

    private static Dictionary<string, object?> ToEditResult(GrpcModels.EditResult source)
        => new()
        {
            ["objectId"] = source.ObjectId,
            ["success"] = source.Success,
            ["error"] = source.Error is null ? null : ToEditError(source.Error),
        };

    private static Dictionary<string, object?> ToEditError(GrpcModels.EditError source)
        => new()
        {
            ["code"] = source.Code,
            ["message"] = source.Message,
        };
}
