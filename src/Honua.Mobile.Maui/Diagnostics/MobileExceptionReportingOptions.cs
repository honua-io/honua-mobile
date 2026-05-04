namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Opt-in reporting modes for mobile exception capture.
/// </summary>
public enum MobileExceptionReportingMode
{
    /// <summary>Do not capture exception reports.</summary>
    Disabled,

    /// <summary>Capture sanitized reports into the local mobile queue only.</summary>
    LocalOnly,
}

/// <summary>
/// Controls local mobile exception report capture, sanitization, and queue retention.
/// </summary>
public sealed record MobileExceptionReportingOptions
{
    public MobileExceptionReportingMode Mode { get; init; } = MobileExceptionReportingMode.Disabled;

    public string? QueueDirectory { get; init; }

    public int MaxQueuedReports { get; init; } = 100;

    public int MaxMessageLength { get; init; } = 2_000;

    public int MaxStackTraceLength { get; init; } = 8_000;

    public TimeSpan DuplicateWindow { get; init; } = TimeSpan.FromMinutes(5);

    public bool IncludePreciseLocation { get; init; }

    public bool IncludeFormPayloads { get; init; }

    public bool IncludeAttachmentContent { get; init; }

    public MobileExceptionReportMetadata Metadata { get; init; } = new();

    public void Validate()
    {
        if (MaxQueuedReports <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxQueuedReports), "The local exception queue size must be positive.");
        }

        if (MaxMessageLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxMessageLength), "The exception message limit must be positive.");
        }

        if (MaxStackTraceLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxStackTraceLength), "The exception stack trace limit must be positive.");
        }

        if (DuplicateWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DuplicateWindow), "The duplicate window cannot be negative.");
        }
    }
}
