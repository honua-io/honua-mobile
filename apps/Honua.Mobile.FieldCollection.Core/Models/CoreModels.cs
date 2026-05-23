using Microsoft.Maui.Devices.Sensors;
using System.Globalization;
using System.Text.Json;
using Honua.Mobile.FieldCollection.Services.Ai;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Models;

// Core domain models for the field collection app

public class Feature
{
    public string Id { get; set; } = string.Empty;
    public int LayerId { get; set; }
    public Geometry? Geometry { get; set; }
    public Dictionary<string, object?> Attributes { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public long Version { get; set; } = 1;
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsPendingSync { get; set; }
    public List<AttachmentInfo> Attachments { get; set; } = new();

    public string DisplayTitle
    {
        get
        {
            if (Attributes.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name?.ToString()))
            {
                return name.ToString()!;
            }

            if (Attributes.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title?.ToString()))
            {
                return title.ToString()!;
            }

            return string.IsNullOrWhiteSpace(Id) ? "Untitled feature" : Id;
        }
    }

    public string AttributeSummary
    {
        get
        {
            if (Attributes.Count == 0)
            {
                return "No attributes";
            }

            return string.Join(", ", Attributes
                .Where(attribute => attribute.Value is not null)
                .Take(3)
                .Select(attribute => $"{attribute.Key}: {attribute.Value}"));
        }
    }

    public bool HasAttachments => Attachments.Count > 0;

    public int AttachmentsCount => Attachments.Count;

    public FeatureRecord ToSdkFeatureRecord()
    {
        return new FeatureRecord
        {
            Id = Id,
            Attributes = Attributes.ToDictionary(
                attribute => attribute.Key,
                attribute => JsonSerializer.SerializeToElement(attribute.Value)),
            Geometry = Geometry == null ? null : GeometryJson.ToJsonElement(Geometry)
        };
    }

    public static Feature FromSdkFeatureRecord(FeatureRecord record, int layerId)
    {
        return new Feature
        {
            Id = record.Id ?? string.Empty,
            LayerId = layerId,
            Geometry = record.Geometry.HasValue ? GeometryJson.FromJsonElement(record.Geometry.Value) : null,
            Attributes = record.Attributes.ToDictionary(
                attribute => attribute.Key,
                attribute => JsonValueToObject(attribute.Value)),
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Version = 1
        };
    }

    private static object? JsonValueToObject(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.Clone()
        };
    }
}

public abstract class Geometry
{
    public abstract string Type { get; }
    public int SRID { get; set; } = 4326;
}

public class Point : Geometry
{
    public override string Type => "Point";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? Altitude { get; set; }

    public Point() { }

    public Point(double latitude, double longitude, double? altitude = null)
    {
        Latitude = latitude;
        Longitude = longitude;
        Altitude = altitude;
    }
}

public class LineString : Geometry
{
    public override string Type => "LineString";
    public List<Point> Coordinates { get; set; } = new();
}

public class Polygon : Geometry
{
    public override string Type => "Polygon";
    public List<List<Point>> Coordinates { get; set; } = new();
}

