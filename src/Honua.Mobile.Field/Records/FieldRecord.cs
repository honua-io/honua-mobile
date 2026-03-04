namespace Honua.Mobile.Field.Records;

public sealed class FieldRecord
{
    public required string RecordId { get; init; }

    public required string FormId { get; init; }

    public Dictionary<string, object?> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<MediaAttachment> Media { get; init; } = [];

    public GeoPoint? Location { get; set; }

    public RecordStatus Status { get; set; } = RecordStatus.Draft;

    public string? AssignedUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SubmittedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public TimeSpan? Duration => CompletedAtUtc.HasValue ? CompletedAtUtc.Value - CreatedAtUtc : null;
}

public sealed class MediaAttachment
{
    public required string AttachmentId { get; init; }

    public required MediaType MediaType { get; init; }

    public required string LocalPath { get; init; }

    public GeoPoint? CaptureLocation { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool RequiresFaceBlur { get; init; }
}

public sealed record GeoPoint(double Latitude, double Longitude, double? AccuracyMeters = null);

public enum MediaType
{
    Photo,
    Video,
    Audio,
    Signature,
    Sketch,
}

public enum RecordStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
}
