namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Drains locally queued mobile exception reports without blocking app startup or foreground workflows.
/// </summary>
public interface IMobileExceptionReportUploadWorker
{
    Task FlushPendingAsync(CancellationToken cancellationToken = default);
}
