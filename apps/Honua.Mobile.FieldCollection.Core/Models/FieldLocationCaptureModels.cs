using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Sdk.Field.Records;
using Microsoft.Maui.Devices.Sensors;

namespace Honua.Mobile.FieldCollection.Models;

public enum FieldLocationSourceKind
{
    Unknown,
    BuiltInGps,
    ExternalGnss,
    Network,
    Manual,
    Simulator,
}

public sealed record FieldLocationReceiverMetadata
{
    public string? Name { get; init; }

    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public string? FirmwareVersion { get; init; }

    public string? SerialNumber { get; init; }

    public bool IsExternal { get; init; }

    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record FieldLocationCaptureMetadata
{
    public FieldLocationSourceKind SourceKind { get; init; } = FieldLocationSourceKind.Unknown;

    public string? Provider { get; init; }

    public FieldLocationReceiverMetadata? Receiver { get; init; }

    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record FieldLocationCaptureEvidence
{
    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public double? AltitudeMeters { get; init; }

    public double? HorizontalAccuracyMeters { get; init; }

    public double? VerticalAccuracyMeters { get; init; }

    public double? SpeedMetersPerSecond { get; init; }

    public double? HeadingDegrees { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public FieldLocationSourceKind SourceKind { get; init; } = FieldLocationSourceKind.Unknown;

    public string? Provider { get; init; }

    public bool? IsMockProvider { get; init; }

    public bool? ReducedAccuracy { get; init; }

    public string? AltitudeReferenceSystem { get; init; }

    public FieldLocationReceiverMetadata? Receiver { get; init; }

    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public FieldGeoPoint ToFieldGeoPoint() => new(Latitude, Longitude, HorizontalAccuracyMeters);

    public Dictionary<string, object?> ToAttributes(string prefix)
    {
        var normalizedPrefix = FieldLocationMetadataMapper.SanitizeAttributeKey(prefix);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{normalizedPrefix}_source"] = SourceKind.ToString(),
            [$"{normalizedPrefix}_source_label"] = FieldLocationMetadataMapper.FormatSource(SourceKind),
            [$"{normalizedPrefix}_captured_at_utc"] = CapturedAtUtc.UtcDateTime,
            [$"{normalizedPrefix}_latitude"] = Latitude,
            [$"{normalizedPrefix}_longitude"] = Longitude,
        };

        AddIfPresent(values, $"{normalizedPrefix}_accuracy_m", HorizontalAccuracyMeters);
        AddIfPresent(values, $"{normalizedPrefix}_vertical_accuracy_m", VerticalAccuracyMeters);
        AddIfPresent(values, $"{normalizedPrefix}_altitude_m", AltitudeMeters);
        AddIfPresent(values, $"{normalizedPrefix}_speed_mps", SpeedMetersPerSecond);
        AddIfPresent(values, $"{normalizedPrefix}_heading_deg", HeadingDegrees);
        AddIfPresent(values, $"{normalizedPrefix}_provider", Provider);
        AddIfPresent(values, $"{normalizedPrefix}_mock_provider", IsMockProvider);
        AddIfPresent(values, $"{normalizedPrefix}_reduced_accuracy", ReducedAccuracy);
        AddIfPresent(values, $"{normalizedPrefix}_altitude_reference", AltitudeReferenceSystem);

        if (Receiver is not null)
        {
            AddIfPresent(values, $"{normalizedPrefix}_receiver_name", Receiver.Name);
            AddIfPresent(values, $"{normalizedPrefix}_receiver_manufacturer", Receiver.Manufacturer);
            AddIfPresent(values, $"{normalizedPrefix}_receiver_model", Receiver.Model);
            AddIfPresent(values, $"{normalizedPrefix}_receiver_firmware", Receiver.FirmwareVersion);
            AddIfPresent(values, $"{normalizedPrefix}_receiver_serial", Receiver.SerialNumber);
            values[$"{normalizedPrefix}_receiver_external"] = Receiver.IsExternal;

            if (Receiver.Properties.Count > 0)
            {
                values[$"{normalizedPrefix}_receiver_properties_json"] =
                    JsonSerializer.Serialize(Receiver.Properties, FieldLocationMetadataMapper.JsonOptions);
            }
        }

        if (Properties.Count > 0)
        {
            values[$"{normalizedPrefix}_properties_json"] =
                JsonSerializer.Serialize(Properties, FieldLocationMetadataMapper.JsonOptions);
        }

        return values;
    }

    private static void AddIfPresent(Dictionary<string, object?> values, string key, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        values[key] = value;
    }
}

public sealed record FieldLocationFix
{
    public required Location Location { get; init; }

    public FieldLocationSourceKind SourceKind { get; init; } = FieldLocationSourceKind.Unknown;

    public string? Provider { get; init; }

    public FieldLocationReceiverMetadata? Receiver { get; init; }

    public Dictionary<string, string> Properties { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset CapturedAtUtc => Location.Timestamp == default
        ? DateTimeOffset.UtcNow
        : Location.Timestamp.ToUniversalTime();

    public FieldLocationCaptureEvidence ToEvidence()
    {
        return new FieldLocationCaptureEvidence
        {
            Latitude = Location.Latitude,
            Longitude = Location.Longitude,
            AltitudeMeters = FieldLocationMetadataMapper.NormalizeFinite(Location.Altitude),
            HorizontalAccuracyMeters = FieldLocationMetadataMapper.NormalizeNonNegative(Location.Accuracy),
            VerticalAccuracyMeters = FieldLocationMetadataMapper.NormalizeNonNegative(Location.VerticalAccuracy),
            SpeedMetersPerSecond = FieldLocationMetadataMapper.NormalizeNonNegative(Location.Speed),
            HeadingDegrees = FieldLocationMetadataMapper.NormalizeHeading(Location.Course),
            CapturedAtUtc = CapturedAtUtc,
            SourceKind = SourceKind,
            Provider = Provider,
            IsMockProvider = Location.IsFromMockProvider,
            ReducedAccuracy = Location.ReducedAccuracy,
            AltitudeReferenceSystem = Location.AltitudeReferenceSystem.ToString(),
            Receiver = Receiver,
            Properties = new Dictionary<string, string>(Properties, StringComparer.OrdinalIgnoreCase)
        };
    }
}

public static class FieldLocationMetadataMapper
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static FieldLocationFix FromMauiLocation(
        Location location,
        FieldLocationCaptureMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(location);

        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata?.Properties is { Count: > 0 })
        {
            foreach (var property in metadata.Properties)
            {
                if (!string.IsNullOrWhiteSpace(property.Key) && !string.IsNullOrWhiteSpace(property.Value))
                {
                    properties[property.Key.Trim()] = property.Value.Trim();
                }
            }
        }

        return new FieldLocationFix
        {
            Location = location,
            SourceKind = ResolveSourceKind(location, metadata),
            Provider = NormalizeText(metadata?.Provider) ?? ReadProperty(properties, "provider"),
            Receiver = metadata?.Receiver,
            Properties = properties
        };
    }

    public static string FormatEvidence(FieldLocationCaptureEvidence? evidence)
    {
        if (evidence is null)
        {
            return "GPS not captured";
        }

        var parts = new List<string>
        {
            FormatSource(evidence.SourceKind)
        };

        if (evidence.HorizontalAccuracyMeters.HasValue)
        {
            parts.Add($"accuracy {evidence.HorizontalAccuracyMeters.Value:F1} m");
        }
        else
        {
            parts.Add("accuracy unknown");
        }

        if (evidence.VerticalAccuracyMeters.HasValue)
        {
            parts.Add($"vertical {evidence.VerticalAccuracyMeters.Value:F1} m");
        }

        if (!string.IsNullOrWhiteSpace(evidence.Provider))
        {
            parts.Add($"provider {evidence.Provider}");
        }

        if (evidence.Receiver?.IsExternal == true &&
            !string.IsNullOrWhiteSpace(evidence.Receiver.Name))
        {
            parts.Add($"receiver {evidence.Receiver.Name}");
        }

        parts.Add($"captured {evidence.CapturedAtUtc.UtcDateTime:u}");
        return string.Join("; ", parts);
    }

    public static string FormatSource(FieldLocationSourceKind sourceKind)
    {
        return sourceKind switch
        {
            FieldLocationSourceKind.BuiltInGps => "Built-in GPS",
            FieldLocationSourceKind.ExternalGnss => "External GNSS",
            FieldLocationSourceKind.Network => "Network location",
            FieldLocationSourceKind.Manual => "Manual location",
            FieldLocationSourceKind.Simulator => "Simulated location",
            _ => "Location source unknown"
        };
    }

    public static void AddAttributes(
        IDictionary<string, object?> values,
        string prefix,
        FieldLocationCaptureEvidence? evidence,
        bool overwrite = false)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (evidence is null)
        {
            return;
        }

        foreach (var attribute in evidence.ToAttributes(prefix))
        {
            if (overwrite || !values.ContainsKey(attribute.Key))
            {
                values[attribute.Key] = attribute.Value;
            }
        }
    }

