// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

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

    /// <summary>Capture sanitized reports locally and allow an upload worker to drain the queue.</summary>
    ServerUpload,
}

/// <summary>
/// Controls local mobile exception report capture, sanitization, and queue retention.
/// </summary>
public sealed record MobileExceptionReportingOptions
{
    public MobileExceptionReportingMode Mode { get; init; } = MobileExceptionReportingMode.Disabled;

    public string? QueueDirectory { get; init; }

    public Uri? UploadEndpoint { get; init; }

    public int MaxQueuedReports { get; init; } = 100;

    public int MaxUploadBatchSize { get; init; } = 10;

    public int MaxMessageLength { get; init; } = 2_000;

    public int MaxStackTraceLength { get; init; } = 8_000;

    public TimeSpan DuplicateWindow { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan UploadInitialBackoff { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan UploadMaxBackoff { get; init; } = TimeSpan.FromMinutes(15);

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

        if (MaxUploadBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxUploadBatchSize), "The exception upload batch size must be positive.");
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

        if (UploadInitialBackoff < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(UploadInitialBackoff), "The upload initial backoff cannot be negative.");
        }

        if (UploadMaxBackoff < UploadInitialBackoff)
        {
            throw new ArgumentOutOfRangeException(nameof(UploadMaxBackoff), "The upload max backoff cannot be less than the initial backoff.");
        }

        if (UploadEndpoint is not null && !IsAllowedUploadEndpoint(UploadEndpoint))
        {
            throw new InvalidOperationException("The mobile exception upload endpoint must use HTTPS unless it points to localhost.");
        }
    }

    internal static bool IsAllowedUploadEndpoint(Uri endpoint)
    {
        return endpoint.Scheme == Uri.UriSchemeHttps ||
            (endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback);
    }
}