public class FormData
{
    public int LayerId { get; set; }
    public string? FeatureId { get; set; }
    public Dictionary<string, object?> Values { get; set; } = new();
    public List<FieldMediaAttachment> Media { get; set; } = new();
    public FieldGeoPoint? Location { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();
    public bool IsValid => ValidationErrors.Count == 0;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public FieldRecord ToSdkFieldRecord(FormDefinition? definition = null)
    {
        return new FieldRecord
        {
            RecordId = FeatureId ?? string.Empty,
            FormId = definition?.FormId ?? LayerId.ToString(CultureInfo.InvariantCulture),
            Values = new Dictionary<string, object?>(Values),
            Media = new System.Collections.ObjectModel.Collection<FieldMediaAttachment>(Media),
            Location = Location,
            CreatedAtUtc = ToDateTimeOffset(CreatedAt),
        };
    }

    public static FormData FromSdkFieldRecord(FieldRecord record, int layerId)
    {
        return new FormData
        {
            LayerId = layerId,
            FeatureId = record.RecordId,
            Values = new Dictionary<string, object?>(record.Values),
            Media = record.Media.ToList(),
            Location = record.Location,
            CreatedAt = record.CreatedAtUtc.UtcDateTime,
            UpdatedAt = record.SubmittedAtUtc?.UtcDateTime ?? record.CompletedAtUtc?.UtcDateTime
        };
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        if (value == default)
        {
            return DateTimeOffset.UtcNow;
        }

        return value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : value.ToUniversalTime();
    }
}

public class AttachmentInfo
{
    public string Id { get; set; } = string.Empty;
    public int LayerId { get; set; }
    public string FeatureId { get; set; } = string.Empty;
    public long? RemoteAttachmentId { get; set; }
    public string? RemoteGlobalId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public AttachmentPayloadKind PayloadKind { get; set; } = AttachmentPayloadKind.File;
    public long SizeBytes { get; set; }
    public string? LocalPath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime UploadedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? Description { get; set; }
    public FieldLocationCaptureEvidence? CaptureLocation { get; set; }
    public string? ThumbnailUrl { get; set; }
    public MobileAiMediaState? AiMediaState { get; set; }
    public AttachmentSyncStatus SyncStatus { get; set; } = AttachmentSyncStatus.Synced;
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public string StatusSummary
    {
        get
        {
            var aiSummary = AiMediaState?.Summary;
            return string.IsNullOrWhiteSpace(aiSummary)
                ? SyncStatus.ToString()
                : $"{SyncStatus} - {aiSummary}";
        }
    }
}

public enum AttachmentSyncStatus
{
    Synced,
    PendingUpload,
    Uploading,
    UploadFailed,
    PendingDownload,
    Downloading,
    DownloadFailed,
    PendingDelete,
    Deleting,
    DeleteFailed
}

public enum AttachmentPayloadKind
{
    File,
    Photo,
    Signature
}

public class LayerInfo
{
    public int Id { get; set; }
    public string? ServiceId { get; set; }
    public string? SourceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public GeometryType GeometryType { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsEditable { get; set; } = true;
    public FormDefinition? Form { get; set; }
    public List<FieldDefinition> Schema { get; set; } = new();
    public LayerStyle Style { get; set; } = new();
}

public class FieldProjectInfo
{
    public string ProjectId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string? PackageId { get; set; }
    public string? Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int LayerCount { get; set; }
    public bool IsAvailableOffline { get; set; }
    public FieldProjectCatalogState CatalogState { get; set; } = FieldProjectCatalogState.RemoteOnly;
    public FieldProjectValidationStatus ValidationStatus { get; set; } = FieldProjectValidationStatus.Unknown;
    public int ValidationIssueCount { get; set; }
    public long PackageSizeBytes { get; set; }
    public long MediaSizeBytes { get; set; }
    public string? LocalStoragePath { get; set; }
    public string? ManifestPath { get; set; }
    public string? ImportSource { get; set; }
    public string? PackageDigest { get; set; }
    public DateTime ImportedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastOpenedAtUtc { get; set; }
    public DateTime? LastValidationAtUtc { get; set; }
    public DateTime? LastSimulationRunAtUtc { get; set; }
    public DateTime? LastExportAtUtc { get; set; }
    public List<LayerInfo> Layers { get; set; } = new();
}

public class FieldProjectCatalogEntry
{
    public string ProjectId { get; set; } = string.Empty;
    public string ServiceId { get; set; } = string.Empty;
    public string? PackageId { get; set; }
    public string? Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FieldProjectCatalogState State { get; set; } = FieldProjectCatalogState.Installed;
    public FieldProjectValidationStatus ValidationStatus { get; set; } = FieldProjectValidationStatus.Unknown;
    public int ValidationIssueCount { get; set; }
    public int LayerCount { get; set; }
    public long PackageSizeBytes { get; set; }
    public long MediaSizeBytes { get; set; }
    public string? LocalStoragePath { get; set; }
    public string? ManifestPath { get; set; }
    public string? ImportSource { get; set; }
    public string? PackageDigest { get; set; }
    public DateTime ImportedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastOpenedAtUtc { get; set; }
    public DateTime? LastValidationAtUtc { get; set; }
    public DateTime? LastSimulationRunAtUtc { get; set; }
    public DateTime? LastExportAtUtc { get; set; }
}

public enum FieldProjectCatalogState
{
    RemoteOnly,
    Installed,
    Stale,
    Invalid,
    Archived,
    Removable
}

public enum FieldProjectValidationStatus
{
    Unknown,
    Valid,
    Warning,
    Error
}

public class LayerStyle
{
    public string FillColor { get; set; } = "#3388ff";
    public string StrokeColor { get; set; } = "#000000";
    public double StrokeWidth { get; set; } = 1.0;
    public double Opacity { get; set; } = 0.8;
    public string? MarkerSymbol { get; set; }
    public double MarkerSize { get; set; } = 10.0;
}

public class SyncStatistics
{
    public DateTime? LastSyncTime { get; set; }
    public int FeaturesPulled { get; set; }
    public int FeaturesPushed { get; set; }
    public int AttachmentsDownloaded { get; set; }
    public int AttachmentsUploaded { get; set; }
    public int ConflictsDetected { get; set; }
    public int ConflictsResolved { get; set; }
    public TimeSpan LastSyncDuration { get; set; }
    public long BytesTransferred { get; set; }
}

public class DeviceInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public Location? LastKnownLocation { get; set; }
    public DateTime LastActiveAt { get; set; }
}

public class UserSession
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ServerUrl { get; set; } = string.Empty;
    public DateTime LoginTime { get; set; }
    public DateTime LastActivityTime { get; set; }
    public string? ApiKey { get; set; }
    public Dictionary<string, object> Preferences { get; set; } = new();
}

