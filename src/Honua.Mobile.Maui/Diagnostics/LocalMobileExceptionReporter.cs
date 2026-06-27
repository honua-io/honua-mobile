// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Builds sanitized exception reports and stores them in the local offline queue.
/// </summary>
public sealed class LocalMobileExceptionReporter : IMobileExceptionReporter
{
    private readonly IMobileExceptionReportQueue _queue;
    private readonly MobileExceptionReportingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LocalMobileExceptionReporter>? _logger;
    private readonly object _dedupeGate = new();
    private readonly Dictionary<string, DateTimeOffset> _recentFingerprints = new(StringComparer.Ordinal);

    public LocalMobileExceptionReporter(
        IMobileExceptionReportQueue queue,
        MobileExceptionReportingOptions options,
        TimeProvider? timeProvider = null,
        ILogger<LocalMobileExceptionReporter>? logger = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task ReportAsync(
        Exception exception,
        MobileExceptionReportContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_options.Mode is not (MobileExceptionReportingMode.LocalOnly or MobileExceptionReportingMode.ServerUpload))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var reportContext = context ?? new MobileExceptionReportContext();
        var report = BuildReport(exception, reportContext, now);

        if (IsDuplicate(report.Fingerprint, now))
        {
            return;
        }

        try
        {
            await _queue.EnqueueAsync(report, cancellationToken);
            TrackFingerprint(report.Fingerprint, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger?.LogWarning(ex, "Failed to queue sanitized mobile exception report");
        }
    }

    private MobileExceptionReport BuildReport(
        Exception exception,
        MobileExceptionReportContext context,
        DateTimeOffset occurredAtUtc)
    {
        var source = string.IsNullOrWhiteSpace(context.Source) ? "Unknown" : context.Source;
        var sanitizedMessage = MobileExceptionRedactor.RedactText(exception.Message, _options, _options.MaxMessageLength);
        var sanitizedStackTrace = MobileExceptionRedactor.RedactText(exception.StackTrace, _options, _options.MaxStackTraceLength);
        var sanitizedInnerMessage = MobileExceptionRedactor.RedactText(
            exception.InnerException?.Message,
            _options,
            _options.MaxMessageLength);

        return new MobileExceptionReport
        {
            Id = Guid.NewGuid().ToString("N"),
            Fingerprint = CreateFingerprint(exception, source, sanitizedMessage),
            OccurredAtUtc = occurredAtUtc,
            Source = source,
            Operation = MobileExceptionRedactor.RedactText(context.Operation, _options, _options.MaxMessageLength),
            CorrelationId = MobileExceptionRedactor.RedactText(context.CorrelationId, _options, _options.MaxMessageLength),
            RequestId = MobileExceptionRedactor.RedactText(context.RequestId, _options, _options.MaxMessageLength),
            Severity = context.Severity,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            Message = sanitizedMessage,
            StackTrace = sanitizedStackTrace,
            InnerExceptionType = exception.InnerException?.GetType().FullName,
            InnerExceptionMessage = sanitizedInnerMessage,
            Metadata = RedactMetadata(_options.Metadata),
            Context = MobileExceptionRedactor.RedactProperties(context.Properties, _options),
        };
    }

    private MobileExceptionReportMetadata RedactMetadata(MobileExceptionReportMetadata metadata)
    {
        return metadata with
        {
            AppId = MobileExceptionRedactor.RedactText(metadata.AppId, _options, _options.MaxMessageLength),
            AppVersion = MobileExceptionRedactor.RedactText(metadata.AppVersion, _options, _options.MaxMessageLength),
            BuildNumber = MobileExceptionRedactor.RedactText(metadata.BuildNumber, _options, _options.MaxMessageLength),
            CommitSha = MobileExceptionRedactor.RedactText(metadata.CommitSha, _options, _options.MaxMessageLength),
            Branch = MobileExceptionRedactor.RedactText(metadata.Branch, _options, _options.MaxMessageLength),
            EnvironmentName = MobileExceptionRedactor.RedactText(metadata.EnvironmentName, _options, _options.MaxMessageLength),
            Platform = MobileExceptionRedactor.RedactText(metadata.Platform, _options, _options.MaxMessageLength),
            OsVersion = MobileExceptionRedactor.RedactText(metadata.OsVersion, _options, _options.MaxMessageLength),
            DeviceClass = MobileExceptionRedactor.RedactText(metadata.DeviceClass, _options, _options.MaxMessageLength),
            Properties = MobileExceptionRedactor.RedactMetadata(metadata.Properties, _options),
        };
    }

    private bool IsDuplicate(string fingerprint, DateTimeOffset occurredAtUtc)
    {
        if (_options.DuplicateWindow == TimeSpan.Zero)
        {
            return false;
        }

        lock (_dedupeGate)
        {
            foreach (var (knownFingerprint, lastSeen) in _recentFingerprints.ToArray())
            {
                if (occurredAtUtc - lastSeen > _options.DuplicateWindow)
                {
                    _recentFingerprints.Remove(knownFingerprint);
                }
            }

            if (_recentFingerprints.TryGetValue(fingerprint, out var previous) &&
                occurredAtUtc - previous <= _options.DuplicateWindow)
            {
                return true;
            }

            return false;
        }
    }

    private void TrackFingerprint(string fingerprint, DateTimeOffset occurredAtUtc)
    {
        if (_options.DuplicateWindow == TimeSpan.Zero)
        {
            return;
        }

        lock (_dedupeGate)
        {
            _recentFingerprints[fingerprint] = occurredAtUtc;
        }
    }

    private static string CreateFingerprint(Exception exception, string source, string? sanitizedMessage)
    {
        var fingerprintSource = string.Join(
            '\n',
            source,
            exception.GetType().FullName,
            sanitizedMessage,
            FirstStackFrame(exception));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? FirstStackFrame(Exception exception)
    {
        return exception.StackTrace?
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
    }
}
