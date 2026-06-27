// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Offline queue boundary used by local exception reporting and future retry/upload workers.
/// </summary>
public interface IMobileExceptionReportQueue
{
    Task EnqueueAsync(MobileExceptionReport report, CancellationToken cancellationToken = default);

    IAsyncEnumerable<QueuedMobileExceptionReport> ReadPendingAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(QueuedMobileExceptionReport report, CancellationToken cancellationToken = default);
}

public sealed record QueuedMobileExceptionReport(string QueueId, MobileExceptionReport Report);
