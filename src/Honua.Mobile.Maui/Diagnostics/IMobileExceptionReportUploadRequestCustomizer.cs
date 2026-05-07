namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Allows mobile apps to add approved request metadata, such as same-origin auth headers,
/// before a sanitized exception report upload is sent.
/// </summary>
public interface IMobileExceptionReportUploadRequestCustomizer
{
    void Customize(HttpRequestMessage request, MobileExceptionReport report);
}
