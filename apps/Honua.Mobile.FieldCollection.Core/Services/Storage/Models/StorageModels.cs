using SQLite;
using Honua.Mobile.FieldCollection.Models;

using BoundingBox = Honua.Sdk.Geometry.GeographicBoundingBox;

namespace Honua.Mobile.FieldCollection.Services.Storage.Models;

/// <summary>
/// SQLite model for locally stored features with change tracking
/// </summary>
[Table("local_features")]
public class LocalFeature
{
    [PrimaryKey]
    [Column("storage_key")]
    public string StorageKey { get; set; } = string.Empty;

    [Column("id")]
    [Indexed]
    public string Id { get; set; } = string.Empty;

    [Column("layer_id")]
    [Indexed]
    public int LayerId { get; set; }

    [Column("geometry")]
    public byte[]? Geometry { get; set; }

    [Column("attributes")]
    public string? Attributes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("modified_at")]
    [Indexed]
    public DateTime ModifiedAt { get; set; }

    [Column("version")]
    public long Version { get; set; }

    [Column("sync_status")]
    [Indexed]
    public SyncStatus SyncStatus { get; set; }

    [Column("server_version")]
    public long? ServerVersion { get; set; }

    [Column("conflict_resolution")]
    public string? ConflictResolution { get; set; }
}

/// <summary>
/// Locally stored feature attachment metadata and sync state.
/// </summary>
[Table("local_attachments")]
public class LocalAttachment
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("feature_id")]
    [Indexed]
    public string FeatureId { get; set; } = string.Empty;

    [Column("layer_id")]
    [Indexed]
    public int LayerId { get; set; }

    [Column("remote_attachment_id")]
    [Indexed]
    public long? RemoteAttachmentId { get; set; }

    [Column("remote_global_id")]
    public string? RemoteGlobalId { get; set; }

    [Column("file_name")]
    public string FileName { get; set; } = string.Empty;

    [Column("content_type")]
    public string ContentType { get; set; } = "application/octet-stream";

    [Column("payload_kind")]
    public AttachmentPayloadKind PayloadKind { get; set; } = AttachmentPayloadKind.File;

    [Column("size_bytes")]
    public long SizeBytes { get; set; }

    [Column("local_path")]
    public string? LocalPath { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("uploaded_at")]
    public DateTime UploadedAt { get; set; }

    [Column("last_synced_at")]
    public DateTime? LastSyncedAt { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("capture_location_json")]
    public string? CaptureLocationJson { get; set; }

    [Column("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [Column("sync_status")]
    [Indexed]
    public AttachmentSyncStatus SyncStatus { get; set; } = AttachmentSyncStatus.Synced;

    [Column("retry_count")]
    public int RetryCount { get; set; }

    [Column("last_error")]
    public string? LastError { get; set; }

    [Column("is_deleted")]
    [Indexed]
    public bool IsDeleted { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// Change tracking record for delta sync
/// </summary>
[Table("change_records")]
public class ChangeRecord
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("feature_id")]
    [Indexed]
    public string FeatureId { get; set; } = string.Empty;

    [Column("layer_id")]
    [Indexed]
    public int LayerId { get; set; }

    [Column("operation")]
    public ChangeOperation Operation { get; set; }

    [Column("timestamp")]
    [Indexed]
    public DateTime Timestamp { get; set; }

    [Column("sync_status")]
    [Indexed]
    public SyncStatus SyncStatus { get; set; }

    [Column("change_data")]
    public string? ChangeData { get; set; }

    [Column("conflict_id")]
    public string? ConflictId { get; set; }
}

/// <summary>
/// Sync session tracking for resumable sync operations
/// </summary>
[Table("sync_sessions")]
public class SyncSession
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("start_time")]
    public DateTime StartTime { get; set; }

    [Column("end_time")]
    public DateTime? EndTime { get; set; }

    [Column("status")]
    public SyncSessionStatus Status { get; set; }

    [Column("server_generation")]
    public long ServerGeneration { get; set; }

    [Column("local_generation")]
    public long LocalGeneration { get; set; }

    [Column("changes_pulled")]
    public int ChangesPulled { get; set; }

    [Column("changes_pushed")]
    public int ChangesPushed { get; set; }

    [Column("conflicts_detected")]
    public int ConflictsDetected { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Conflict record for manual resolution
/// </summary>
[Table("conflict_records")]
public class ConflictRecord
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    [Column("feature_id")]
    [Indexed]
    public string FeatureId { get; set; } = string.Empty;

    [Column("layer_id")]
    public int LayerId { get; set; }

    [Column("conflict_type")]
    public ConflictType ConflictType { get; set; }

    [Column("local_version")]
    public long LocalVersion { get; set; }

    [Column("server_version")]
    public long ServerVersion { get; set; }

    [Column("local_data")]
    public string LocalData { get; set; } = string.Empty;

    [Column("server_data")]
    public string ServerData { get; set; } = string.Empty;

    [Column("resolution")]
    public ConflictResolution? Resolution { get; set; }

    [Column("resolved_data")]
    public string? ResolvedData { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("resolved_at")]
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>
/// Layer metadata storage
/// </summary>
[Table("layer_metadata")]
public class LayerMetadata
{
    [PrimaryKey]
    [Column("storage_key")]
    public string StorageKey { get; set; } = string.Empty;

    [Column("id")]
    public int Id { get; set; }

    [Column("service_id")]
    public string? ServiceId { get; set; }

    [Column("source_id")]
    public string? SourceId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("geometry_type")]
    public string GeometryType { get; set; } = string.Empty;

    [Column("spatial_reference")]
    public string SpatialReference { get; set; } = "EPSG:4326";

    [Column("is_editable")]
    public bool IsEditable { get; set; }

    [Column("schema")]
    public string? Schema { get; set; }

    [Column("server_url")]
    public string? ServerUrl { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("last_sync")]
    public DateTime? LastSync { get; set; }

    [Column("sync_enabled")]
    public bool SyncEnabled { get; set; } = true;
}

