namespace Honua.Mobile.Field.Records;

/// <summary>
/// A single data-collection record captured in the field, containing form values,
/// media attachments, location, and workflow status.
/// </summary>
public sealed class FieldRecord
{
    /// <summary>
    /// Unique identifier for this record.
    /// </summary>
    public required string RecordId { get; init; }

    /// <summary>
    /// The <see cref="Forms.FormDefinition.FormId"/> this record was collected against.
    /// </summary>
    public required string FormId { get; init; }

    /// <summary>
    /// Field values keyed by <see cref="Forms.FormField.FieldId"/>. Case-insensitive keys.
    /// </summary>
    public Dictionary<string, object?> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Media attachments (photos, videos, etc.) captured with this record.
    /// </summary>
    public List<MediaAttachment> Media { get; init; } = [];

    /// <summary>
    /// GPS location where the record was captured, or <see langword="null"/> if unavailable.
    /// </summary>
    public GeoPoint? Location { get; set; }

    /// <summary>
    /// Current workflow status of the record.
    /// </summary>
    public RecordStatus Status { get; set; } = RecordStatus.Draft;

    /// <summary>
    /// User ID of the field worker this record is assigned to, or <see langword="null"/> if unassigned.
    /// </summary>
    public string? AssignedUserId { get; set; }

    /// <summary>
    /// UTC timestamp when the record was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was submitted, or <see langword="null"/> if not yet submitted.
    /// </summary>
    public DateTimeOffset? SubmittedAtUtc { get; set; }

    /// <summary>
    /// UTC timestamp when the record was approved or rejected, or <see langword="null"/> if still in progress.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }

    /// <summary>
    /// Elapsed time from creation to completion, or <see langword="null"/> if not yet completed.
    /// </summary>
    public TimeSpan? Duration => CompletedAtUtc.HasValue ? CompletedAtUtc.Value - CreatedAtUtc : null;
}

/// <summary>
/// A media file attached to a <see cref="FieldRecord"/>.
/// </summary>
public sealed class MediaAttachment
{
    /// <summary>
    /// Unique identifier for this attachment.
    /// </summary>
    public required string AttachmentId { get; init; }

    /// <summary>
    /// Type of media (photo, video, audio, etc.).
    /// </summary>
    public required MediaType MediaType { get; init; }

    /// <summary>
    /// Local file system path to the media file.
    /// </summary>
    public required string LocalPath { get; init; }

    /// <summary>
    /// GPS location where the media was captured, or <see langword="null"/> if unavailable.
    /// </summary>
    public GeoPoint? CaptureLocation { get; init; }

    /// <summary>
    /// UTC timestamp when the media was captured.
    /// </summary>
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When <see langword="true"/>, faces in this media should be blurred before upload.
    /// </summary>
    public bool RequiresFaceBlur { get; init; }
}

/// <summary>
/// A geographic point with latitude, longitude, and optional GPS accuracy.
/// </summary>
/// <param name="Latitude">Latitude in decimal degrees.</param>
/// <param name="Longitude">Longitude in decimal degrees.</param>
/// <param name="AccuracyMeters">Horizontal GPS accuracy in meters, or <see langword="null"/> if unknown.</param>
public sealed record GeoPoint(double Latitude, double Longitude, double? AccuracyMeters = null);

/// <summary>
/// Types of media that can be attached to a <see cref="FieldRecord"/>.
/// </summary>
public enum MediaType
{
    /// <summary>A photograph.</summary>
    Photo,
    /// <summary>A video recording.</summary>
    Video,
    /// <summary>An audio recording.</summary>
    Audio,
    /// <summary>A digital signature capture.</summary>
    Signature,
    /// <summary>A freehand sketch or annotation.</summary>
    Sketch,
}

/// <summary>
/// Workflow status of a <see cref="FieldRecord"/>.
/// </summary>
public enum RecordStatus
{
    /// <summary>Record is being edited and has not been submitted.</summary>
    Draft,
    /// <summary>Record has been submitted for review.</summary>
    Submitted,
    /// <summary>Record has been approved.</summary>
    Approved,
    /// <summary>Record has been rejected and may be resubmitted.</summary>
    Rejected,
}
