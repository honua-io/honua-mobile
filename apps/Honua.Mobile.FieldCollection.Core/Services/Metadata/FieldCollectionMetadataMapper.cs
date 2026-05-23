using System.Globalization;
using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.GeoServices.FeatureServer.Models;

namespace Honua.Mobile.FieldCollection.Services.Metadata;

internal static class FieldCollectionMetadataMapper
{
    public static LayerInfo ToLayerInfo(string serviceId, FeatureServerLayerInfo layer)
    {
        var schema = (layer.Fields ?? [])
            .Where(IsFieldCollectionField)
            .Select(ToFormField)
            .ToList();

        var layerInfo = new LayerInfo
        {
            Id = layer.Id,
            ServiceId = serviceId,
            SourceId = BuildSourceId(serviceId, layer.Id),
            Name = string.IsNullOrWhiteSpace(layer.Name) ? $"Layer {layer.Id}" : layer.Name,
            Description = layer.Description ?? string.Empty,
            GeometryType = MapGeometryType(layer.GeometryType),
            IsVisible = true,
            IsEditable = AllowsEdits(layer.Capabilities),
            Schema = schema
        };

        layerInfo.Form = CreateFormDefinition(layerInfo);
        return layerInfo;
    }

    public static FormDefinition CreateFormDefinition(LayerInfo layer)
    {
        var formId = !string.IsNullOrWhiteSpace(layer.SourceId)
            ? $"{layer.SourceId}:form"
            : $"layer-{layer.Id}:form";

        return new FormDefinition
        {
            FormId = formId,
            Name = string.IsNullOrWhiteSpace(layer.Name) ? $"Layer {layer.Id}" : layer.Name,
            Description = layer.Description,
            Target = new FormTarget
            {
                SourceId = layer.SourceId,
                ServiceId = layer.ServiceId,
                LayerId = layer.Id
            },
            Sections =
            [
                new FormSection
                {
                    SectionId = "attributes",
                    Label = "Attributes",
                    Fields = layer.Schema
                }
            ],
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["geometryType"] = layer.GeometryType.ToString(),
                ["editable"] = layer.IsEditable.ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    public static FeatureSpatialGeometryType MapGeometryType(string? geometryType)
    {
        return geometryType?.Trim().ToLowerInvariant() switch
        {
            "esrigeometrypoint" or "point" => FeatureSpatialGeometryType.Point,
            "esrigeometrymultipoint" or "multipoint" => FeatureSpatialGeometryType.MultiPoint,
            "esrigeometrypolyline" or "polyline" or "linestring" or "multilinestring" => FeatureSpatialGeometryType.Polyline,
            "esrigeometrypolygon" or "polygon" or "multipolygon" => FeatureSpatialGeometryType.Polygon,
            "esrigeometryenvelope" or "envelope" => FeatureSpatialGeometryType.Envelope,
            _ => FeatureSpatialGeometryType.Unspecified
        };
    }

    public static string BuildSourceId(string serviceId, int layerId)
    {
        return $"{serviceId}/FeatureServer/{layerId}";
    }

    private static FormField ToFormField(FeatureServerField field)
    {
        var choices = ReadChoices(field.Domain);
        var type = choices.Count > 0 ? FormFieldType.SingleChoice : MapFieldType(field.Type);

        return new FormField
        {
            FieldId = field.Name,
            SourceFieldName = field.Name,
            Label = string.IsNullOrWhiteSpace(field.Alias) ? field.Name : field.Alias,
            Type = type,
            Required = !field.Nullable && field.DefaultValue is null,
            Choices = choices,
            Validation = new FieldValidationRule
            {
                MaxLength = type == FormFieldType.Text ? field.Length : null
            }
        };
    }

    private static bool IsFieldCollectionField(FeatureServerField field)
    {
        if (string.IsNullOrWhiteSpace(field.Name))
        {
            return false;
        }

        var type = field.Type.Trim();
        if (type.Contains("OID", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("GlobalID", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("Geometry", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(field.Name, "objectid", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(field.Name, "globalid", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(field.Name, "shape", StringComparison.OrdinalIgnoreCase);
    }

    private static FormFieldType MapFieldType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "esrifieldtypesmallinteger" or "esrifieldtypeinteger" or "esrifieldtypesingle" or "esrifieldtypedouble" => FormFieldType.Numeric,
            "esrifieldtypedate" => FormFieldType.DateTime,
            "esrifieldtypeblob" => FormFieldType.File,
            "esrifieldtypeguid" => FormFieldType.Text,
            _ => FormFieldType.Text
        };
    }

    private static bool AllowsEdits(string? capabilities)
    {
        if (string.IsNullOrWhiteSpace(capabilities))
        {
            return false;
        }

        return capabilities
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(capability =>
                capability.Equals("Create", StringComparison.OrdinalIgnoreCase) ||
                capability.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                capability.Equals("Delete", StringComparison.OrdinalIgnoreCase) ||
                capability.Equals("Editing", StringComparison.OrdinalIgnoreCase) ||
                capability.Equals("Uploads", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<FieldChoice> ReadChoices(JsonElement? domain)
    {
        if (domain is null ||
            domain.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            !domain.Value.TryGetProperty("codedValues", out var codedValues) ||
            codedValues.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var choices = new List<FieldChoice>();
        foreach (var codedValue in codedValues.EnumerateArray())
        {
            if (!codedValue.TryGetProperty("code", out var code))
            {
                continue;
            }

            var value = JsonScalarToString(code);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var label = codedValue.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : value;

            choices.Add(new FieldChoice { Value = value, Label = label });
        }

        return choices;
    }

    private static string? JsonScalarToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
}