/// <summary>
/// OGC GeoPackage contents table
/// </summary>
[Table("gpkg_contents")]
public class GpkgContent
{
    [PrimaryKey]
    [Column("table_name")]
    public string TableName { get; set; } = string.Empty;

    [Column("data_type")]
    public string DataType { get; set; } = string.Empty;

    [Column("identifier")]
    public string? Identifier { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("last_change")]
    public DateTime LastChange { get; set; }

    [Column("min_x")]
    public double? MinX { get; set; }

    [Column("min_y")]
    public double? MinY { get; set; }

    [Column("max_x")]
    public double? MaxX { get; set; }

    [Column("max_y")]
    public double? MaxY { get; set; }

    [Column("srs_id")]
    public int? SrsId { get; set; }
}

/// <summary>
/// Enumeration for change operations
/// </summary>
public enum ChangeOperation
{
    Insert = 1,
    Update = 2,
    Delete = 3
}

/// <summary>
/// Enumeration for sync status
/// </summary>
public enum SyncStatus
{
    Synced = 0,
    PendingUpload = 1,
    PendingDownload = 2,
    Conflict = 3,
    Error = 4
}

/// <summary>
/// Enumeration for sync session status
/// </summary>
public enum SyncSessionStatus
{
    Active = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Enumeration for conflict types
/// </summary>
public enum ConflictType
{
    UpdateUpdate = 1,    // Both local and server updated
    UpdateDelete = 2,    // Local updated, server deleted
    DeleteUpdate = 3,    // Local deleted, server updated
    DeleteDelete = 4     // Both deleted (should not happen)
}

/// <summary>
/// Stored conflict resolution decisions.
/// </summary>
public enum ConflictResolution
{
    AcceptLocal = 1,
    AcceptServer = 2,
    Merge = 3,
    Manual = 4
}

/// <summary>
/// Spatial query parameters
/// </summary>
public class SpatialQuery
{
    public BoundingBox Bounds { get; set; } = new();
    public SpatialRelationship Relationship { get; set; } = SpatialRelationship.Intersects;
    public int? MaxResults { get; set; }
}

/// <summary>
/// Bounding box for spatial queries
/// </summary>
public class BoundingBox
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }

    public bool IsValid => MinX <= MaxX && MinY <= MaxY;

    public static BoundingBox FromCoordinates(double x1, double y1, double x2, double y2)
    {
        return new BoundingBox
        {
            MinX = Math.Min(x1, x2),
            MinY = Math.Min(y1, y2),
            MaxX = Math.Max(x1, x2),
            MaxY = Math.Max(y1, y2)
        };
    }
}

/// <summary>
/// Spatial relationship types for queries
/// </summary>
public enum SpatialRelationship
{
    Intersects,
    Contains,
    Within,
    Overlaps,
    Touches,
    Crosses
}
