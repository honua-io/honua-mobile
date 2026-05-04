using Honua.Mobile.FieldCollection.Services.Storage;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace Honua.Mobile.FieldCollection.Services.Diagnostics;

/// <summary>
/// Diagnostic service for monitoring app health, database status, and performance
/// </summary>
public class DiagnosticService
{
    private readonly DatabaseService _databaseService;
    private readonly ISyncService _syncService;
    private readonly IConnectivityService _connectivityService;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<DiagnosticService>? _logger;

    public DiagnosticService(
        DatabaseService databaseService,
        ISyncService syncService,
        IConnectivityService connectivityService,
        IAuthenticationService authService,
        ILogger<DiagnosticService>? logger = null)
    {
        _databaseService = databaseService;
        _syncService = syncService;
        _connectivityService = connectivityService;
        _authService = authService;
        _logger = logger;
    }

    #region System Diagnostics

    public async Task<SystemDiagnostics> GetSystemDiagnosticsAsync()
    {
        var diagnostics = new SystemDiagnostics
        {
            AppVersion = GetAppVersion(),
            Platform = DeviceInfo.Platform.ToString(),
            DeviceModel = DeviceInfo.Model,
            OperatingSystem = $"{DeviceInfo.Platform} {DeviceInfo.VersionString}",
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Manufacturer = DeviceInfo.Manufacturer,
            DeviceName = DeviceInfo.Name,
            DeviceType = DeviceInfo.DeviceType.ToString(),
            Timestamp = DateTime.UtcNow
        };

        // Memory information
        try
        {
            var memoryInfo = GetMemoryInfo();
            diagnostics.MemoryUsageMB = memoryInfo.UsedMemoryMB;
            diagnostics.AvailableMemoryMB = memoryInfo.AvailableMemoryMB;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to collect memory diagnostics");
            diagnostics.MemoryUsageMB = 0;
            diagnostics.AvailableMemoryMB = 0;
        }

        // Storage information
        diagnostics.DatabaseInfo = await _databaseService.GetDatabaseInfoAsync();

        return diagnostics;
    }

    public async Task<ConnectivityDiagnostics> GetConnectivityDiagnosticsAsync()
    {
        var diagnostics = new ConnectivityDiagnostics
        {
            IsConnected = _connectivityService.IsConnected,
            ConnectionProfiles = GetConnectionProfiles(),
            Timestamp = DateTime.UtcNow
        };

        // Test server connectivity
        if (_connectivityService.IsConnected && _authService.IsAuthenticated)
        {
            try
            {
                diagnostics.ServerReachable = await TestServerConnectivityAsync();
                diagnostics.ServerResponseTimeMs = await MeasureServerResponseTimeAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to collect server connectivity diagnostics");
                diagnostics.ServerReachable = false;
                diagnostics.ServerResponseTimeMs = -1;
            }
        }

        return diagnostics;
    }

