using SQLite;
using System.Text.Json;
using NetTopologySuite.IO;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage.Models;
using ChangeOperation = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeOperation;
using CoreModels = Honua.Mobile.FieldCollection.Models;
using StorageSpatialRelationship = Honua.Mobile.FieldCollection.Services.Storage.Models.SpatialRelationship;
using StorageSyncStatus = Honua.Mobile.FieldCollection.Services.Storage.Models.SyncStatus;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace Honua.Mobile.FieldCollection.Services.Storage;

/// <summary>
/// OGC GeoPackage-compliant storage service for offline field data collection
/// Implements SQLite-based spatial database with change tracking for delta sync
/// </summary>
public class GeoPackageStorageService : IDisposable
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
        await EnsureLocalFeaturesTableAsync();
        await _connection.CreateTableAsync<ChangeRecord>();
        await _connection.CreateTableAsync<SyncSession>();
        await _connection.CreateTableAsync<ConflictRecord>();
        await _connection.CreateTableAsync<LayerMetadata>();
    }

    private async Task EnsureLocalFeaturesTableAsync()
    {
        var hasTable = await _connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'local_features'");

        if (hasTable == 0)
        {
            await CreateLocalFeaturesTableAsync("local_features");
        }
        else if (!await LocalFeaturesTableUsesStorageKeyAsync())
        {
            await MigrateLocalFeaturesTableAsync();
        }

        await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_local_features_id_layer_id ON local_features(id, layer_id)");
        await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_local_features_layer_id ON local_features(layer_id)");
        await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_local_features_modified_at ON local_features(modified_at)");
        await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_local_features_sync_status ON local_features(sync_status)");
    }

    private async Task<bool> LocalFeaturesTableUsesStorageKeyAsync()
    {
        var columns = await _connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(local_features)");
        return columns.Any(column =>
            string.Equals(column.Name, "storage_key", StringComparison.OrdinalIgnoreCase) &&
            column.PrimaryKey > 0);
    }

    private async Task MigrateLocalFeaturesTableAsync()
    {
        const string migrationTable = "local_features_migration";

        await _connection.ExecuteAsync("BEGIN IMMEDIATE");
        try
        {
            await _connection.ExecuteAsync($"DROP TABLE IF EXISTS {migrationTable}");
            await CreateLocalFeaturesTableAsync(migrationTable);
            await _connection.ExecuteAsync($@"
                INSERT OR REPLACE INTO {migrationTable}
                    (storage_key, id, layer_id, geometry, attributes, created_at, modified_at, version, sync_status, server_version, conflict_resolution)
                SELECT
                    CAST(layer_id AS TEXT) || ':' || id,
                    id,
                    layer_id,
                    geometry,
                    attributes,
                    created_at,
                    modified_at,
                    version,
                    sync_status,
                    server_version,
                    conflict_resolution
                FROM local_features");
            await _connection.ExecuteAsync("DROP TABLE local_features");
            await _connection.ExecuteAsync($"ALTER TABLE {migrationTable} RENAME TO local_features");
            await _connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await _connection.ExecuteAsync("ROLLBACK");
            throw;
        }
    }

    private Task CreateLocalFeaturesTableAsync(string tableName)
    {
        return _connection.ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {tableName} (
                storage_key TEXT NOT NULL PRIMARY KEY,
                id TEXT NOT NULL,
                layer_id INTEGER NOT NULL,
                geometry BLOB,
                attributes TEXT,
                created_at DATETIME NOT NULL,
                modified_at DATETIME NOT NULL,
                version INTEGER NOT NULL,
                sync_status INTEGER NOT NULL,
                server_version INTEGER,
                conflict_resolution TEXT
            )");
    }

    private async Task CreateSpatialIndexes()
    {
        await _connection.ExecuteAsync("DROP TRIGGER IF EXISTS local_features_geom_insert");
        await _connection.ExecuteAsync("DROP TRIGGER IF EXISTS local_features_geom_update");
        await _connection.ExecuteAsync("DROP TABLE IF EXISTS idx_local_features_geom");
    }

    #region Feature Storage

    public async Task<string> StoreFeatureAsync(Feature feature)
    {
        await _dbLock.WaitAsync();
        try
        {
            return await SaveFeatureAsync(feature, StorageSyncStatus.PendingUpload, trackChange: true, ChangeOperation.Insert);
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
            var localFeatures = await _connection.Table<LocalFeature>()
                .Where(f => f.LayerId == layerId)
                .ToListAsync();

            if (spatialQuery != null)
            {
                localFeatures = localFeatures
                    .Where(feature => MatchesSpatialQuery(feature, spatialQuery))
                    .ToList();

                if (spatialQuery.MaxResults.HasValue)
                {
                    localFeatures = localFeatures.Take(spatialQuery.MaxResults.Value).ToList();
                }
            }

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

            await SaveFeatureAsync(feature, StorageSyncStatus.PendingUpload, trackChange: true, ChangeOperation.Update);

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

    public async Task<string> ApplyRemoteFeatureAsync(Feature feature)
    {
        await _dbLock.WaitAsync();
        try
        {
            return await SaveFeatureAsync(feature, StorageSyncStatus.Synced, trackChange: false, ChangeOperation.Update);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> ApplyRemoteDeleteAsync(string featureId, int layerId)
    {
        await _dbLock.WaitAsync();
        try
        {
            var deleted = await _connection.Table<LocalFeature>()
                .DeleteAsync(f => f.Id == featureId && f.LayerId == layerId);

            return deleted > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async Task<string> SaveFeatureAsync(
        Feature feature,
        StorageSyncStatus syncStatus,
        bool trackChange,
        ChangeOperation operation)
    {
        if (string.IsNullOrWhiteSpace(feature.Id))
        {
            feature.Id = Guid.NewGuid().ToString();
        }

        var now = DateTime.UtcNow;
        if (feature.CreatedAt == default)
        {
            feature.CreatedAt = now;
        }

        feature.ModifiedAt ??= feature.UpdatedAt ?? now;
        feature.UpdatedAt ??= feature.ModifiedAt;

        if (feature.Version <= 0)
        {
            feature.Version = 1;
        }

        var geometry = ConvertToNtsGeometry(feature.Geometry);
        var localFeature = new LocalFeature
        {
            StorageKey = BuildStorageKey(feature.LayerId, feature.Id),
            Id = feature.Id,
            LayerId = feature.LayerId,
            Geometry = geometry != null ? _wkbWriter.Write(geometry) : null,
            Attributes = JsonSerializer.Serialize(feature.Attributes),
            CreatedAt = feature.CreatedAt,
            ModifiedAt = feature.ModifiedAt.Value,
            Version = feature.Version,
            SyncStatus = syncStatus
        };

        await _connection.InsertOrReplaceAsync(localFeature);

        if (trackChange)
        {
            await RecordChange(feature.Id, feature.LayerId, operation);
        }

        return feature.Id;
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
            SyncStatus = StorageSyncStatus.PendingUpload
        };

        await _connection.InsertAsync(changeRecord);
    }

    public async Task<List<ChangeRecord>> GetPendingChangesAsync(int? layerId = null)
    {
        await _dbLock.WaitAsync();
        try
        {
            var query = _connection.Table<ChangeRecord>()
                .Where(c => c.SyncStatus == StorageSyncStatus.PendingUpload);

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
                    StorageSyncStatus.Synced, changeId);
            }
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Spatial Queries

    private bool MatchesSpatialQuery(LocalFeature feature, SpatialQuery spatialQuery)
    {
        if (feature.Geometry == null || !spatialQuery.Bounds.IsValid)
        {
            return false;
        }

        var geometry = _wkbReader.Read(feature.Geometry);
        var bounds = spatialQuery.Bounds;
        var queryGeometry = geometry.Factory.ToGeometry(new NtsEnvelope(
            bounds.MinX,
            bounds.MaxX,
            bounds.MinY,
            bounds.MaxY));

        return spatialQuery.Relationship switch
        {
            StorageSpatialRelationship.Intersects => geometry.Intersects(queryGeometry),
            StorageSpatialRelationship.Contains => geometry.Contains(queryGeometry),
            StorageSpatialRelationship.Within => geometry.Within(queryGeometry),
            StorageSpatialRelationship.Overlaps => geometry.Overlaps(queryGeometry),
            StorageSpatialRelationship.Touches => geometry.Touches(queryGeometry),
            StorageSpatialRelationship.Crosses => geometry.Crosses(queryGeometry),
            _ => geometry.Intersects(queryGeometry)
        };
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
            UpdatedAt = localFeature.ModifiedAt,
            Version = localFeature.Version,
            IsPendingSync = localFeature.SyncStatus == StorageSyncStatus.PendingUpload
        };
    }

    private LayerInfo ConvertToLayerInfo(LayerMetadata metadata)
    {
        return new LayerInfo
        {
            Id = metadata.Id,
            Name = metadata.Name,
            Description = metadata.Description,
            GeometryType = Enum.Parse<CoreModels.GeometryType>(metadata.GeometryType),
            IsEditable = metadata.IsEditable,
            IsVisible = true,
            Schema = JsonSerializer.Deserialize<List<FieldDefinition>>(metadata.Schema ?? "[]") ?? new()
        };
    }

    private static NtsGeometry? ConvertToNtsGeometry(CoreModels.Geometry? geometry)
    {
        return geometry switch
        {
            CoreModels.Point point => new NetTopologySuite.Geometries.Point(point.Longitude, point.Latitude),
            CoreModels.LineString line => new NetTopologySuite.Geometries.GeometryFactory(
                    new NetTopologySuite.Geometries.PrecisionModel(),
                    line.SRID)
                .CreateLineString(line.Coordinates
                    .Select(point => new NetTopologySuite.Geometries.Coordinate(point.Longitude, point.Latitude))
                    .ToArray()),
            CoreModels.Polygon polygon => CreateNtsPolygon(polygon),
            null => null,
            _ => throw new NotSupportedException($"Geometry type {geometry.Type} not supported")
        };
    }

    private static NetTopologySuite.Geometries.Polygon CreateNtsPolygon(CoreModels.Polygon polygon)
    {
        var factory = new NetTopologySuite.Geometries.GeometryFactory(
            new NetTopologySuite.Geometries.PrecisionModel(),
            polygon.SRID);
        var shellCoordinates = polygon.Coordinates.FirstOrDefault() ?? [];
        var coordinates = shellCoordinates
            .Select(point => new NetTopologySuite.Geometries.Coordinate(point.Longitude, point.Latitude))
            .ToList();

        if (coordinates.Count > 0 && !coordinates[0].Equals2D(coordinates[^1]))
        {
            coordinates.Add(coordinates[0]);
        }

        return factory.CreatePolygon(coordinates.ToArray());
    }

    private static CoreModels.Geometry? ConvertFromNtsGeometry(NtsGeometry ntsGeometry)
    {
        if (ntsGeometry is NetTopologySuite.Geometries.Point point)
        {
            return new CoreModels.Point
            {
                Latitude = point.Y,
                Longitude = point.X
            };
        }

        if (ntsGeometry is NetTopologySuite.Geometries.LineString line)
        {
            return new CoreModels.LineString
            {
                Coordinates = line.Coordinates
                    .Select(point => new CoreModels.Point(point.Y, point.X))
                    .ToList()
            };
        }

        if (ntsGeometry is NetTopologySuite.Geometries.Polygon polygon)
        {
            return new CoreModels.Polygon
            {
                Coordinates =
                [
                    polygon.ExteriorRing.Coordinates
                        .Select(point => new CoreModels.Point(point.Y, point.X))
                        .ToList()
                ]
            };
        }

        throw new NotSupportedException($"Geometry type {ntsGeometry.GeometryType} not supported");
    }

    private static string BuildStorageKey(int layerId, string featureId)
    {
        return $"{layerId}:{featureId}";
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
                .CountAsync(c => c.SyncStatus == StorageSyncStatus.PendingUpload);

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

internal sealed class TableColumnInfo
{
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("pk")]
    public int PrimaryKey { get; set; }
}
