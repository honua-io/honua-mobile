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
