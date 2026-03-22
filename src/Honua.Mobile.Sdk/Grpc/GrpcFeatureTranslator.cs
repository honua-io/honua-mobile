using System.Globalization;
using System.Text.Json;
using Honua.Mobile.Sdk.Models;
using Proto = Honua.Server.Features.Grpc.Proto;

namespace Honua.Mobile.Sdk.Grpc;

/// <summary>
/// Translates between SDK request/response models and gRPC protobuf types.
/// </summary>
public static class GrpcFeatureTranslator
{
    /// <summary>
    /// Converts a <see cref="QueryFeaturesRequest"/> to its protobuf equivalent.
    /// </summary>
    /// <param name="request">The SDK query request.</param>
    /// <returns>A protobuf <see cref="Proto.QueryFeaturesRequest"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    public static Proto.QueryFeaturesRequest ToProtoQueryRequest(QueryFeaturesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var proto = new Proto.QueryFeaturesRequest
        {
            ServiceId = request.ServiceId,
            LayerId = request.LayerId,
            Where = request.Where,
            ReturnGeometry = request.ReturnGeometry,
            ResultOffset = request.ResultOffset ?? 0,
            ResultRecordCount = request.ResultRecordCount ?? 0,
            OrderBy = request.OrderBy ?? string.Empty,
            ReturnDistinct = request.ReturnDistinct,
            ReturnCountOnly = request.ReturnCountOnly,
            ReturnIdsOnly = request.ReturnIdsOnly,
            ReturnExtentOnly = request.ReturnExtentOnly,
        };

        if (request.ObjectIds is { Count: > 0 })
        {
            proto.ObjectIds.AddRange(request.ObjectIds);
        }

        if (request.OutFields is { Count: > 0 })
        {
            proto.OutFields.AddRange(request.OutFields);
        }

