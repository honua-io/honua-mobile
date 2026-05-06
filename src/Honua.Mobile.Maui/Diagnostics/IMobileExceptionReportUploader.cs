namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Transport boundary for sending sanitized mobile exception reports to an app-configured ingestion point.
/// </summary>
public interface IMobileExceptionReportUploader
{
    Task<bool> UploadAsync(MobileExceptionReport report, CancellationToken cancellationToken = default);
}