    public async Task<SyncDiagnostics> GetSyncDiagnosticsAsync()
    {
        var diagnostics = new SyncDiagnostics
        {
            IsSyncing = _syncService.IsSyncing,
            LastSyncTime = _syncService.LastSyncTime,
            PendingChanges = _syncService.PendingChangesCount,
            SyncStatus = _syncService.Status.ToString(),
            Timestamp = DateTime.UtcNow
        };

        // Get sync conflicts
        try
        {
            var conflicts = (await _syncService.GetConflictsAsync()).ToList();
            diagnostics.ConflictCount = conflicts.Count;
            diagnostics.ConflictDetails = conflicts.Take(5).Select(c => new ConflictSummary
            {
                ConflictId = c.Id,
                LayerName = c.LayerName,
                ConflictType = c.Type.ToString()
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to collect sync conflict diagnostics");
            diagnostics.ConflictCount = 0;
            diagnostics.ConflictDetails = new List<ConflictSummary>();
        }

        return diagnostics;
    }

    #endregion

    #region Database Management

    public async Task<DatabaseDiagnostics> GetDatabaseDiagnosticsAsync()
    {
        var databaseInfo = await _databaseService.GetDatabaseInfoAsync();

        var diagnostics = new DatabaseDiagnostics
        {
            DatabasePath = databaseInfo.DatabasePath,
            DatabaseExists = databaseInfo.Exists,
            DatabaseSize = databaseInfo.SizeMB,
            Created = databaseInfo.Created,
            LastModified = databaseInfo.Modified,
            IsInitialized = await _databaseService.IsInitializedAsync(),
            Timestamp = DateTime.UtcNow
        };

        if (databaseInfo.Statistics != null)
        {
            diagnostics.TotalFeatures = databaseInfo.Statistics.TotalFeatures;
            diagnostics.PendingChanges = databaseInfo.Statistics.PendingChanges;
            diagnostics.LayerCount = databaseInfo.Statistics.LayerCount;
        }

        return diagnostics;
    }

    public async Task<bool> CompactDatabaseAsync()
    {
        try
        {
            return await _databaseService.CompactDatabaseAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Database compaction failed");
            return false;
        }
    }

    public async Task<bool> ResetDatabaseAsync()
    {
        try
        {
            return await _databaseService.ResetDatabaseAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Database reset failed");
            return false;
        }
    }

    #endregion

    #region Export and Backup

    public async Task<DiagnosticReport> GenerateDiagnosticReportAsync()
    {
        var report = new DiagnosticReport
        {
            GeneratedAt = DateTime.UtcNow,
            AppVersion = GetAppVersion(),
            System = await GetSystemDiagnosticsAsync(),
            Connectivity = await GetConnectivityDiagnosticsAsync(),
            Sync = await GetSyncDiagnosticsAsync(),
            Database = await GetDatabaseDiagnosticsAsync()
        };

        return report;
    }

    public async Task<string> ExportDiagnosticsAsync()
    {
        try
        {
            var report = await GenerateDiagnosticReportAsync();
            RedactSensitiveFields(report);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var fileName = $"honua_diagnostics_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(documentsPath, fileName);

            await File.WriteAllTextAsync(filePath, json);
            return filePath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to export diagnostics.", ex);
        }
    }

    #endregion

    #region Helper Methods

    private string GetAppVersion()
    {
        try
        {
            return AppInfo.Current.VersionString;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to get app version");
            return "Unknown";
        }
    }

    private static MemoryInfo GetMemoryInfo()
    {
        // Platform-specific memory information
        // This is a simplified implementation
        return new MemoryInfo
        {
            UsedMemoryMB = 0, // Would implement platform-specific memory queries
            AvailableMemoryMB = 0
        };
    }

    private List<string> GetConnectionProfiles()
    {
        try
        {
            var profiles = Connectivity.Current.ConnectionProfiles;
            return profiles.Select(p => p.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to get connection profiles");
            return new List<string> { "Unknown" };
        }
    }

    private Task<bool> TestServerConnectivityAsync()
    {
        return string.IsNullOrWhiteSpace(_authService.ServerUrl)
            ? Task.FromResult(false)
            : _authService.ValidateConnectionAsync(_authService.ServerUrl, _authService.ApiKey);
    }

    private async Task<int> MeasureServerResponseTimeAsync()
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await TestServerConnectivityAsync();
            stopwatch.Stop();
            return (int)stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to measure server response time");
            return -1;
        }
    }

    private static void RedactSensitiveFields(DiagnosticReport report)
    {
        report.System.DeviceName = "Redacted";

        if (report.System.DatabaseInfo != null)
        {
            report.System.DatabaseInfo.DatabasePath = RedactPath(report.System.DatabaseInfo.DatabasePath);
        }

        report.Database.DatabasePath = RedactPath(report.Database.DatabasePath);
    }

    private static string RedactPath(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetFileName(path);
    }

    #endregion
}

#region Diagnostic Models

public class SystemDiagnostics
{
    public string AppVersion { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public double MemoryUsageMB { get; set; }
    public double AvailableMemoryMB { get; set; }
    public DatabaseInfo? DatabaseInfo { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ConnectivityDiagnostics
{
    public bool IsConnected { get; set; }
    public List<string> ConnectionProfiles { get; set; } = new();
    public bool ServerReachable { get; set; }
    public int ServerResponseTimeMs { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SyncDiagnostics
{
    public bool IsSyncing { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public int PendingChanges { get; set; }
    public string SyncStatus { get; set; } = string.Empty;
    public int ConflictCount { get; set; }
    public List<ConflictSummary> ConflictDetails { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public class DatabaseDiagnostics
{
    public string DatabasePath { get; set; } = string.Empty;
    public bool DatabaseExists { get; set; }
    public string DatabaseSize { get; set; } = string.Empty;
    public DateTime? Created { get; set; }
    public DateTime? LastModified { get; set; }
    public bool IsInitialized { get; set; }
    public int TotalFeatures { get; set; }
    public int PendingChanges { get; set; }
    public int LayerCount { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ConflictSummary
{
    public string ConflictId { get; set; } = string.Empty;
    public string LayerName { get; set; } = string.Empty;
    public string ConflictType { get; set; } = string.Empty;
}

public class MemoryInfo
{
    public double UsedMemoryMB { get; set; }
    public double AvailableMemoryMB { get; set; }
}

public class DiagnosticReport
{
    public DateTime GeneratedAt { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public SystemDiagnostics System { get; set; } = new();
    public ConnectivityDiagnostics Connectivity { get; set; } = new();
    public SyncDiagnostics Sync { get; set; } = new();
    public DatabaseDiagnostics Database { get; set; } = new();
}

#endregion
