using Honua.Sdk.Field.Records;

namespace Honua.Mobile.Field.Capture;

/// <summary>
/// Mobile-owned media capture metadata that keeps local file paths outside the SDK field contract.
/// </summary>
public sealed record MobileFieldMediaAttachment
{
    /// <summary>Stable attachment identifier.</summary>
    public required string AttachmentId { get; init; }

    /// <summary>SDK field that owns this attachment, when known.</summary>
    public string? FieldId { get; init; }

    /// <summary>Mobile local file-system path for the captured media.</summary>
    public required string LocalPath { get; init; }

    /// <summary>SDK media type.</summary>
    public FieldMediaType MediaType { get; init; }

    /// <summary>Media content type, when known.</summary>
    public string? ContentType { get; init; }

    /// <summary>Media size in bytes, when known.</summary>
    public long? SizeBytes { get; init; }

    /// <summary>Location where the media was captured.</summary>
    public FieldGeoPoint? CaptureLocation { get; init; }

    /// <summary>UTC time the media was captured.</summary>
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Whether the host should blur faces before upload or export.</summary>
    public bool RequiresFaceBlur { get; init; }

    /// <summary>
    /// Mobile-owned evidence metadata, such as AR scene anchoring context. This is
    /// intentionally excluded from SDK attachment conversion until the SDK owns a
    /// portable evidence contract.
    /// </summary>
    public IReadOnlyDictionary<string, object?> EvidenceMetadata { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>
    /// Returns a copy with merged mobile evidence metadata.
    /// </summary>
    /// <param name="metadata">Metadata to merge into the attachment.</param>
    /// <returns>A copy containing the merged metadata.</returns>
    public MobileFieldMediaAttachment WithEvidenceMetadata(IReadOnlyDictionary<string, object?> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var merged = new Dictionary<string, object?>(EvidenceMetadata, StringComparer.Ordinal);
        foreach (var item in metadata)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Key);
            merged[item.Key] = item.Value;
        }

        return this with { EvidenceMetadata = merged };
    }

    /// <summary>
    /// Converts mobile capture metadata to the portable SDK attachment contract.
    /// </summary>
    /// <returns>SDK field media attachment without host-local file-system paths.</returns>
    public FieldMediaAttachment ToSdkAttachment()
    {
        return new FieldMediaAttachment
        {
            AttachmentId = AttachmentId,
            FieldId = FieldId,
            MediaType = MediaType,
            FileName = Path.GetFileName(LocalPath),
            ContentType = ContentType,
            SizeBytes = SizeBytes,
            CaptureLocation = CaptureLocation,
            CapturedAtUtc = CapturedAtUtc,
            RequiresFaceBlur = RequiresFaceBlur,
        };
    }
}