    public static string BuildFieldEvidencePrefix(string valueKey)
    {
        return $"{SanitizeAttributeKey(valueKey)}_gps";
    }

    public static bool MatchesFieldLocation(
        FieldGeoPoint point,
        FieldLocationCaptureEvidence evidence,
        double coordinateTolerance = 0.0000001,
        double accuracyTolerance = 0.001)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(evidence);

        if (Math.Abs(point.Latitude - evidence.Latitude) > coordinateTolerance ||
            Math.Abs(point.Longitude - evidence.Longitude) > coordinateTolerance)
        {
            return false;
        }

        return NullableDoubleMatches(
            point.AccuracyMeters,
            evidence.HorizontalAccuracyMeters,
            accuracyTolerance);
    }

    public static string ToAttachmentKeywords(
        string? description,
        FieldLocationCaptureEvidence? evidence)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add(description.Trim());
        }

        if (evidence is null)
        {
            return string.Join("; ", parts);
        }

        parts.Add($"honua.location.source={evidence.SourceKind}");
        AddKeyword(parts, "honua.location.accuracy_m", evidence.HorizontalAccuracyMeters);
        AddKeyword(parts, "honua.location.vertical_accuracy_m", evidence.VerticalAccuracyMeters);
        AddKeyword(parts, "honua.location.captured_at_utc", evidence.CapturedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        AddKeyword(parts, "honua.location.provider", evidence.Provider);
        AddKeyword(parts, "honua.location.receiver", evidence.Receiver?.Name);
        AddKeyword(parts, "honua.location.receiver_model", evidence.Receiver?.Model);
        return string.Join("; ", parts);
    }

    public static string SerializeEvidence(FieldLocationCaptureEvidence? evidence)
    {
        return evidence is null ? string.Empty : JsonSerializer.Serialize(evidence, JsonOptions);
    }

    public static FieldLocationCaptureEvidence? DeserializeEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FieldLocationCaptureEvidence>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static double? NormalizeFinite(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value) ? value.Value : null;
    }

    internal static double? NormalizeNonNegative(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value) && value.Value >= 0
            ? value.Value
            : null;
    }

    internal static double? NormalizeHeading(double? value)
    {
        return value.HasValue && double.IsFinite(value.Value) && value.Value >= 0 && value.Value < 360
            ? value.Value
            : null;
    }

    internal static string SanitizeAttributeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "gps";
        }

        var builder = new StringBuilder(key.Length);
        foreach (var character in key.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        var sanitized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "gps" : sanitized;
    }

    private static bool NullableDoubleMatches(double? left, double? right, double tolerance)
    {
        if (!left.HasValue || !right.HasValue)
        {
            return left.HasValue == right.HasValue;
        }

        return Math.Abs(left.Value - right.Value) <= tolerance;
    }

    private static FieldLocationSourceKind ResolveSourceKind(
        Location location,
        FieldLocationCaptureMetadata? metadata)
    {
        if (metadata?.SourceKind is { } sourceKind && sourceKind != FieldLocationSourceKind.Unknown)
        {
            return sourceKind;
        }

        if (location.IsFromMockProvider)
        {
            return FieldLocationSourceKind.Simulator;
        }

        var provider = NormalizeText(metadata?.Provider) ?? ReadProperty(metadata?.Properties, "provider");
        if (provider is null)
        {
            return FieldLocationSourceKind.BuiltInGps;
        }

        if (ContainsAny(provider, "external", "gnss", "nmea", "bluetooth", "rtk"))
        {
            return FieldLocationSourceKind.ExternalGnss;
        }

        if (ContainsAny(provider, "network", "wifi", "cell"))
        {
            return FieldLocationSourceKind.Network;
        }

        if (ContainsAny(provider, "mock", "sim"))
        {
            return FieldLocationSourceKind.Simulator;
        }

        return FieldLocationSourceKind.BuiltInGps;
    }

    private static void AddKeyword(List<string> parts, string key, double? value)
    {
        if (value.HasValue)
        {
            parts.Add($"{key}={value.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
        }
    }

    private static void AddKeyword(List<string> parts, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{key}={value.Trim()}");
        }
    }

    private static string? ReadProperty(IReadOnlyDictionary<string, string>? properties, string key)
    {
        return properties is not null && properties.TryGetValue(key, out var value)
            ? NormalizeText(value)
            : null;
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
