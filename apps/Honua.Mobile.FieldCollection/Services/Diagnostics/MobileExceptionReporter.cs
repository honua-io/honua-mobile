using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Honua.Mobile.FieldCollection.Services.Diagnostics;

public enum MobileExceptionReportingMode
{
    Disabled,
    Server
}

public sealed class MobileExceptionReportingOptions
{
    public MobileExceptionReportingMode Mode { get; init; } = MobileExceptionReportingMode.Disabled;
    public Uri? Endpoint { get; init; }
    public int MaxMessageLength { get; init; } = 2_000;
    public int MaxStackTraceLength { get; init; } = 8_000;
    public string? PendingReportsDirectory { get; init; }

    public static MobileExceptionReportingOptions FromPreferences()
    {
        var modeValue = Preferences.Default.Get("honua_exception_reporting_mode", "Disabled");
        var endpointValue = Preferences.Default.Get("honua_exception_reporting_endpoint", string.Empty);

        return new MobileExceptionReportingOptions
        {
            Mode = Enum.TryParse<MobileExceptionReportingMode>(modeValue, ignoreCase: true, out var mode)
                ? mode
                : MobileExceptionReportingMode.Disabled,
            Endpoint = Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
                ? endpoint
                : null,
            PendingReportsDirectory = Preferences.Default.Get("honua_exception_reporting_queue_path", string.Empty)
        };
    }
}

public interface IMobileExceptionReporter
{
    Task ReportAsync(
        Exception exception,
        string source,
        IReadOnlyDictionary<string, string?>? context = null,
        CancellationToken cancellationToken = default);

    Task FlushPendingAsync(CancellationToken cancellationToken = default);
}

public sealed class MobileExceptionReporter : IMobileExceptionReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string PendingReportsDirectoryName = "pending-exception-reports";

    private readonly HttpClient _httpClient;
    private readonly IAuthenticationService _authService;
    private readonly MobileExceptionReportingOptions _options;
    private readonly ILogger<MobileExceptionReporter>? _logger;

    public MobileExceptionReporter(
        HttpClient httpClient,
        IAuthenticationService authService,
        MobileExceptionReportingOptions options,
        ILogger<MobileExceptionReporter>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
    }

    public async Task ReportAsync(
        Exception exception,
        string source,
        IReadOnlyDictionary<string, string?>? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_options.Mode != MobileExceptionReportingMode.Server)
        {
            return;
        }

        var endpoint = ResolveEndpoint();
        if (endpoint == null)
        {
            _logger?.LogDebug("Mobile exception reporting is enabled but no endpoint is configured");
            return;
        }

        if (!IsAllowedEndpoint(endpoint))
        {
            _logger?.LogWarning("Mobile exception reporting endpoint must use HTTPS unless it points to localhost");
            return;
        }

        try
        {
            var report = BuildReport(exception, source, context);
            var pendingPath = await PersistPendingReportAsync(report, cancellationToken);

            if (await SendReportAsync(report, endpoint, cancellationToken))
            {
                DeletePendingReport(pendingPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to persist pending mobile exception report");
        }
    }

    public async Task FlushPendingAsync(CancellationToken cancellationToken = default)
    {
        if (_options.Mode != MobileExceptionReportingMode.Server)
        {
            return;
        }

        var endpoint = ResolveEndpoint();
        if (endpoint == null || !IsAllowedEndpoint(endpoint))
        {
            return;
        }

        var pendingDirectory = GetPendingReportsDirectory();
        if (!Directory.Exists(pendingDirectory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(pendingDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            MobileExceptionReport? report;
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                report = JsonSerializer.Deserialize<MobileExceptionReport>(json, SerializerOptions);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Discarding unreadable pending mobile exception report");
                DeletePendingReport(path);
                continue;
            }

            if (report != null && await SendReportAsync(report, endpoint, cancellationToken))
            {
                DeletePendingReport(path);
            }
        }
    }

    private async Task<bool> SendReportAsync(
        MobileExceptionReport report,
        Uri endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (ShouldAttachCredentials(endpoint))
            {
                request.Headers.TryAddWithoutValidation("X-API-Key", _authService.ApiKey);
            }

            var json = JsonSerializer.Serialize(report, SerializerOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning(
                    "Mobile exception report failed with status {StatusCode}",
                    (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Failed to send mobile exception report");
            return false;
        }
    }

    private Uri? ResolveEndpoint()
    {
        if (_options.Endpoint != null)
        {
            return _options.Endpoint;
        }

        return Uri.TryCreate(_authService.ServerUrl, UriKind.Absolute, out var serverUri)
            ? new Uri(serverUri, "/api/mobile/exceptions")
            : null;
    }

    private static bool IsAllowedEndpoint(Uri endpoint)
    {
        return endpoint.Scheme == Uri.UriSchemeHttps ||
            (endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback);
    }

    private bool ShouldAttachCredentials(Uri endpoint)
    {
        return !string.IsNullOrWhiteSpace(_authService.ApiKey) &&
            Uri.TryCreate(_authService.ServerUrl, UriKind.Absolute, out var serverUri) &&
            Uri.Compare(
                endpoint,
                serverUri,
                UriComponents.SchemeAndServer,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0;
    }

    private MobileExceptionReport BuildReport(
        Exception exception,
        string source,
        IReadOnlyDictionary<string, string?>? context)
    {
        return new MobileExceptionReport
        {
            Id = Guid.NewGuid().ToString("N"),
            Source = string.IsNullOrWhiteSpace(source) ? "Unknown" : source,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            AppVersion = SafeAppVersion(),
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            Message = Truncate(exception.Message, _options.MaxMessageLength),
            StackTrace = Truncate(exception.StackTrace, _options.MaxStackTraceLength),
            InnerExceptionType = exception.InnerException?.GetType().FullName,
            InnerExceptionMessage = Truncate(exception.InnerException?.Message, _options.MaxMessageLength),
            Context = SanitizeContext(context)
        };
    }

    private static string SafeAppVersion()
    {
        try
        {
            return AppInfo.Current.VersionString;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static IReadOnlyDictionary<string, string?> SanitizeContext(IReadOnlyDictionary<string, string?>? context)
    {
        if (context == null || context.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        return context
            .Where(item => !LooksSensitive(item.Key))
            .ToDictionary(item => item.Key, item => item.Value);
    }

    private static bool LooksSensitive(string key)
    {
        return key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("apiKey", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("api_key", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private async Task<string> PersistPendingReportAsync(
        MobileExceptionReport report,
        CancellationToken cancellationToken)
    {
        var directory = GetPendingReportsDirectory();
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{report.Id}.json");
        var json = JsonSerializer.Serialize(report, SerializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        return path;
    }

    private string GetPendingReportsDirectory()
    {
        return string.IsNullOrWhiteSpace(_options.PendingReportsDirectory)
            ? Path.Combine(FileSystem.AppDataDirectory, PendingReportsDirectoryName)
            : _options.PendingReportsDirectory;
    }

    private static void DeletePendingReport(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

public sealed class NoOpMobileExceptionReporter : IMobileExceptionReporter
{
    public Task ReportAsync(
        Exception exception,
        string source,
        IReadOnlyDictionary<string, string?>? context = null,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task FlushPendingAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

internal sealed class MobileExceptionReport
{
    public string Id { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
    public string AppVersion { get; init; } = string.Empty;
    public string ExceptionType { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string? StackTrace { get; init; }
    public string? InnerExceptionType { get; init; }
    public string? InnerExceptionMessage { get; init; }
    public IReadOnlyDictionary<string, string?> Context { get; init; } = new Dictionary<string, string?>();
}