public static class GeometryJson
{
    public static JsonElement ToJsonElement(Geometry geometry)
    {
        var payload = ToGeoJson(geometry);
        return JsonSerializer.SerializeToElement(payload);
    }

    public static Geometry? FromJsonElement(JsonElement geometry)
    {
        if (geometry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryReadGeoJson(geometry, out var geoJson)
            ? geoJson
            : ReadFeatureServerGeometry(geometry);
    }

    private static bool TryReadGeoJson(JsonElement geometry, out Geometry? parsedGeometry)
    {
        parsedGeometry = null;

        if (!geometry.TryGetProperty("type", out var typeProperty) ||
            typeProperty.GetString() is not { Length: > 0 } type ||
            !geometry.TryGetProperty("coordinates", out var coordinates))
        {
            return false;
        }

        var srid = ReadSrid(geometry);
        parsedGeometry = type switch
        {
            "Point" => ReadPoint(coordinates, srid),
            "LineString" => new LineString
            {
                SRID = srid,
                Coordinates = ReadPointArray(coordinates, srid)
            },
            "Polygon" => new Polygon
            {
                SRID = srid,
                Coordinates = ReadPointRings(coordinates, srid)
            },
            _ => null
        };

        return true;
    }

    private static Geometry? ReadFeatureServerGeometry(JsonElement geometry)
    {
        var srid = ReadSrid(geometry);
        if (TryGetDouble(geometry, "x", out var x) &&
            TryGetDouble(geometry, "y", out var y))
        {
            return new Point(y, x, TryGetDouble(geometry, "z", out var z) ? z : null)
            {
                SRID = srid
            };
        }

        if (geometry.TryGetProperty("paths", out var paths))
        {
            var coordinates = ReadFirstPointArray(paths, srid);
            return coordinates.Count > 0
                ? new LineString { SRID = srid, Coordinates = coordinates }
                : null;
        }

        if (geometry.TryGetProperty("rings", out var rings))
        {
            var coordinates = ReadPointRings(rings, srid);
            return coordinates.Count > 0
                ? new Polygon { SRID = srid, Coordinates = coordinates }
                : null;
        }

        return null;
    }

    private static List<Point> ReadFirstPointArray(JsonElement paths, int srid)
    {
        if (paths.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return paths.EnumerateArray()
            .Where(path => path.ValueKind == JsonValueKind.Array)
            .Select(path => ReadPointArray(path, srid))
            .FirstOrDefault(points => points.Count > 0) ?? [];
    }

    private static List<List<Point>> ReadPointRings(JsonElement rings, int srid)
    {
        if (rings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return rings.EnumerateArray()
            .Where(ring => ring.ValueKind == JsonValueKind.Array)
            .Select(ring => ReadPointArray(ring, srid))
            .Where(ring => ring.Count > 0)
            .ToList();
    }

    private static List<Point> ReadPointArray(JsonElement coordinates, int srid)
    {
        if (coordinates.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return coordinates.EnumerateArray()
            .Select(coordinate => ReadPoint(coordinate, srid))
            .Where(point => point != null)
            .Cast<Point>()
            .ToList();
    }

    private static int ReadSrid(JsonElement geometry)
    {
        if (TryGetInt(geometry, "srid", out var srid))
        {
            return srid;
        }

        if (geometry.TryGetProperty("spatialReference", out var spatialReference))
        {
            if (TryGetInt(spatialReference, "latestWkid", out var latestWkid))
            {
                return latestWkid;
            }

            if (TryGetInt(spatialReference, "wkid", out var wkid))
            {
                return wkid;
            }
        }

        return 4326;
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value);
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out value);
    }

    private static object ToGeoJson(Geometry geometry)
    {
        return geometry switch
        {
            Point point => new
            {
                type = "Point",
                coordinates = ToCoordinateArray(point)
            },
            LineString line => new
            {
                type = "LineString",
                coordinates = line.Coordinates.Select(ToCoordinateArray).ToArray()
            },
            Polygon polygon => new
            {
                type = "Polygon",
                coordinates = polygon.Coordinates
                    .Select(ring => ring.Select(ToCoordinateArray).ToArray())
                    .ToArray()
            },
            _ => throw new NotSupportedException($"Geometry type {geometry.Type} not supported")
        };
    }

    private static double[] ToCoordinateArray(Point point)
    {
        return point.Altitude.HasValue
            ? [point.Longitude, point.Latitude, point.Altitude.Value]
            : [point.Longitude, point.Latitude];
    }

    private static Point? ReadPoint(JsonElement coordinates, int srid)
    {
        if (coordinates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = coordinates.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Number)
            .Select(value => value.GetDouble())
            .ToArray();

        if (values.Length < 2)
        {
            return null;
        }

        return new Point(values[1], values[0], values.Length >= 3 ? values[2] : null)
        {
            SRID = srid
        };
    }
}
