using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.Services.Sync;

namespace Honua.Mobile.FieldCollection.Services.Storage;

/// <summary>
/// Database service factory for creating and managing GeoPackage database instances
/// Handles platform-specific database path resolution and initialization
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly string _databasePath;
    private GeoPackageStorageService? _storageService;
    private GeoPackageSyncService? _syncService;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public DatabaseService()
    {
        _databasePath = GetDatabasePath();
    }

    public async Task<GeoPackageStorageService> GetStorageServiceAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            return await GetOrCreateStorageServiceAsync();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<GeoPackageSyncService> GetSyncServiceAsync(
        IAuthenticationService authService,
        IConnectivityService connectivityService)
    {
        await _initLock.WaitAsync();
        try
        {
            if (_syncService == null)
            {
                var storageService = await GetOrCreateStorageServiceAsync();
                _syncService = new GeoPackageSyncService(storageService, authService, connectivityService);
            }

            return _syncService;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<GeoPackageStorageService> GetOrCreateStorageServiceAsync()
    {
        if (_storageService == null)
        {
            _storageService = new GeoPackageStorageService(_databasePath);
            await _storageService.InitializeAsync();
            _initialized = true;
        }

        return _storageService;
    }

    public async Task<bool> IsInitializedAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            return _initialized;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<DatabaseInfo> GetDatabaseInfoAsync()
    {
        var fileInfo = new FileInfo(_databasePath);
        var exists = fileInfo.Exists;
        var sizeBytes = exists ? fileInfo.Length : 0;
        var created = exists ? fileInfo.CreationTimeUtc : (DateTime?)null;
        var modified = exists ? fileInfo.LastWriteTimeUtc : (DateTime?)null;

        DatabaseStatistics? stats = null;
        if (_storageService != null)
        {
            var storageStats = await _storageService.GetStorageStatisticsAsync();
            stats = new DatabaseStatistics
            {
                TotalFeatures = storageStats.TotalFeatures,
                PendingChanges = storageStats.PendingChanges,
                DatabaseSizeMb = storageStats.DatabaseSizeMb
            };
        }

        return new DatabaseInfo
        {
            DatabasePath = _databasePath,
            Exists = exists,
            SizeBytes = sizeBytes,
            Created = created,
            Modified = modified,
            Statistics = stats
        };
    }

    public async Task<bool> CompactDatabaseAsync()
    {
        try
        {
            if (_storageService != null)
            {
                // SQLite VACUUM operation to compact database
                // This would be implemented in the storage service
                await Task.Delay(1000); // Simulate compaction
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> ResetDatabaseAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            // Close existing connections
            _syncService?.Dispose();
            _storageService?.Dispose();
            _syncService = null;
            _storageService = null;

            // Delete database file
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }

            _initialized = false;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static string GetDatabasePath()
    {
        // Platform-specific database path resolution
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var databaseDirectory = Path.Combine(appDataPath, "Honua", "FieldCollection");

        // Ensure directory exists
        Directory.CreateDirectory(databaseDirectory);

        return Path.Combine(databaseDirectory, "honua_field_collection.gpkg");
    }

    public void Dispose()
    {
        _syncService?.Dispose();
        _storageService?.Dispose();
        _initLock?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Database information and statistics
/// </summary>
public class DatabaseInfo
{
    public string DatabasePath { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public long SizeBytes { get; set; }
    public DateTime? Created { get; set; }
    public DateTime? Modified { get; set; }
    public DatabaseStatistics? Statistics { get; set; }

    public string SizeMB => $"{SizeBytes / (1024.0 * 1024.0):F2} MB";
}

/// <summary>
/// Database usage statistics
/// </summary>
public class DatabaseStatistics
{
    public int TotalFeatures { get; set; }
    public int PendingChanges { get; set; }
    public double DatabaseSizeMb { get; set; }
    public DateTime? LastCompaction { get; set; }
    public int LayerCount { get; set; }
    public int SyncSessionCount { get; set; }
}

/// <summary>
/// Database configuration options
/// </summary>
public class DatabaseOptions
{
    public string DatabaseName { get; set; } = "honua_field_collection.gpkg";
    public bool EnableWAL { get; set; } = true;
    public bool EnableForeignKeys { get; set; } = true;
    public int CacheSize { get; set; } = 2000; // Pages
    public int BusyTimeout { get; set; } = 30000; // Milliseconds
    public bool EnableSpatialIndex { get; set; } = true;
}
