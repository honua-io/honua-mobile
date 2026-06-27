// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Severity label for locally captured exception reports.
/// </summary>
public enum MobileExceptionSeverity
{
    Error,
    Warning,
    Critical,
}

/// <summary>
/// Build, runtime, and device metadata attached to a mobile exception report.
/// </summary>
public sealed record MobileExceptionReportMetadata
{
    public string? AppId { get; init; }

    public string? AppVersion { get; init; }

    public string? BuildNumber { get; init; }

    public string? CommitSha { get; init; }

    public string? Branch { get; init; }

    public string? EnvironmentName { get; init; }

    public string? Platform { get; init; }

    public string? OsVersion { get; init; }

    public string? DeviceClass { get; init; }

    public IReadOnlyDictionary<string, string?> Properties { get; init; } = new Dictionary<string, string?>();
}

/// <summary>
/// Caller-supplied context for handled or unhandled mobile exception reporting.
/// </summary>
public sealed record MobileExceptionReportContext
{
    public string Source { get; init; } = "Unknown";

    public string? Operation { get; init; }

    public string? CorrelationId { get; init; }

    public string? RequestId { get; init; }

    public MobileExceptionSeverity Severity { get; init; } = MobileExceptionSeverity.Error;

    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
}

/// <summary>
/// Sanitized mobile exception report stored on device and posted to configured ingestion endpoints.
/// </summary>
public sealed record MobileExceptionReport
{
    public required string Id { get; init; }

    public required string Fingerprint { get; init; }

    public required DateTimeOffset OccurredAtUtc { get; init; }

    public required string Source { get; init; }

    public string? Operation { get; init; }

    public string? CorrelationId { get; init; }

    public string? RequestId { get; init; }

    public MobileExceptionSeverity Severity { get; init; } = MobileExceptionSeverity.Error;

    public required string ExceptionType { get; init; }

    public string? Message { get; init; }

    public string? StackTrace { get; init; }

    public string? InnerExceptionType { get; init; }

    public string? InnerExceptionMessage { get; init; }

    public MobileExceptionReportMetadata Metadata { get; init; } = new();

    public IReadOnlyDictionary<string, string?> Context { get; init; } = new Dictionary<string, string?>();
}