        return proto;
    }

    /// <summary>
    /// Converts an <see cref="ApplyEditsRequest"/> to its protobuf equivalent,
    /// parsing JSON feature payloads and delete ID lists into protobuf messages.
    /// </summary>
    /// <param name="request">The SDK edit request.</param>
    /// <returns>A protobuf <see cref="Proto.ApplyEditsRequest"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    public static Proto.ApplyEditsRequest ToProtoApplyEditsRequest(ApplyEditsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var proto = new Proto.ApplyEditsRequest
        {
            ServiceId = request.ServiceId,
            LayerId = request.LayerId,
            RollbackOnFailure = request.RollbackOnFailure,
            ForceWrite = request.ForceWrite,
        };

        foreach (var feature in ParseFeaturePayload(request.AddsJson))
        {
            proto.Adds.Add(feature);
        }

        foreach (var feature in ParseFeaturePayload(request.UpdatesJson))
        {
            proto.Updates.Add(feature);
        }

        foreach (var objectId in ParseDeleteObjectIds(request.DeletesCsv))
        {
            proto.Deletes.Add(objectId);
        }

        return proto;
    }

    /// <summary>
    /// Converts a protobuf <see cref="Proto.QueryFeaturesResponse"/> to a <see cref="JsonDocument"/>
    /// matching the REST API JSON structure.
    /// </summary>
    /// <param name="response">The protobuf query response.</param>
    /// <returns>A <see cref="JsonDocument"/> with fields, features, spatial reference, and metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is <see langword="null"/>.</exception>
    public static JsonDocument ToJsonDocument(Proto.QueryFeaturesResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var payload = new Dictionary<string, object?>
        {
            ["objectIdFieldName"] = response.ObjectIdFieldName,
            ["geometryType"] = response.GeometryType.ToString(),
            ["spatialReference"] = ToSpatialReference(response.SpatialReference),
            ["fields"] = response.Fields.Select(field => new Dictionary<string, object?>
            {
                ["name"] = field.Name,
                ["fieldType"] = field.FieldType.ToString(),
                ["length"] = field.Length,
                ["nullable"] = field.Nullable,
            }).ToArray(),
            ["features"] = response.Features.Select(ToFeature).ToArray(),
            ["exceededTransferLimit"] = response.ExceededTransferLimit,
            ["count"] = response.Count,
            ["objectIds"] = response.ObjectIds.ToArray(),
            ["extent"] = response.Extent is null ? null : ToExtent(response.Extent),
        };

        return JsonSerializer.SerializeToDocument(payload);
    }

    /// <summary>
    /// Converts a protobuf <see cref="Proto.FeaturePage"/> (from a streaming response) to a <see cref="JsonDocument"/>.
    /// </summary>
    /// <param name="page">A single page from the gRPC streaming response.</param>
    /// <returns>A <see cref="JsonDocument"/> with features and pagination metadata.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page"/> is <see langword="null"/>.</exception>
    public static JsonDocument ToJsonDocument(Proto.FeaturePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var payload = new Dictionary<string, object?>
        {
            ["objectIdFieldName"] = page.ObjectIdFieldName,
            ["geometryType"] = page.GeometryType.ToString(),
            ["spatialReference"] = ToSpatialReference(page.SpatialReference),
            ["fields"] = page.Fields.Select(field => new Dictionary<string, object?>
            {
                ["name"] = field.Name,
                ["fieldType"] = field.FieldType.ToString(),
                ["length"] = field.Length,
                ["nullable"] = field.Nullable,
            }).ToArray(),
            ["features"] = page.Features.Select(ToFeature).ToArray(),
            ["isLastPage"] = page.IsLastPage,
        };

        return JsonSerializer.SerializeToDocument(payload);
    }

    /// <summary>
    /// Converts a protobuf <see cref="Proto.ApplyEditsResponse"/> to a <see cref="JsonDocument"/>
    /// containing add/update/delete result arrays.
    /// </summary>
    /// <param name="response">The protobuf edit response.</param>
    /// <returns>A <see cref="JsonDocument"/> with per-operation results and any top-level error.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is <see langword="null"/>.</exception>
    public static JsonDocument ToJsonDocument(Proto.ApplyEditsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var payload = new Dictionary<string, object?>
        {
            ["addResults"] = response.AddResults.Select(ToEditResult).ToArray(),
            ["updateResults"] = response.UpdateResults.Select(ToEditResult).ToArray(),
            ["deleteResults"] = response.DeleteResults.Select(ToEditResult).ToArray(),
            ["error"] = response.Error is null ? null : ToEditError(response.Error),
        };

        return JsonSerializer.SerializeToDocument(payload);
    }

    private static IEnumerable<Proto.Feature> ParseFeaturePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in document.RootElement.EnumerateArray())
            {
                yield return ToFeature(item);
            }

            yield break;
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            yield return ToFeature(document.RootElement);
        }
    }

    private static IEnumerable<long> ParseDeleteObjectIds(string? deletesCsv)
    {
        if (string.IsNullOrWhiteSpace(deletesCsv))
        {
            return [];
        }

        var result = new List<long>();
        foreach (var token in deletesCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var objectId))
            {
                result.Add(objectId);
            }
        }

        return result;
    }

    private static Proto.Feature ToFeature(JsonElement source)
    {
        var feature = new Proto.Feature();

        if (source.TryGetProperty("id", out var idNode) && idNode.TryGetInt64(out var id))
        {
            feature.Id = id;
        }
        else if (source.TryGetProperty("objectId", out var objectIdNode) && objectIdNode.TryGetInt64(out var objectId))
        {
            feature.Id = objectId;
        }

        if (source.TryGetProperty("attributes", out var attributesNode) && attributesNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in attributesNode.EnumerateObject())
            {
                feature.Attributes[property.Name] = ToAttributeValue(property.Value);
            }
        }

        if (source.TryGetProperty("geometry", out var geometryNode) && geometryNode.ValueKind == JsonValueKind.Object)
        {
            var geometry = ToGeometry(geometryNode);
            if (geometry is not null)
            {
                feature.Geometry = geometry;
            }
        }

        return feature;
    }

    private static Proto.AttributeValue ToAttributeValue(JsonElement source)
    {
        var value = new Proto.AttributeValue();

        switch (source.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                value.NullValue = Proto.NullValue.NullValue;
                return value;
            case JsonValueKind.String:
                value.StringValue = source.GetString() ?? string.Empty;
                return value;
            case JsonValueKind.True:
            case JsonValueKind.False:
                value.BoolValue = source.GetBoolean();
                return value;
            case JsonValueKind.Number:
                if (source.TryGetInt64(out var i64))
                {
                    value.Int64Value = i64;
                    return value;
                }

                value.DoubleValue = source.GetDouble();
                return value;
            default:
                value.StringValue = source.GetRawText();
                return value;
        }
    }

    private static Dictionary<string, object?> ToFeature(Proto.Feature source)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in source.Attributes)
        {
            attributes[pair.Key] = ToAttribute(pair.Value);
        }

        return new Dictionary<string, object?>
        {
            ["id"] = source.Id,
            ["attributes"] = attributes,
            ["geometry"] = source.Geometry is null ? null : ToGeometry(source.Geometry),
        };
    }

    private static object? ToAttribute(Proto.AttributeValue value)
    {
        return value.ValueCase switch
        {
            Proto.AttributeValue.ValueOneofCase.StringValue => value.StringValue,
            Proto.AttributeValue.ValueOneofCase.Int32Value => value.Int32Value,
            Proto.AttributeValue.ValueOneofCase.Int64Value => value.Int64Value,
            Proto.AttributeValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            Proto.AttributeValue.ValueOneofCase.FloatValue => value.FloatValue,
            Proto.AttributeValue.ValueOneofCase.BoolValue => value.BoolValue,
            Proto.AttributeValue.ValueOneofCase.DatetimeValue => value.DatetimeValue,
            Proto.AttributeValue.ValueOneofCase.BytesValue => Convert.ToBase64String(value.BytesValue.ToByteArray()),
            _ => null,
        };
    }

    private static Proto.Geometry? ToGeometry(JsonElement source)
    {
        var hasZ = TryReadBoolean(source, "hasZ");
        var hasM = TryReadBoolean(source, "hasM");

        if (source.TryGetProperty("x", out var xNode) &&
            source.TryGetProperty("y", out var yNode) &&
            xNode.TryGetDouble(out var x) &&
            yNode.TryGetDouble(out var y))
        {
            var point = new Proto.PointGeometry
            {
                X = x,
                Y = y,
            };

            if (source.TryGetProperty("z", out var zNode) && zNode.TryGetDouble(out var z))
            {
                point.Z = z;
            }

            if (source.TryGetProperty("m", out var mNode) && mNode.TryGetDouble(out var m))
            {
                point.M = m;
            }

            return new Proto.Geometry
            {
                Point = point,
            };
        }

        if (source.TryGetProperty("points", out var pointsNode) && pointsNode.ValueKind == JsonValueKind.Array)
        {
            var multipoint = new Proto.MultiPointGeometry();
            foreach (var pointNode in pointsNode.EnumerateArray())
            {
                if (TryReadPointFromArray(pointNode, hasZ, hasM, out var point))
                {
                    multipoint.Points.Add(point);
                }
            }

            return new Proto.Geometry { MultiPoint = multipoint };
        }

        if (source.TryGetProperty("paths", out var pathsNode) && pathsNode.ValueKind == JsonValueKind.Array)
        {
            var polyline = new Proto.PolylineGeometry();
            foreach (var pathNode in pathsNode.EnumerateArray())
            {
                var sequence = ToCoordinateSequence(pathNode, hasZ, hasM);
                if (sequence is not null)
                {
                    polyline.Paths.Add(sequence);
                }
            }

            return new Proto.Geometry { Polyline = polyline };
        }

        if (source.TryGetProperty("rings", out var ringsNode) && ringsNode.ValueKind == JsonValueKind.Array)
        {
            var polygon = new Proto.PolygonGeometry();
            foreach (var ringNode in ringsNode.EnumerateArray())
            {
                var sequence = ToCoordinateSequence(ringNode, hasZ, hasM);
                if (sequence is not null)
                {
                    polygon.Rings.Add(sequence);
                }
            }

            return new Proto.Geometry { Polygon = polygon };
        }

        return null;
    }

    private static object? ToGeometry(Proto.Geometry source)
    {
        return source.ShapeCase switch
        {
            Proto.Geometry.ShapeOneofCase.Point => ToPointDictionary(source.Point),
            Proto.Geometry.ShapeOneofCase.MultiPoint => new Dictionary<string, object?>
            {
                ["points"] = source.MultiPoint.Points.Select(point => new object?[] { point.X, point.Y, point.Z, point.M }).ToArray(),
            },
            Proto.Geometry.ShapeOneofCase.Polyline => new Dictionary<string, object?>
            {
                ["paths"] = source.Polyline.Paths.Select(ToCoordinateArray).ToArray(),
            },
            Proto.Geometry.ShapeOneofCase.Polygon => new Dictionary<string, object?>
            {
                ["rings"] = source.Polygon.Rings.Select(ToCoordinateArray).ToArray(),
            },
            Proto.Geometry.ShapeOneofCase.MultiPolygon => new Dictionary<string, object?>
            {
                ["polygons"] = source.MultiPolygon.Polygons
                    .Select(polygon => polygon.Rings.Select(ToCoordinateArray).ToArray())
                    .ToArray(),
            },
            _ => null,
        };
    }

    private static Dictionary<string, object?> ToPointDictionary(Proto.PointGeometry point)
    {
        var dict = new Dictionary<string, object?>
        {
            ["x"] = point.X,
            ["y"] = point.Y,
        };

        bool hasZ = point.Z != 0;
        bool hasM = point.M != 0;

        if (hasZ)
        {
            dict["z"] = point.Z;
        }

        if (hasM)
        {
            dict["m"] = point.M;
        }

        return dict;
    }

    private static object?[] ToCoordinateArray(Proto.CoordinateSequence sequence)
    {
        return sequence.Coords.Select(coord => new object?[] { coord.X, coord.Y, coord.Z, coord.M }).Cast<object?>().ToArray();
    }

    private static Proto.CoordinateSequence? ToCoordinateSequence(JsonElement source, bool hasZ, bool hasM)
    {
        if (source.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sequence = new Proto.CoordinateSequence();
        foreach (var node in source.EnumerateArray())
        {
            if (!TryReadCoordinate(node, hasZ, hasM, out var coord))
            {
                continue;
            }

            sequence.Coords.Add(coord);
        }

        return sequence;
    }

    private static bool TryReadPointFromArray(JsonElement source, bool hasZ, bool hasM, out Proto.PointGeometry point)
    {
        point = new Proto.PointGeometry();

        if (source.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        using var enumerator = source.EnumerateArray();
        if (!enumerator.MoveNext() || !enumerator.Current.TryGetDouble(out var x))
        {
            return false;
        }

        if (!enumerator.MoveNext() || !enumerator.Current.TryGetDouble(out var y))
        {
            return false;
        }

        point.X = x;
        point.Y = y;

        if (enumerator.MoveNext() && enumerator.Current.TryGetDouble(out var third))
        {
            if (hasM && !hasZ)
            {
                point.M = third;
            }
            else
            {
                point.Z = third;
            }

            if (hasM && hasZ && enumerator.MoveNext() && enumerator.Current.TryGetDouble(out var m))
            {
                point.M = m;
            }
        }

        return true;
    }

    private static bool TryReadCoordinate(JsonElement source, bool hasZ, bool hasM, out Proto.Coordinate coordinate)
    {
        coordinate = new Proto.Coordinate();

        if (source.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        using var enumerator = source.EnumerateArray();
        if (!enumerator.MoveNext() || !enumerator.Current.TryGetDouble(out var x))
        {
            return false;
        }

        if (!enumerator.MoveNext() || !enumerator.Current.TryGetDouble(out var y))
        {
            return false;
        }

        coordinate.X = x;
        coordinate.Y = y;

        if (enumerator.MoveNext() && enumerator.Current.TryGetDouble(out var third))
        {
            if (hasM && !hasZ)
            {
                coordinate.M = third;
            }
            else
            {
                coordinate.Z = third;
            }

            if (hasM && hasZ && enumerator.MoveNext() && enumerator.Current.TryGetDouble(out var m))
            {
                coordinate.M = m;
            }
        }

        return true;
    }

    private static bool TryReadBoolean(JsonElement source, string propertyName)
    {
        if (!source.TryGetProperty(propertyName, out var node))
        {
            return false;
        }

        return node.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => false,
        };
    }

    private static Dictionary<string, object?>? ToSpatialReference(Proto.SpatialReference? source)
    {
        if (source is null)
        {
            return null;
        }

        if (source.Wkid == 0 && source.LatestWkid == 0 && string.IsNullOrWhiteSpace(source.Wkt))
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

    private static Dictionary<string, object?>? ToExtent(Proto.Extent? source)
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

    private static Dictionary<string, object?> ToEditResult(Proto.EditResult source)
    {
        return new Dictionary<string, object?>
        {
            ["objectId"] = source.ObjectId,
            ["success"] = source.Success,
            ["error"] = source.Error is null ? null : ToEditError(source.Error),
        };
    }

    private static Dictionary<string, object?> ToEditError(Proto.EditError source)
    {
        return new Dictionary<string, object?>
        {
            ["code"] = source.Code,
            ["message"] = source.Message,
        };
    }
}
