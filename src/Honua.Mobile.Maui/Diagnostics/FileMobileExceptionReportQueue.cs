using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// File-backed local queue for sanitized mobile exception reports.
/// </summary>
public sealed class FileMobileExceptionReportQueue : IMobileExceptionReportQueue
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly MobileExceptionReportingOptions _options;
    private readonly ILogger<FileMobileExceptionReportQueue>? _logger;

    public FileMobileExceptionReportQueue(
        MobileExceptionReportingOptions options,
        ILogger<FileMobileExceptionReportQueue>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _logger = logger;
    }

    public async Task EnqueueAsync(MobileExceptionReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var directory = GetQueueDirectory();
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{report.OccurredAtUtc:yyyyMMddHHmmssfff}-{report.Id}.json");
        var json = JsonSerializer.Serialize(report, SerializerOptions);
        await File.WriteAllTextAsync(path, json, cancellationToken);
        TrimQueue(directory);
    }

    public async IAsyncEnumerable<QueuedMobileExceptionReport> ReadPendingAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = GetQueueDirectory();
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderBy(static path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            MobileExceptionReport? report = null;
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                report = JsonSerializer.Deserialize<MobileExceptionReport>(json, SerializerOptions);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Discarding unreadable mobile exception report {Path}", path);
                DeleteFile(path);
            }

            if (report is not null)
            {
                yield return new QueuedMobileExceptionReport(path, report);
            }
        }
    }

    public Task DeleteAsync(QueuedMobileExceptionReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();
        DeleteFile(report.QueueId);
        return Task.CompletedTask;
    }

    private string GetQueueDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.QueueDirectory))
        {
            return _options.QueueDirectory;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "Honua", "exception-reports");
    }

    private void TrimQueue(string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*.json")
            .OrderByDescending(static path => path, StringComparer.Ordinal)
            .Skip(_options.MaxQueuedReports)
            .ToArray();

        foreach (var file in files)
        {
            DeleteFile(file);
        }
    }

    private static void DeleteFile(string path)
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
