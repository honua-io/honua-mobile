// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Mobile boundary for reporting handled and unhandled exceptions without coupling to server transport.
/// </summary>
public interface IMobileExceptionReporter
{
    Task ReportAsync(
        Exception exception,
        MobileExceptionReportContext? context = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Disabled exception reporter used when mobile exception reporting is not opted in.
/// </summary>
public sealed class NoOpMobileExceptionReporter : IMobileExceptionReporter
{
    public Task ReportAsync(
        Exception exception,
        MobileExceptionReportContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Task.CompletedTask;
    }
}
