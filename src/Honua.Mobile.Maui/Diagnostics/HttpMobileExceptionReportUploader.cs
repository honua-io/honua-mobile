using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Posts sanitized mobile exception reports to an explicit server ingestion endpoint.
/// </summary>
public sealed class HttpMobileExceptionReportUploader : IMobileExceptionReportUploader
{
    private readonly HttpClient _httpClient;
    private readonly MobileExceptionReportingOptions _options;
    private readonly ILogger<HttpMobileExceptionReportUploader>? _logger;

    public HttpMobileExceptionReportUploader(
        HttpClient httpClient,
        MobileExceptionReportingOptions options,
        ILogger<HttpMobileExceptionReportUploader>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger;
    }

    public async Task<bool> UploadAsync(MobileExceptionReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (_options.Mode != MobileExceptionReportingMode.ServerUpload)
        {
            return false;
        }

        if (_options.UploadEndpoint is null)
        {
            _logger?.LogDebug("Mobile exception server upload is enabled but no upload endpoint is configured");
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.UploadEndpoint);
            var json = JsonSerializer.Serialize(report, HonuaMobileMauiDiagnosticsJsonContext.Default.MobileExceptionReport);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning(
                    "Mobile exception report upload failed with status {StatusCode}",
                    (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger?.LogWarning(ex, "Failed to upload mobile exception report");
            return false;
        }
    }
}
