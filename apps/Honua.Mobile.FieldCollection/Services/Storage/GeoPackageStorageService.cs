using SQLite;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage.Models;

namespace Honua.Mobile.FieldCollection.Services.Storage;

/// <summary>
/// OGC GeoPackage-compliant storage service for offline field data collection
/// Implements SQLite-based spatial database with change tracking for delta sync
/// </summary>
public class GeoPackageStorageService : IStorageService, IDisposable
{
    private readonly SQLiteAsyncConnection _connection;
    private readonly WKBWriter _wkbWriter;
    private readonly WKBReader _wkbReader;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _dbLock = new(1, 1);

    public GeoPackageStorageService(string databasePath)
    {
        _databasePath = databasePath;
        _connection = new SQLiteAsyncConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
        _wkbWriter = new WKBWriter();
        _wkbReader = new WKBReader();

        InitializeDatabase().Wait();
    }

    public async Task<bool> InitializeAsync()
    {
        await _dbLock.WaitAsync();
        try
        {
            await InitializeDatabase();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async Task InitializeDatabase()
    {
        // Create OGC GeoPackage required tables
        await CreateGeoPackageCoreTables();

        // Create Honua-specific tables for change tracking
        await CreateChangeTrackingTables();

        // Create spatial indexes
        await CreateSpatialIndexes();
    }

    private async Task CreateGeoPackageCoreTables()
    {
        // OGC GeoPackage specification tables
        await _connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS gpkg_spatial_ref_sys (
                srs_name TEXT NOT NULL,
                srs_id INTEGER NOT NULL PRIMARY KEY,
                organization TEXT NOT NULL,
                organization_coordsys_id INTEGER NOT NULL,
                definition TEXT NOT NULL,
                description TEXT
            )");

        await _connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS gpkg_contents (
                table_name TEXT NOT NULL PRIMARY KEY,
                data_type TEXT NOT NULL,
                identifier TEXT UNIQUE,
                description TEXT DEFAULT '',
                last_change DATETIME NOT NULL DEFAULT (datetime('now','localtime')),
                min_x REAL,
                min_y REAL,
                max_x REAL,
                max_y REAL,
                srs_id INTEGER,
                CONSTRAINT fk_gc_r_srs_id FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
            )");

        await _connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS gpkg_geometry_columns (
                table_name TEXT NOT NULL,
                column_name TEXT NOT NULL,
                geometry_type_name TEXT NOT NULL,
                srs_id INTEGER NOT NULL,
                z TINYINT NOT NULL,
                m TINYINT NOT NULL,
                CONSTRAINT pk_geom_cols PRIMARY KEY (table_name, column_name),
                CONSTRAINT uk_gc_table_name UNIQUE (table_name),
                CONSTRAINT fk_gc_tn FOREIGN KEY (table_name) REFERENCES gpkg_contents(table_name),
                CONSTRAINT fk_gc_srs FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
            )");

        // Insert WGS84 spatial reference system
        await _connection.ExecuteAsync(@"
            INSERT OR IGNORE INTO gpkg_spatial_ref_sys
            (srs_name, srs_id, organization, organization_coordsys_id, definition, description)
            VALUES ('WGS 84', 4326, 'EPSG', 4326,
                'GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563,AUTHORITY[""EPSG"",""7030""]],AUTHORITY[""EPSG"",""6326""]],PRIMEM[""Greenwich"",0,AUTHORITY[""EPSG"",""8901""]],UNIT[""degree"",0.01745329251994328,AUTHORITY[""EPSG"",""9122""]],AUTHORITY[""EPSG"",""4326""]]',
                'longitude/latitude coordinates in decimal degrees on the WGS 84 spheroid')");
    }

    private async Task CreateChangeTrackingTables()
    {
        // Honua change tracking for delta sync
        await _connection.CreateTableAsync<LocalFeature>();
        await _connection.CreateTableAsync<ChangeRecord>();
        await _connection.CreateTableAsync<SyncSession>();
        await _connection.CreateTableAsync<ConflictRecord>();
        await _connection.CreateTableAsync<LayerMetadata>();
    }

    private async Task CreateSpatialIndexes()
    {
        // Create spatial index using SQLite R*Tree
        await _connection.ExecuteAsync(@"
            CREATE VIRTUAL TABLE IF NOT EXISTS idx_local_features_geom USING rtree(
                id INTEGER PRIMARY KEY,
                minx REAL, maxx REAL,
                miny REAL, maxy REAL
            )");

        // Trigger to maintain spatial index
        await _connection.ExecuteAsync(@"
            CREATE TRIGGER IF NOT EXISTS local_features_geom_insert
            AFTER INSERT ON local_features
            WHEN NEW.geometry IS NOT NULL
            BEGIN
                INSERT INTO idx_local_features_geom VALUES (
                    NEW.id,
                    ST_MinX(NEW.geometry), ST_MaxX(NEW.geometry),
                    ST_MinY(NEW.geometry), ST_MaxY(NEW.geometry)
                );
            END");

        await _connection.ExecuteAsync(@"
            CREATE TRIGGER IF NOT EXISTS local_features_geom_update
            AFTER UPDATE OF geometry ON local_features
            WHEN NEW.geometry IS NOT NULL
            BEGIN
                UPDATE idx_local_features_geom SET
                    minx = ST_MinX(NEW.geometry), maxx = ST_MaxX(NEW.geometry),
                    miny = ST_MinY(NEW.geometry), maxy = ST_MaxY(NEW.geometry)
                WHERE id = NEW.id;
            END");
    }

    #region Feature Storage

    public async Task<string> StoreFeatureAsync(Feature feature)
    {
        await _dbLock.WaitAsync();
        try
        {
            var localFeature = new LocalFeature
            {
                Id = feature.Id,
                LayerId = feature.LayerId,
                Geometry = _wkbWriter.Write(ConvertToNtsGeometry(feature.Geometry)),
                Attributes = JsonSerializer.Serialize(feature.Attributes),
                CreatedAt = feature.CreatedAt,
                ModifiedAt = feature.ModifiedAt ?? DateTime.UtcNow,
                Version = feature.Version,
                SyncStatus = SyncStatus.PendingUpload
            };

            await _connection.InsertOrReplaceAsync(localFeature);

            // Track change for delta sync
            await RecordChange(feature.Id, feature.LayerId, ChangeOperation.Insert);

            return feature.Id;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<Feature?> GetFeatureAsync(string featureId, int layerId)
    {
        await _dbLock.WaitAsync();
        try
        {
            var localFeature = await _connection.Table<LocalFeature>()
                .FirstOrDefaultAsync(f => f.Id == featureId && f.LayerId == layerId);

            return localFeature != null ? ConvertToFeature(localFeature) : null;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<Feature>> QueryFeaturesAsync(int layerId, SpatialQuery? spatialQuery = null)
    {
        await _dbLock.WaitAsync();
        try
        {
            var query = _connection.Table<LocalFeature>().Where(f => f.LayerId == layerId);

            if (spatialQuery != null)
            {
                // Use spatial index for efficient querying
                var spatialIds = await GetFeaturesInBounds(spatialQuery.Bounds);
                query = query.Where(f => spatialIds.Contains(f.Id));
            }

            var localFeatures = await query.ToListAsync();
            return localFeatures.Select(ConvertToFeature).ToList();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> UpdateFeatureAsync(Feature feature)
    {
        await _dbLock.WaitAsync();
        try
        {
            var existing = await _connection.Table<LocalFeature>()
                .FirstOrDefaultAsync(f => f.Id == feature.Id && f.LayerId == feature.LayerId);

            if (existing == null) return false;

            existing.Geometry = feature.Geometry != null ? _wkbWriter.Write(ConvertToNtsGeometry(feature.Geometry)) : null;
            existing.Attributes = JsonSerializer.Serialize(feature.Attributes);
            existing.ModifiedAt = DateTime.UtcNow;
            existing.Version = feature.Version;
            existing.SyncStatus = SyncStatus.PendingUpload;

            await _connection.UpdateAsync(existing);
            await RecordChange(feature.Id, feature.LayerId, ChangeOperation.Update);

            return true;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> DeleteFeatureAsync(string featureId, int layerId)
    {
        await _dbLock.WaitAsync();
        try
        {
            var deleted = await _connection.Table<LocalFeature>()
                .DeleteAsync(f => f.Id == featureId && f.LayerId == layerId);

            if (deleted > 0)
            {
                await RecordChange(featureId, layerId, ChangeOperation.Delete);
                return true;
            }

            return false;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Change Tracking

    private async Task RecordChange(string featureId, int layerId, ChangeOperation operation)
    {
        var changeRecord = new ChangeRecord
        {
            Id = Guid.NewGuid().ToString(),
            FeatureId = featureId,
            LayerId = layerId,
            Operation = operation,
            Timestamp = DateTime.UtcNow,
            SyncStatus = SyncStatus.PendingUpload
        };

        await _connection.InsertAsync(changeRecord);
    }

    public async Task<List<ChangeRecord>> GetPendingChangesAsync(int? layerId = null)
    {
        await _dbLock.WaitAsync();
        try
        {
            var query = _connection.Table<ChangeRecord>()
                .Where(c => c.SyncStatus == SyncStatus.PendingUpload);

            if (layerId.HasValue)
            {
                query = query.Where(c => c.LayerId == layerId.Value);
            }

            return await query.OrderBy(c => c.Timestamp).ToListAsync();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkChangesAsSynced(List<string> changeIds)
    {
        await _dbLock.WaitAsync();
        try
        {
            foreach (var changeId in changeIds)
            {
                await _connection.ExecuteAsync(
                    "UPDATE change_records SET sync_status = ? WHERE id = ?",
                    SyncStatus.Synced, changeId);
            }
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Spatial Queries

    private async Task<List<string>> GetFeaturesInBounds(BoundingBox bounds)
    {
        var sql = @"
            SELECT f.id FROM local_features f
            INNER JOIN idx_local_features_geom idx ON f.id = idx.id
            WHERE idx.minx <= ? AND idx.maxx >= ? AND idx.miny <= ? AND idx.maxy >= ?";

        var results = await _connection.QueryAsync<dynamic>(sql,
            bounds.MaxX, bounds.MinX, bounds.MaxY, bounds.MinY);

        return results.Select(r => (string)r.id).ToList();
    }

    #endregion

    #region Layer Management

    public async Task<bool> CreateLayerAsync(LayerInfo layer)
    {
        await _dbLock.WaitAsync();
        try
        {
            var metadata = new LayerMetadata
            {
                Id = layer.Id,
                Name = layer.Name,
                Description = layer.Description,
                GeometryType = layer.GeometryType.ToString(),
                SpatialReference = "EPSG:4326",
                IsEditable = layer.IsEditable,
                Schema = JsonSerializer.Serialize(layer.Schema),
                CreatedAt = DateTime.UtcNow
            };

            await _connection.InsertOrReplaceAsync(metadata);

            // Register in GeoPackage contents
            var content = new GpkgContent
            {
                TableName = $"layer_{layer.Id}",
                DataType = "features",
                Identifier = layer.Name,
                Description = layer.Description,
                LastChange = DateTime.UtcNow,
                SrsId = 4326
            };

            await _connection.InsertOrReplaceAsync(content);

            return true;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<LayerInfo>> GetLayersAsync()
    {
        await _dbLock.WaitAsync();
        try
        {
            var layers = await _connection.Table<LayerMetadata>().ToListAsync();
            return layers.Select(ConvertToLayerInfo).ToList();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Conversion Helpers

    private Feature ConvertToFeature(LocalFeature localFeature)
    {
        return new Feature
        {
            Id = localFeature.Id,
            LayerId = localFeature.LayerId,
            Geometry = localFeature.Geometry != null ? ConvertFromNtsGeometry(_wkbReader.Read(localFeature.Geometry)) : null,
            Attributes = JsonSerializer.Deserialize<Dictionary<string, object>>(localFeature.Attributes ?? "{}") ?? new(),
            CreatedAt = localFeature.CreatedAt,
            ModifiedAt = localFeature.ModifiedAt,
            Version = localFeature.Version,
            IsPendingSync = localFeature.SyncStatus == SyncStatus.PendingUpload
        };
    }

    private LayerInfo ConvertToLayerInfo(LayerMetadata metadata)
    {
        return new LayerInfo
        {
            Id = metadata.Id,
            Name = metadata.Name,
            Description = metadata.Description,
            GeometryType = Enum.Parse<GeometryType>(metadata.GeometryType),
            IsEditable = metadata.IsEditable,
            IsVisible = true,
            Schema = JsonSerializer.Deserialize<List<FieldDefinition>>(metadata.Schema ?? "[]") ?? new()
        };
    }

    private Geometry ConvertToNtsGeometry(Models.Point point)
    {
        return new NetTopologySuite.Geometries.Point(point.Longitude, point.Latitude);
    }

    private Models.Point ConvertFromNtsGeometry(Geometry ntsGeometry)
    {
        if (ntsGeometry is NetTopologySuite.Geometries.Point point)
        {
            return new Models.Point
            {
                Latitude = point.Y,
                Longitude = point.X
            };
        }

        throw new NotSupportedException($"Geometry type {ntsGeometry.GeometryType} not supported");
    }

    #endregion

    #region Storage Statistics

    public async Task<StorageStatistics> GetStorageStatisticsAsync()
    {
        await _dbLock.WaitAsync();
        try
        {
            var featureCount = await _connection.Table<LocalFeature>().CountAsync();
            var pendingChanges = await _connection.Table<ChangeRecord>()
                .CountAsync(c => c.SyncStatus == SyncStatus.PendingUpload);

            var fileInfo = new FileInfo(_databasePath);
            var databaseSizeMb = fileInfo.Exists ? fileInfo.Length / (1024.0 * 1024.0) : 0;

            return new StorageStatistics
            {
                TotalFeatures = featureCount,
                PendingChanges = pendingChanges,
                DatabaseSizeMb = databaseSizeMb,
                LastCompaction = DateTime.UtcNow // TODO: Track actual compaction time
            };
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _connection?.CloseAsync().Wait();
        _dbLock?.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion
}

public class StorageStatistics
{
    public int TotalFeatures { get; set; }
    public int PendingChanges { get; set; }
    public double DatabaseSizeMb { get; set; }
    public DateTime LastCompaction { get; set; }
}