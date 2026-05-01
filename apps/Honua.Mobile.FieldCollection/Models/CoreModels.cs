using Microsoft.Maui.Devices.Sensors;

namespace Honua.Mobile.FieldCollection.Models;

// Core domain models for the field collection app

public class Feature
{
    public string Id { get; set; } = string.Empty;
    public int LayerId { get; set; }
    public Geometry? Geometry { get; set; }
    public Dictionary<string, object> Attributes { get; set; } = new();
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

public class FormDefinition
{
    public int LayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FieldDefinition[] Fields { get; set; } = Array.Empty<FieldDefinition>();
    public string Version { get; set; } = "1.0";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class FieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // text, number, select, date, textarea, boolean, etc.
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Required { get; set; }
    public string? DefaultValue { get; set; }
    public string[]? Options { get; set; } // For select fields
    public double? Min { get; set; } // For number fields
    public double? Max { get; set; } // For number fields
    public int? MaxLength { get; set; } // For text fields
    public string? Pattern { get; set; } // Regex validation
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class FormData
{
    public int LayerId { get; set; }
    public string? FeatureId { get; set; }
    public Dictionary<string, object> Values { get; set; } = new();
    public Dictionary<string, string> ValidationErrors { get; set; } = new();
    public bool IsValid => ValidationErrors.Count == 0;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AttachmentInfo
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? Description { get; set; }
    public string? ThumbnailUrl { get; set; }
    public AttachmentSyncStatus SyncStatus { get; set; } = AttachmentSyncStatus.Synced;
}

public enum AttachmentSyncStatus
{
    Synced,
    PendingUpload,
    Uploading,
    UploadFailed,
    PendingDownload,
    Downloading,
    DownloadFailed
}

public class LayerInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public GeometryType GeometryType { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsEditable { get; set; } = true;
    public FormDefinition? Form { get; set; }
    public List<FieldDefinition> Schema { get; set; } = new();
    public LayerStyle Style { get; set; } = new();
}

public class FeatureQuery
{
    public SpatialFilter? SpatialFilter { get; set; }
    public string? WhereClause { get; set; }
    public List<OrderByClause>? OrderBy { get; set; }
    public int? MaxResults { get; set; }
}

public class SpatialFilter
{
    public Geometry? Geometry { get; set; }
    public SpatialRelationship Relationship { get; set; } = SpatialRelationship.Intersects;
}

public enum SpatialRelationship
{
    Intersects,
    Contains,
    Within,
    Overlaps,
    Touches,
    Crosses
}

public class OrderByClause
{
    public string FieldName { get; set; } = string.Empty;
    public bool Ascending { get; set; } = true;
}

public enum GeometryType
{
    Point,
    LineString,
    Polygon,
    MultiPoint,
    MultiLineString,
    MultiPolygon
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
