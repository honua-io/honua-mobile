using SQLite;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetTopologySuite.IO;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Ai;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Metadata;
using Honua.Mobile.FieldCollection.Services.Storage.Models;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Projects;
using ChangeOperation = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeOperation;
using CoreModels = Honua.Mobile.FieldCollection.Models;
using StorageConflictResolution = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictResolution;
using StorageSpatialRelationship = Honua.Mobile.FieldCollection.Services.Storage.Models.SpatialRelationship;
using StorageSyncStatus = Honua.Mobile.FieldCollection.Services.Storage.Models.SyncStatus;
using NtsEnvelope = NetTopologySuite.Geometries.Envelope;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace Honua.Mobile.FieldCollection.Services.Storage;

/// <summary>
/// OGC GeoPackage-compliant storage service for offline field data collection
/// Implements SQLite-based spatial database with change tracking for delta sync
/// </summary>
public class GeoPackageStorageService : IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SchemaJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SQLiteAsyncConnection _connection;
    private readonly WKBWriter _wkbWriter;
    private readonly WKBReader _wkbReader;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private readonly Lazy<Task> _initializationTask;

    public GeoPackageStorageService(string databasePath)
    {
        _databasePath = databasePath;
        _connection = new SQLiteAsyncConnection(databasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
        // emitZ so 3D points/vertices survive the local round-trip; the default WKBWriter writes
        // XY only and would silently drop the Z ordinate even when the geometry carries one.
        _wkbWriter = new WKBWriter(ByteOrder.LittleEndian, handleSRID: false, emitZ: true);
        _wkbReader = new WKBReader();
        _initializationTask = new Lazy<Task>(InitializeDatabase);
    }

    public async Task<bool> InitializeAsync()
    {
        try
        {
            await EnsureInitializedAsync();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Task EnsureInitializedAsync() => _initializationTask.Value;

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
        await EnsureLayerMetadataTableAsync();
        await EnsureProjectCatalogTableAsync();
        await EnsureFieldAssignmentsTableAsync();
        await EnsureLocalAttachmentsTableAsync();
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

        await EnsureLocalFeaturesBboxColumnsAsync();

        await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_local_features_id_layer_id ON local_features(id, layer_id)");
        await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_local_features_layer_id ON local_features(layer_id)");
        await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_local_features_modified_at ON local_features(modified_at)");
        await _connection.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_local_features_sync_status ON local_features(sync_status)");
        // Supports the bounded-query bbox pre-filter (layer-scoped envelope range scan).
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_local_features_layer_bbox ON local_features(layer_id, min_x, max_x, min_y, max_y)");
    }

    /// <summary>
    /// Adds the cached geometry-envelope columns (<c>min_x</c>/<c>min_y</c>/<c>max_x</c>/<c>max_y</c>)
    /// to a pre-existing <c>local_features</c> table and backfills them from stored geometry. New
    /// databases get the columns from <see cref="CreateLocalFeaturesTableAsync"/>; this only runs the
    /// one-time migration for tables created before the spatial pre-filter existed.
    /// </summary>
    private async Task EnsureLocalFeaturesBboxColumnsAsync()
    {
        var columns = await _connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(local_features)");
        var columnNames = columns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var bboxColumn in new[] { "min_x", "min_y", "max_x", "max_y" })
        {
            if (!columnNames.Contains(bboxColumn))
            {
                await _connection.ExecuteAsync($"ALTER TABLE local_features ADD COLUMN {bboxColumn} REAL");
            }
        }

        // Backfill any geometry rows missing a cached envelope. This covers both freshly added
        // columns and rows copied in by a schema migration (which recreates the table with the
        // bbox columns already present, so the ALTER above is a no-op for them). The guard keeps
        // normal startups from rescanning once every row is populated.
        var pendingBackfill = await _connection.ExecuteScalarAsync<int>(
            "SELECT EXISTS(SELECT 1 FROM local_features WHERE geometry IS NOT NULL AND min_x IS NULL)");
        if (pendingBackfill != 0)
        {
            await BackfillLocalFeaturesBboxAsync();
        }
    }

    /// <summary>
    /// Computes and persists the envelope columns for rows that have geometry but no cached bbox
    /// (legacy rows after the column migration). Decodes each WKB once.
    /// </summary>
    private async Task BackfillLocalFeaturesBboxAsync()
    {
        var rows = await _connection.QueryAsync<LocalFeature>(
            "SELECT * FROM local_features WHERE geometry IS NOT NULL AND min_x IS NULL");

        foreach (var row in rows)
        {
            if (row.Geometry is null)
            {
                continue;
            }

            var envelope = _wkbReader.Read(row.Geometry).EnvelopeInternal;
            await _connection.ExecuteAsync(
                "UPDATE local_features SET min_x = ?, min_y = ?, max_x = ?, max_y = ? WHERE storage_key = ?",
                envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY, row.StorageKey);
        }
    }

    private async Task<bool> LocalFeaturesTableUsesStorageKeyAsync()
    {
        var columns = await _connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(local_features)");
        return columns.Any(column =>
            string.Equals(column.Name, "storage_key", StringComparison.OrdinalIgnoreCase) &&
            column.PrimaryKey > 0);
    }

    private async Task EnsureLayerMetadataTableAsync()
    {
        var columns = await _connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(layer_metadata)");
        var columnNames = columns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!columnNames.Contains("service_id"))
        {
            await _connection.ExecuteAsync("ALTER TABLE layer_metadata ADD COLUMN service_id TEXT");
            columnNames.Add("service_id");
        }

        if (!columnNames.Contains("source_id"))
        {
            await _connection.ExecuteAsync("ALTER TABLE layer_metadata ADD COLUMN source_id TEXT");
            columnNames.Add("source_id");
        }

        if (!columnNames.Contains("form_json"))
        {
            await _connection.ExecuteAsync("ALTER TABLE layer_metadata ADD COLUMN form_json TEXT");
            columnNames.Add("form_json");
        }

        if (!columnNames.Contains("storage_key"))
        {
            await MigrateLayerMetadataTableAsync();
        }
    }

    private async Task EnsureLocalAttachmentsTableAsync()
    {
        await _connection.CreateTableAsync<LocalAttachment>();
        var columns = await _connection.QueryAsync<TableColumnInfo>("PRAGMA table_info(local_attachments)");
        var columnNames = columns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!columnNames.Contains("capture_location_json"))
        {
            await _connection.ExecuteAsync("ALTER TABLE local_attachments ADD COLUMN capture_location_json TEXT");
        }

        if (!columnNames.Contains("ai_media_state_json"))
        {
            await _connection.ExecuteAsync("ALTER TABLE local_attachments ADD COLUMN ai_media_state_json TEXT");
        }

        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_local_attachments_feature_layer ON local_attachments(feature_id, layer_id)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_local_attachments_remote ON local_attachments(layer_id, feature_id, remote_attachment_id)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_local_attachments_sync_status ON local_attachments(sync_status)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_local_attachments_deleted ON local_attachments(is_deleted)");
    }

    private async Task EnsureProjectCatalogTableAsync()
    {
        await _connection.CreateTableAsync<LocalFieldProjectCatalogEntry>();
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_project_catalog_service_id ON field_project_catalog(service_id)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_project_catalog_package_id ON field_project_catalog(package_id)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_project_catalog_state ON field_project_catalog(state)");
    }

    private async Task EnsureFieldAssignmentsTableAsync()
    {
        await _connection.CreateTableAsync<LocalFieldAssignmentEntry>();
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_assignments_project_id ON field_assignments(project_id)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_assignments_binding_id ON field_assignments(binding_id)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_assignments_source_id ON field_assignments(source_id)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_assignments_status ON field_assignments(status)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_assignments_assignee ON field_assignments(assignee_user_id)");
        await _connection.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS idx_field_assignments_due_at ON field_assignments(due_at_utc)");
    }

    private async Task MigrateLayerMetadataTableAsync()
    {
        const string migrationTable = "layer_metadata_migration";

        await _connection.ExecuteAsync("BEGIN IMMEDIATE");
        try
        {
            await _connection.ExecuteAsync($"DROP TABLE IF EXISTS {migrationTable}");
            await CreateLayerMetadataTableAsync(migrationTable);
            await _connection.ExecuteAsync($@"
                INSERT OR REPLACE INTO {migrationTable}
                    (storage_key, id, service_id, source_id, name, description, geometry_type, spatial_reference,
                     is_editable, schema, form_json, server_url, created_at, last_sync, sync_enabled)
                SELECT
                    COALESCE(NULLIF(source_id, ''), COALESCE(NULLIF(service_id, ''), 'mobile_offline_demo') || '/FeatureServer/' || id),
                    id,
                    service_id,
                    source_id,
                    name,
                    description,
                    geometry_type,
                    spatial_reference,
                    is_editable,
                    schema,
                    NULL,
                    server_url,
                    created_at,
                    last_sync,
                    sync_enabled
                FROM layer_metadata");
            await _connection.ExecuteAsync("DROP TABLE layer_metadata");
            await _connection.ExecuteAsync($"ALTER TABLE {migrationTable} RENAME TO layer_metadata");
            await _connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            await _connection.ExecuteAsync("ROLLBACK");
            throw;
        }
    }

    private Task CreateLayerMetadataTableAsync(string tableName)
    {
        return _connection.ExecuteAsync($@"
            CREATE TABLE IF NOT EXISTS {tableName} (
                storage_key TEXT NOT NULL PRIMARY KEY,
                id INTEGER NOT NULL,
                service_id TEXT,
                source_id TEXT,
                name TEXT NOT NULL,
                description TEXT NOT NULL,
                geometry_type TEXT NOT NULL,
                spatial_reference TEXT NOT NULL,
                is_editable INTEGER NOT NULL,
                schema TEXT,
                form_json TEXT,
                server_url TEXT,
                created_at TEXT NOT NULL,
                last_sync TEXT,
                sync_enabled INTEGER NOT NULL DEFAULT 1
            )");
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
                min_x REAL,
                min_y REAL,
                max_x REAL,
                max_y REAL,
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
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            // Stamp a durable client-generated GlobalID at capture time so a retried
            // Insert (after a lost upload ack) can be deduped against the server feature
            // instead of creating a duplicate (#305).
            EnsureGlobalId(feature);
            return await SaveFeatureAsync(feature, StorageSyncStatus.PendingUpload, trackChange: true, ChangeOperation.Insert);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<Feature?> GetFeatureAsync(string featureId, int layerId)
    {
        await EnsureInitializedAsync();
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
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            if (spatialQuery is null)
            {
                var all = await _connection.Table<LocalFeature>()
                    .Where(f => f.LayerId == layerId)
                    .ToListAsync();
                return all.Select(ConvertToFeature).ToList();
            }

            if (!spatialQuery.Bounds.IsValid)
            {
                // Preserves prior behaviour: an invalid query window matches nothing.
                return [];
            }

            // Pre-filter to rows whose cached envelope overlaps the query window using an indexed
            // range scan, so we only decode WKB and run the exact NTS predicate on real candidates
            // instead of materialising and testing the whole layer. Inclusive comparisons keep this
            // a correct superset for every supported relationship (including Touches on a shared edge).
            var bounds = spatialQuery.Bounds;
            var candidates = await _connection.QueryAsync<LocalFeature>(
                @"SELECT * FROM local_features
                  WHERE layer_id = ?
                    AND min_x IS NOT NULL
                    AND max_x >= ? AND min_x <= ?
                    AND max_y >= ? AND min_y <= ?",
                layerId, bounds.MinX, bounds.MaxX, bounds.MinY, bounds.MaxY);

            var matches = new List<Feature>();
            foreach (var candidate in candidates)
            {
                if (!MatchesSpatialQuery(candidate, spatialQuery))
                {
                    continue;
                }

                matches.Add(ConvertToFeature(candidate));

                // MaxResults is applied after the exact predicate (the bbox candidate set is a
                // superset, so it cannot be capped earlier without dropping valid matches).
                if (spatialQuery.MaxResults is { } max && matches.Count >= max)
                {
                    break;
                }
            }

            return matches;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> UpdateFeatureAsync(Feature feature)
    {
        await EnsureInitializedAsync();
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
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var deleted = 0;
            await ExecuteInImmediateTransactionAsync(async () =>
            {
                var existing = await _connection.Table<LocalFeature>()
                    .FirstOrDefaultAsync(f => f.Id == featureId && f.LayerId == layerId);
                var existingFeature = existing == null ? null : ConvertToFeature(existing);
                var changeData = existing == null
                    ? null
                    : CreateChangeData(existingFeature!, ChangeOperation.Delete);
                var pendingFeatureChanges = await _connection.Table<ChangeRecord>()
                    .Where(c =>
                        c.FeatureId == featureId &&
                        c.LayerId == layerId &&
                        c.SyncStatus == StorageSyncStatus.PendingUpload)
                    .ToListAsync();
                var collapseLocalOnlyLifecycle = existingFeature != null &&
                    pendingFeatureChanges.Any(change => change.Operation == ChangeOperation.Insert) &&
                    !TryReadObjectId(existingFeature.Attributes, out _);

                deleted = await _connection.Table<LocalFeature>()
                    .DeleteAsync(f => f.Id == featureId && f.LayerId == layerId);

                if (deleted > 0)
                {
                    if (collapseLocalOnlyLifecycle)
                    {
                        foreach (var change in pendingFeatureChanges)
                        {
                            await _connection.ExecuteAsync(
                                "UPDATE change_records SET sync_status = ? WHERE id = ?",
                                StorageSyncStatus.Synced,
                                change.Id);
                        }
                    }
                    else
                    {
                        await RecordChange(featureId, layerId, ChangeOperation.Delete, changeData);
                    }
                }
            });

            return deleted > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<string> ApplyRemoteFeatureAsync(Feature feature)
    {
        await EnsureInitializedAsync();
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
        await EnsureInitializedAsync();
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

    /// <summary>
    /// Attribute carrying the durable client-generated idempotency token used to dedupe
    /// retried Inserts against server features (#305).
    /// </summary>
    internal const string GlobalIdAttribute = "globalid";

    private static readonly string[] GlobalIdFieldCandidates =
        ["globalid", "GlobalID", "GLOBALID", "global_id"];

    private static void EnsureGlobalId(Feature feature)
    {
        foreach (var candidate in GlobalIdFieldCandidates)
        {
            if (feature.Attributes.TryGetValue(candidate, out var existing) &&
                existing is not null &&
                !string.IsNullOrWhiteSpace(existing.ToString()))
            {
                return;
            }
        }

        feature.Attributes[GlobalIdAttribute] = Guid.NewGuid().ToString("N");
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

        // Determine the version/base-version pair that gets persisted.
        //
        // Remote applies (trackChange == false) write the server's authoritative
        // version as both the row Version and its ServerVersion "base" — the row is
        // clean and reconciled to that base.
        //
        // Local edits (trackChange == true) must advance Version PAST the row's base
        // so the dirty row is distinguishable from the last-synced server version.
        // Without this, a pulled-then-edited row keeps the exact version it last
        // synced at and the pull-side conflict guard is structurally dead (#303).
        long baseVersion;
        if (trackChange)
        {
            var existing = await _connection.Table<LocalFeature>()
                .FirstOrDefaultAsync(f => f.Id == feature.Id && f.LayerId == feature.LayerId);

            // The base is the server version we last reconciled this row against.
            // Fall back to the existing row Version (legacy rows without a recorded
            // base) and finally the incoming Version for brand-new inserts.
            baseVersion = existing?.ServerVersion ?? existing?.Version ?? feature.Version;
            if (baseVersion <= 0)
            {
                baseVersion = 1;
            }

            var previousVersion = existing?.Version ?? feature.Version;
            feature.Version = Math.Max(previousVersion, baseVersion) + 1;
        }
        else
        {
            // Server-authoritative write: row reconciled to the incoming version.
            baseVersion = feature.Version;
        }

        feature.BaseVersion = baseVersion;

        var geometry = ConvertToNtsGeometry(feature.Geometry);
        var envelope = geometry?.EnvelopeInternal;
        var localFeature = new LocalFeature
        {
            StorageKey = BuildStorageKey(feature.LayerId, feature.Id),
            Id = feature.Id,
            LayerId = feature.LayerId,
            Geometry = geometry != null ? _wkbWriter.Write(geometry) : null,
            MinX = envelope?.MinX,
            MinY = envelope?.MinY,
            MaxX = envelope?.MaxX,
            MaxY = envelope?.MaxY,
            Attributes = JsonSerializer.Serialize(feature.Attributes),
            CreatedAt = feature.CreatedAt,
            ModifiedAt = feature.ModifiedAt.Value,
            Version = feature.Version,
            ServerVersion = baseVersion,
            SyncStatus = syncStatus
        };

        if (trackChange)
        {
            var changeData = CreateChangeData(feature, operation);
            await ExecuteInImmediateTransactionAsync(async () =>
            {
                await _connection.InsertOrReplaceAsync(localFeature);
                await RecordChange(feature.Id, feature.LayerId, operation, changeData);
            });
        }
        else
        {
            await _connection.InsertOrReplaceAsync(localFeature);
        }

        return feature.Id;
    }

    private async Task ExecuteInImmediateTransactionAsync(Func<Task> operation)
    {
        await _connection.ExecuteAsync("BEGIN IMMEDIATE");
        try
        {
            await operation();
            await _connection.ExecuteAsync("COMMIT");
        }
        catch
        {
            try
            {
                await _connection.ExecuteAsync("ROLLBACK");
            }
            catch
            {
                // Preserve the original write failure.
            }

            throw;
        }
    }

    #endregion

    #region Change Tracking

    private async Task RecordChange(
        string featureId,
        int layerId,
        ChangeOperation operation,
        string? changeData = null)
    {
        var changeRecord = new ChangeRecord
        {
            Id = Guid.NewGuid().ToString(),
            FeatureId = featureId,
            LayerId = layerId,
            Operation = operation,
            Timestamp = DateTime.UtcNow,
            SyncStatus = StorageSyncStatus.PendingUpload,
            ChangeData = changeData
        };

        await _connection.InsertAsync(changeRecord);
    }

    private static string CreateChangeData(Feature feature, ChangeOperation operation)
    {
        JsonElement? geometry = feature.Geometry == null
            ? null
            : GeometryJson.ToJsonElement(feature.Geometry);

        var payload = new
        {
            featureId = feature.Id,
            layerId = feature.LayerId,
            operation = operation.ToString(),
            version = feature.Version,
            objectId = TryReadObjectId(feature.Attributes, out var objectId) ? objectId : (long?)null,
            attributes = feature.Attributes,
            geometry,
            timestamp = DateTime.UtcNow
        };

        return JsonSerializer.Serialize(payload);
    }

    private static bool TryReadObjectId(IReadOnlyDictionary<string, object?> attributes, out long objectId)
    {
        objectId = 0;
        foreach (var attribute in attributes)
        {
            if (!attribute.Key.Equals("objectid", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("objectId", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("FID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryConvertInt64(attribute.Value, out objectId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertInt64(object? value, out long objectId)
    {
        objectId = 0;
        return value switch
        {
            long longValue => Set(longValue, out objectId),
            int intValue => Set(intValue, out objectId),
            double doubleValue when Math.Abs(doubleValue % 1) < double.Epsilon => Set((long)doubleValue, out objectId),
            decimal decimalValue when decimalValue == Math.Truncate(decimalValue) => Set((long)decimalValue, out objectId),
            string stringValue => long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId),
            JsonElement { ValueKind: JsonValueKind.Number } element => element.TryGetInt64(out objectId),
            JsonElement { ValueKind: JsonValueKind.String } element => long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId),
            _ => false
        };

        static bool Set(long value, out long target)
        {
            target = value;
            return true;
        }
    }

    public async Task<List<ChangeRecord>> GetPendingChangesAsync(int? layerId = null)
    {
        await EnsureInitializedAsync();
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
        ArgumentNullException.ThrowIfNull(changeIds);
        if (changeIds.Count == 0)
        {
            return;
        }

        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            // Mark every change in a single transaction so a mid-operation crash cannot leave
            // part of the batch marked Synced while the rest stays PendingUpload. That split state
            // would cause the un-marked changes to be re-uploaded on the next sync, double-applying
            // them server-side. RunInTransactionAsync rolls back on failure, so the batch is
            // all-or-nothing. Each chunk is one set-based UPDATE to stay within the SQLite
            // bound-parameter limit.
            await _connection.RunInTransactionAsync(connection =>
            {
                const int chunkSize = 500;
                for (var offset = 0; offset < changeIds.Count; offset += chunkSize)
                {
                    var chunk = changeIds.GetRange(offset, Math.Min(chunkSize, changeIds.Count - offset));
                    var placeholders = string.Join(",", Enumerable.Repeat("?", chunk.Count));
                    var args = new object[chunk.Count + 1];
                    args[0] = StorageSyncStatus.Synced;
                    for (var i = 0; i < chunk.Count; i++)
                    {
                        args[i + 1] = chunk[i];
                    }

                    connection.Execute(
                        $"UPDATE change_records SET sync_status = ? WHERE id IN ({placeholders})",
                        args);
                }
            });
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Returns the number of change records still queued for upload without materializing the
    /// rows. Used by the pending-changes poll, which only needs the count.
    /// </summary>
    public async Task<int> GetPendingChangesCountAsync()
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            return await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM change_records WHERE sync_status = ?",
                StorageSyncStatus.PendingUpload);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Attachment Storage

    public async Task StoreAttachmentMetadataAsync(AttachmentInfo attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(attachment.Id))
            {
                attachment.Id = Guid.NewGuid().ToString("N");
            }

            if (attachment.CreatedAt == default)
            {
                attachment.CreatedAt = now;
            }

            attachment.UpdatedAt = now;
            await _connection.InsertOrReplaceAsync(ToLocalAttachment(attachment));
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<AttachmentInfo?> GetAttachmentMetadataAsync(string attachmentId)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var attachment = await _connection.Table<LocalAttachment>()
                .FirstOrDefaultAsync(row => row.Id == attachmentId);
            return attachment == null ? null : ToAttachmentInfo(attachment);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<AttachmentInfo?> GetAttachmentByRemoteIdAsync(
        int layerId,
        string featureId,
        long remoteAttachmentId)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var attachment = await _connection.Table<LocalAttachment>()
                .FirstOrDefaultAsync(row =>
                    row.LayerId == layerId &&
                    row.FeatureId == featureId &&
                    row.RemoteAttachmentId == remoteAttachmentId);
            return attachment == null ? null : ToAttachmentInfo(attachment);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<AttachmentInfo>> GetAttachmentsForFeatureAsync(
        string featureId,
        int? layerId = null,
        bool includeDeleted = false)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var query = _connection.Table<LocalAttachment>()
                .Where(row => row.FeatureId == featureId);

            if (layerId.HasValue)
            {
                query = query.Where(row => row.LayerId == layerId.Value);
            }

            var rows = await query.ToListAsync();
            return rows
                .Where(row => includeDeleted || !row.IsDeleted)
                .OrderBy(row => row.CreatedAt)
                .Select(ToAttachmentInfo)
                .ToList();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<AttachmentInfo>> GetPendingAttachmentChangesAsync()
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var rows = await _connection.Table<LocalAttachment>().ToListAsync();
            return rows
                .Where(row => IsPendingAttachmentStatus(row.SyncStatus))
                .OrderBy(row => row.UpdatedAt ?? row.CreatedAt)
                .Select(ToAttachmentInfo)
                .ToList();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkAttachmentUploadedAsync(
        string attachmentId,
        long remoteAttachmentId,
        string? remoteGlobalId,
        DateTime uploadedAt)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.ExecuteAsync(
                """
                UPDATE local_attachments
                SET remote_attachment_id = ?,
                    remote_global_id = ?,
                    uploaded_at = ?,
                    last_synced_at = ?,
                    updated_at = ?,
                    sync_status = ?,
                    retry_count = 0,
                    last_error = NULL,
                    is_deleted = 0,
                    deleted_at = NULL
                WHERE id = ?
                """,
                remoteAttachmentId,
                remoteGlobalId,
                uploadedAt,
                uploadedAt,
                DateTime.UtcNow,
                AttachmentSyncStatus.Synced,
                attachmentId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkAttachmentSyncedAsync(string attachmentId)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            await _connection.ExecuteAsync(
                """
                UPDATE local_attachments
                SET last_synced_at = ?,
                    updated_at = ?,
                    sync_status = ?,
                    retry_count = 0,
                    last_error = NULL
                WHERE id = ?
                """,
                now,
                now,
                AttachmentSyncStatus.Synced,
                attachmentId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkAttachmentPendingDeleteAsync(string attachmentId)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            await _connection.ExecuteAsync(
                """
                UPDATE local_attachments
                SET sync_status = ?,
                    is_deleted = 1,
                    deleted_at = ?,
                    updated_at = ?,
                    last_error = NULL
                WHERE id = ?
                """,
                AttachmentSyncStatus.PendingDelete,
                now,
                now,
                attachmentId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkAttachmentDeletedSyncedAsync(string attachmentId)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            await _connection.ExecuteAsync(
                """
                UPDATE local_attachments
                SET sync_status = ?,
                    is_deleted = 1,
                    deleted_at = COALESCE(deleted_at, ?),
                    last_synced_at = ?,
                    updated_at = ?,
                    retry_count = 0,
                    last_error = NULL
                WHERE id = ?
                """,
                AttachmentSyncStatus.Synced,
                now,
                now,
                now,
                attachmentId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkAttachmentSyncFailedAsync(
        string attachmentId,
        AttachmentSyncStatus failedStatus,
        string errorMessage)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.ExecuteAsync(
                """
                UPDATE local_attachments
                SET sync_status = ?,
                    retry_count = retry_count + 1,
                    last_error = ?,
                    updated_at = ?
                WHERE id = ?
                """,
                failedStatus,
                errorMessage,
                DateTime.UtcNow,
                attachmentId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task UpdateAttachmentAiStateAsync(string attachmentId, MobileAiMediaState? state)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.ExecuteAsync(
                """
                UPDATE local_attachments
                SET ai_media_state_json = ?,
                    updated_at = ?
                WHERE id = ?
                """,
                SerializeAiMediaState(state),
                DateTime.UtcNow,
                attachmentId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> GetPendingAttachmentChangesCountAsync()
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var rows = await _connection.Table<LocalAttachment>().ToListAsync();
            return rows.Count(row => IsPendingAttachmentStatus(row.SyncStatus));
        }
        finally
        {
            _dbLock.Release();
        }
    }

    internal async Task<IReadOnlyList<AttachmentStatusCount>> GetAttachmentStatusCountsAsync()
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            return await _connection.QueryAsync<AttachmentStatusCount>(
                "SELECT sync_status AS sync_status, COUNT(*) AS item_count FROM local_attachments GROUP BY sync_status");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static bool IsPendingAttachmentStatus(AttachmentSyncStatus status)
    {
        return status is AttachmentSyncStatus.PendingUpload
            or AttachmentSyncStatus.UploadFailed
            or AttachmentSyncStatus.PendingDownload
            or AttachmentSyncStatus.DownloadFailed
            or AttachmentSyncStatus.PendingDelete
            or AttachmentSyncStatus.DeleteFailed;
    }

    private static LocalAttachment ToLocalAttachment(AttachmentInfo attachment)
    {
        return new LocalAttachment
        {
            Id = attachment.Id,
            FeatureId = attachment.FeatureId,
            LayerId = attachment.LayerId,
            RemoteAttachmentId = attachment.RemoteAttachmentId,
            RemoteGlobalId = attachment.RemoteGlobalId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            PayloadKind = attachment.PayloadKind,
            SizeBytes = attachment.SizeBytes,
            LocalPath = attachment.LocalPath,
            CreatedAt = attachment.CreatedAt,
            UpdatedAt = attachment.UpdatedAt,
            UploadedAt = attachment.UploadedAt,
            LastSyncedAt = attachment.LastSyncedAt,
            Description = attachment.Description,
            CaptureLocationJson = FieldLocationMetadataMapper.SerializeEvidence(attachment.CaptureLocation),
            ThumbnailUrl = attachment.ThumbnailUrl,
            AiMediaStateJson = SerializeAiMediaState(attachment.AiMediaState),
            SyncStatus = attachment.SyncStatus,
            RetryCount = attachment.RetryCount,
            LastError = attachment.LastError,
            IsDeleted = attachment.IsDeleted,
            DeletedAt = attachment.DeletedAt
        };
    }

    private static AttachmentInfo ToAttachmentInfo(LocalAttachment attachment)
    {
        return new AttachmentInfo
        {
            Id = attachment.Id,
            FeatureId = attachment.FeatureId,
            LayerId = attachment.LayerId,
            RemoteAttachmentId = attachment.RemoteAttachmentId,
            RemoteGlobalId = attachment.RemoteGlobalId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            PayloadKind = attachment.PayloadKind,
            SizeBytes = attachment.SizeBytes,
            LocalPath = attachment.LocalPath,
            CreatedAt = attachment.CreatedAt,
            UpdatedAt = attachment.UpdatedAt,
            UploadedAt = attachment.UploadedAt,
            LastSyncedAt = attachment.LastSyncedAt,
            Description = attachment.Description,
            CaptureLocation = FieldLocationMetadataMapper.DeserializeEvidence(attachment.CaptureLocationJson),
            ThumbnailUrl = attachment.ThumbnailUrl,
            AiMediaState = DeserializeAiMediaState(attachment.AiMediaStateJson),
            SyncStatus = attachment.SyncStatus,
            RetryCount = attachment.RetryCount,
            LastError = attachment.LastError,
            IsDeleted = attachment.IsDeleted,
            DeletedAt = attachment.DeletedAt
        };
    }

    private static string? SerializeAiMediaState(MobileAiMediaState? state)
        => state is null ? null : JsonSerializer.Serialize(state, SchemaJsonOptions);

    private static MobileAiMediaState? DeserializeAiMediaState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MobileAiMediaState>(json, SchemaJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    #endregion

    #region Sync Session History

    public async Task StoreSyncSessionAsync(SyncSession session)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.InsertOrReplaceAsync(session);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task UpdateSyncSessionAsync(SyncSession session)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.InsertOrReplaceAsync(session);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<IReadOnlyList<SyncSession>> GetSyncSessionsAsync(int limit = 50)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var safeLimit = Math.Clamp(limit, 1, 250);
            return await _connection.QueryAsync<SyncSession>(
                """
                SELECT *
                FROM sync_sessions
                ORDER BY COALESCE(end_time, start_time) DESC
                LIMIT ?
                """,
                safeLimit);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region Conflict Tracking

    public async Task StoreConflictAsync(ConflictRecord conflict)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.InsertOrReplaceAsync(conflict);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<List<ConflictRecord>> GetUnresolvedConflictsAsync()
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            return await _connection.QueryAsync<ConflictRecord>(
                "SELECT * FROM conflict_records WHERE resolved_at IS NULL ORDER BY created_at");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<ConflictRecord?> GetConflictAsync(string conflictId)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            return await _connection.Table<ConflictRecord>()
                .Where(conflict => conflict.Id == conflictId)
                .FirstOrDefaultAsync();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkConflictResolvedAsync(
        string conflictId,
        StorageConflictResolution resolution,
        string? resolvedData)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.ExecuteAsync(
                """
                UPDATE conflict_records
                SET resolution = ?, resolved_data = ?, resolved_at = ?
                WHERE id = ?
                """,
                resolution,
                resolvedData,
                DateTime.UtcNow,
                conflictId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task MarkConflictDeferredAsync(string conflictId, string? reason)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.ExecuteAsync(
                """
                UPDATE conflict_records
                SET resolution = ?, resolved_data = ?, resolved_at = NULL
                WHERE id = ? AND resolved_at IS NULL
                """,
                StorageConflictResolution.Manual,
                reason,
                conflictId);
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

    #region Project Catalog

    public async Task UpsertProjectCatalogEntryAsync(FieldProjectCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var projectId = NormalizeProjectId(entry.ProjectId);
        var now = DateTime.UtcNow;
        if (entry.ImportedAtUtc == default)
        {
            entry.ImportedAtUtc = now;
        }

        entry.UpdatedAtUtc = now;
        if (string.IsNullOrWhiteSpace(entry.ServiceId))
        {
            entry.ServiceId = projectId;
        }

        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            entry.Name = projectId;
        }

        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.InsertOrReplaceAsync(ToLocalProjectCatalogEntry(entry, projectId));
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<FieldProjectCatalogEntry?> GetProjectCatalogEntryAsync(string projectId)
    {
        projectId = NormalizeProjectId(projectId);

        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var row = await _connection.Table<LocalFieldProjectCatalogEntry>()
                .FirstOrDefaultAsync(entry => entry.ProjectId == projectId);
            return row == null ? null : ToProjectCatalogEntry(row);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<IReadOnlyList<FieldProjectCatalogEntry>> GetProjectCatalogEntriesAsync(bool includeArchived = false)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var rows = await _connection.Table<LocalFieldProjectCatalogEntry>().ToListAsync();
            return rows
                .Where(row => includeArchived || row.State != FieldProjectCatalogState.Archived)
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ProjectId, StringComparer.OrdinalIgnoreCase)
                .Select(ToProjectCatalogEntry)
                .ToList();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public Task UpdateProjectCatalogStateAsync(
        string projectId,
        FieldProjectCatalogState state,
        DateTime? updatedAtUtc = null)
        => UpdateProjectCatalogFieldsAsync(
            projectId,
            "state = ?",
            [state],
            updatedAtUtc);

    public Task MarkProjectCatalogEntryOpenedAsync(string projectId, DateTime? openedAtUtc = null)
        => UpdateProjectCatalogFieldsAsync(
            projectId,
            "last_opened_at_utc = ?",
            [openedAtUtc ?? DateTime.UtcNow],
            openedAtUtc);

    public Task MarkProjectCatalogValidationAsync(
        string projectId,
        FieldProjectValidationStatus status,
        int issueCount,
        DateTime? validatedAtUtc = null)
        => UpdateProjectCatalogFieldsAsync(
            projectId,
            "validation_status = ?, validation_issue_count = ?, last_validation_at_utc = ?",
            [status, Math.Max(0, issueCount), validatedAtUtc ?? DateTime.UtcNow],
            validatedAtUtc);

    public Task MarkProjectCatalogSimulationRunAsync(string projectId, DateTime? simulatedAtUtc = null)
        => UpdateProjectCatalogFieldsAsync(
            projectId,
            "last_simulation_run_at_utc = ?",
            [simulatedAtUtc ?? DateTime.UtcNow],
            simulatedAtUtc);

    public Task MarkProjectCatalogExportedAsync(string projectId, DateTime? exportedAtUtc = null)
        => UpdateProjectCatalogFieldsAsync(
            projectId,
            "last_export_at_utc = ?",
            [exportedAtUtc ?? DateTime.UtcNow],
            exportedAtUtc);

    public async Task<bool> DeleteProjectCatalogEntryAsync(string projectId)
    {
        projectId = NormalizeProjectId(projectId);

        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            return await _connection.Table<LocalFieldProjectCatalogEntry>()
                .DeleteAsync(entry => entry.ProjectId == projectId) > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private async Task UpdateProjectCatalogFieldsAsync(
        string projectId,
        string assignmentSql,
        IReadOnlyList<object?> values,
        DateTime? updatedAtUtc = null)
    {
        projectId = NormalizeProjectId(projectId);

        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var parameters = values
                .Concat(new object?[] { updatedAtUtc ?? DateTime.UtcNow, projectId })
                .ToArray();
            await _connection.ExecuteAsync(
                $"UPDATE field_project_catalog SET {assignmentSql}, updated_at_utc = ? WHERE project_id = ?",
                parameters);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static string NormalizeProjectId(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("Project ID is required.", nameof(projectId));
        }

        return projectId.Trim();
    }

    private static LocalFieldProjectCatalogEntry ToLocalProjectCatalogEntry(
        FieldProjectCatalogEntry entry,
        string projectId)
    {
        return new LocalFieldProjectCatalogEntry
        {
            ProjectId = projectId,
            ServiceId = string.IsNullOrWhiteSpace(entry.ServiceId) ? projectId : entry.ServiceId.Trim(),
            PackageId = string.IsNullOrWhiteSpace(entry.PackageId) ? null : entry.PackageId.Trim(),
            Version = string.IsNullOrWhiteSpace(entry.Version) ? null : entry.Version.Trim(),
            Name = string.IsNullOrWhiteSpace(entry.Name) ? projectId : entry.Name.Trim(),
            Description = entry.Description,
            State = entry.State,
            ValidationStatus = entry.ValidationStatus,
            ValidationIssueCount = Math.Max(0, entry.ValidationIssueCount),
            LayerCount = Math.Max(0, entry.LayerCount),
            PackageSizeBytes = Math.Max(0, entry.PackageSizeBytes),
            MediaSizeBytes = Math.Max(0, entry.MediaSizeBytes),
            LocalStoragePath = entry.LocalStoragePath,
            ManifestPath = entry.ManifestPath,
            ImportSource = entry.ImportSource,
            PackageDigest = entry.PackageDigest,
            ImportedAtUtc = entry.ImportedAtUtc,
            UpdatedAtUtc = entry.UpdatedAtUtc,
            LastOpenedAtUtc = entry.LastOpenedAtUtc,
            LastValidationAtUtc = entry.LastValidationAtUtc,
            LastSimulationRunAtUtc = entry.LastSimulationRunAtUtc,
            LastExportAtUtc = entry.LastExportAtUtc
        };
    }

    private static FieldProjectCatalogEntry ToProjectCatalogEntry(LocalFieldProjectCatalogEntry row)
    {
        return new FieldProjectCatalogEntry
        {
            ProjectId = row.ProjectId,
            ServiceId = row.ServiceId,
            PackageId = row.PackageId,
            Version = row.Version,
            Name = row.Name,
            Description = row.Description,
            State = row.State,
            ValidationStatus = row.ValidationStatus,
            ValidationIssueCount = row.ValidationIssueCount,
            LayerCount = row.LayerCount,
            PackageSizeBytes = row.PackageSizeBytes,
            MediaSizeBytes = row.MediaSizeBytes,
            LocalStoragePath = row.LocalStoragePath,
            ManifestPath = row.ManifestPath,
            ImportSource = row.ImportSource,
            PackageDigest = row.PackageDigest,
            ImportedAtUtc = row.ImportedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            LastOpenedAtUtc = row.LastOpenedAtUtc,
            LastValidationAtUtc = row.LastValidationAtUtc,
            LastSimulationRunAtUtc = row.LastSimulationRunAtUtc,
            LastExportAtUtc = row.LastExportAtUtc
        };
    }

    #endregion

    #region Field Assignments

    public async Task UpsertFieldTaskPacketsAsync(
        string projectId,
        IReadOnlyList<FieldTaskPacket> taskPackets,
        IReadOnlyDictionary<string, string?> bindingSourceIds)
    {
        projectId = NormalizeProjectId(projectId);
        ArgumentNullException.ThrowIfNull(taskPackets);
        ArgumentNullException.ThrowIfNull(bindingSourceIds);

        var now = DateTime.UtcNow;
        var rows = taskPackets
            .SelectMany(packet => packet.Assignments.Select(assignment =>
                ToLocalFieldAssignmentEntry(projectId, packet.TaskPacketId, assignment, bindingSourceIds, now)))
            .ToList();

        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await ExecuteInImmediateTransactionAsync(async () =>
            {
                foreach (var row in rows)
                {
                    await _connection.InsertOrReplaceAsync(row);
                }
            });
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<IReadOnlyList<LocalFieldAssignmentInfo>> GetFieldAssignmentsAsync(
        LocalFieldAssignmentFilter? filter = null)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var rows = await _connection.Table<LocalFieldAssignmentEntry>().ToListAsync();
            return rows
                .Select(ToFieldAssignmentInfo)
                .Where(assignment => MatchesAssignmentFilter(assignment, filter))
                .OrderBy(assignment => assignment.DueAtUtc ?? DateTimeOffset.MaxValue)
                .ThenByDescending(assignment => assignment.Priority)
                .ThenBy(assignment => assignment.AssignmentId, StringComparer.Ordinal)
                .ToList();
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<bool> UpdateFieldAssignmentStatusAsync(
        string assignmentId,
        FieldAssignmentStatus status,
        DateTime? updatedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(assignmentId))
        {
            throw new ArgumentException("Assignment ID is required.", nameof(assignmentId));
        }

        var updatedAt = updatedAtUtc ?? DateTime.UtcNow;
        var completedAt = status == FieldAssignmentStatus.Complete ? updatedAt : (DateTime?)null;

        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            return await _connection.ExecuteAsync(
                """
                UPDATE field_assignments
                SET status = ?,
                    updated_at_utc = ?,
                    completed_at_utc = ?
                WHERE assignment_id = ?
                """,
                status,
                updatedAt,
                completedAt,
                assignmentId.Trim()) > 0;
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public async Task<int> DeleteFieldAssignmentsForProjectAsync(string projectId)
    {
        projectId = NormalizeProjectId(projectId);

        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            return await _connection.Table<LocalFieldAssignmentEntry>()
                .DeleteAsync(row => row.ProjectId == projectId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static LocalFieldAssignmentEntry ToLocalFieldAssignmentEntry(
        string projectId,
        string taskPacketId,
        FieldAssignment assignment,
        IReadOnlyDictionary<string, string?> bindingSourceIds,
        DateTime now)
    {
        bindingSourceIds.TryGetValue(assignment.BindingId, out var sourceId);

        return new LocalFieldAssignmentEntry
        {
            AssignmentId = assignment.AssignmentId.Trim(),
            TaskPacketId = taskPacketId.Trim(),
            ProjectId = projectId,
            BindingId = assignment.BindingId.Trim(),
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? null : sourceId.Trim(),
            AssigneeUserId = string.IsNullOrWhiteSpace(assignment.AssigneeUserId) ? null : assignment.AssigneeUserId.Trim(),
            CrewId = string.IsNullOrWhiteSpace(assignment.CrewId) ? null : assignment.CrewId.Trim(),
            Priority = assignment.Priority,
            Status = assignment.Status,
            DueAtUtc = assignment.DueAtUtc?.UtcDateTime,
            WorkQueryJson = assignment.WorkQuery is null ? null : JsonSerializer.Serialize(assignment.WorkQuery, SchemaJsonOptions),
            RecordIdsJson = JsonSerializer.Serialize(assignment.RecordIds, SchemaJsonOptions),
            MetadataJson = JsonSerializer.Serialize(assignment.Metadata, SchemaJsonOptions),
            ImportedAtUtc = now,
            UpdatedAtUtc = now,
            CompletedAtUtc = assignment.Status == FieldAssignmentStatus.Complete ? now : null
        };
    }

    private static LocalFieldAssignmentInfo ToFieldAssignmentInfo(LocalFieldAssignmentEntry row)
        => new()
        {
            AssignmentId = row.AssignmentId,
            TaskPacketId = row.TaskPacketId,
            ProjectId = row.ProjectId,
            BindingId = row.BindingId,
            SourceId = row.SourceId,
            AssigneeUserId = row.AssigneeUserId,
            CrewId = row.CrewId,
            Priority = row.Priority,
            Status = row.Status,
            DueAtUtc = row.DueAtUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(row.DueAtUtc.Value, DateTimeKind.Utc))
                : null,
            WorkQuery = DeserializeJson<SourceQuery>(row.WorkQueryJson),
            RecordIds = DeserializeJson<IReadOnlyList<string>>(row.RecordIdsJson) ?? [],
            Metadata = DeserializeJson<IReadOnlyDictionary<string, string>>(row.MetadataJson) ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            ImportedAtUtc = row.ImportedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            CompletedAtUtc = row.CompletedAtUtc
        };

    private static bool MatchesAssignmentFilter(
        LocalFieldAssignmentInfo assignment,
        LocalFieldAssignmentFilter? filter)
    {
        if (filter is null)
        {
            return true;
        }

        return Matches(filter.ProjectId, assignment.ProjectId) &&
            Matches(filter.BindingId, assignment.BindingId) &&
            Matches(filter.SourceId, assignment.SourceId) &&
            Matches(filter.AssigneeUserId, assignment.AssigneeUserId) &&
            Matches(filter.CrewId, assignment.CrewId) &&
            (!filter.Status.HasValue || assignment.Status == filter.Status.Value) &&
            (!filter.MinimumPriority.HasValue || (int)assignment.Priority >= (int)filter.MinimumPriority.Value) &&
            (!filter.DueBeforeUtc.HasValue || assignment.DueAtUtc <= filter.DueBeforeUtc.Value) &&
            Intersects(assignment.WorkQuery?.Bbox, filter.IntersectsExtent);

        static bool Matches(string? expected, string? actual)
            => string.IsNullOrWhiteSpace(expected) ||
                string.Equals(expected.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool Intersects(FeatureBoundingBox? assignmentExtent, FeatureBoundingBox? filterExtent)
    {
        if (filterExtent is null || assignmentExtent is null)
        {
            return true;
        }

        return assignmentExtent.MinX <= filterExtent.MaxX &&
            assignmentExtent.MaxX >= filterExtent.MinX &&
            assignmentExtent.MinY <= filterExtent.MaxY &&
            assignmentExtent.MaxY >= filterExtent.MinY;
    }

    private static T? DeserializeJson<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, SchemaJsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    #endregion

    #region Layer Management

    public async Task<bool> CreateLayerAsync(LayerInfo layer)
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var metadata = new LayerMetadata
            {
                StorageKey = GetLayerMetadataStorageKey(layer),
                Id = layer.Id,
                ServiceId = layer.ServiceId,
                SourceId = layer.SourceId,
                Name = layer.Name,
                Description = layer.Description,
                GeometryType = layer.GeometryType.ToString(),
                SpatialReference = "EPSG:4326",
                IsEditable = layer.IsEditable,
                Schema = JsonSerializer.Serialize(GetLayerSchema(layer), SchemaJsonOptions),
                FormJson = layer.Form is null ? null : JsonSerializer.Serialize(layer.Form, SchemaJsonOptions),
                CreatedAt = DateTime.UtcNow,
                LastSync = DateTime.UtcNow
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
        await EnsureInitializedAsync();
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
            Attributes = JsonSerializer.Deserialize<Dictionary<string, object?>>(localFeature.Attributes ?? "{}") ?? new(),
            CreatedAt = localFeature.CreatedAt,
            ModifiedAt = localFeature.ModifiedAt,
            UpdatedAt = localFeature.ModifiedAt,
            Version = localFeature.Version,
            BaseVersion = localFeature.ServerVersion ?? localFeature.Version,
            IsPendingSync = localFeature.SyncStatus == StorageSyncStatus.PendingUpload
        };
    }

    private LayerInfo ConvertToLayerInfo(LayerMetadata metadata)
    {
        var layer = new LayerInfo
        {
            Id = metadata.Id,
            ServiceId = metadata.ServiceId,
            SourceId = metadata.SourceId,
            Name = metadata.Name,
            Description = metadata.Description,
            GeometryType = ParseGeometryType(metadata.GeometryType),
            IsEditable = metadata.IsEditable,
            IsVisible = true,
            Schema = DeserializeLayerSchema(metadata.Schema)
        };

        layer.Form = DeserializeFormDefinition(metadata.FormJson) ?? FieldCollectionMetadataMapper.CreateFormDefinition(layer);
        return layer;
    }

    private static IReadOnlyList<FormField> GetLayerSchema(LayerInfo layer)
    {
        if (layer.Schema.Count > 0)
        {
            return layer.Schema;
        }

        return layer.Form?.Sections
            .SelectMany(section => section.Fields)
            .ToList() ?? [];
    }

    private static string GetLayerMetadataStorageKey(LayerInfo layer)
    {
        if (!string.IsNullOrWhiteSpace(layer.SourceId))
        {
            return layer.SourceId;
        }

        var serviceId = string.IsNullOrWhiteSpace(layer.ServiceId)
            ? "mobile_offline_demo"
            : layer.ServiceId.Trim();

        return FieldCollectionMetadataMapper.BuildSourceId(serviceId, layer.Id);
    }

    private static GeometryType ParseGeometryType(string? value)
    {
        if (Enum.TryParse<GeometryType>(value, ignoreCase: true, out var geometryType))
        {
            return geometryType;
        }

        return string.Equals(value, "LineString", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "MultiLineString", StringComparison.OrdinalIgnoreCase)
            ? GeometryType.Polyline
            : GeometryType.Unspecified;
    }

    private static List<FieldDefinition> DeserializeLayerSchema(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<FieldDefinition>>(schema, SchemaJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return DeserializeLegacyLayerSchema(schema);
        }
    }

    private static FormDefinition? DeserializeFormDefinition(string? formJson)
    {
        if (string.IsNullOrWhiteSpace(formJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FormDefinition>(formJson, SchemaJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<FieldDefinition> DeserializeLegacyLayerSchema(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var fields = new List<FieldDefinition>();
        foreach (var field in document.RootElement.EnumerateArray())
        {
            var name = ReadString(field, "Name") ?? ReadString(field, "FieldId");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            fields.Add(new FieldDefinition
            {
                FieldId = name,
                SourceFieldName = name,
                Label = ReadString(field, "Label") ?? name,
                HelpText = ReadString(field, "Description"),
                Required = ReadBoolean(field, "Required"),
                Type = MapLegacyFieldType(ReadString(field, "Type")),
                Choices = ReadStringArray(field, "Options")
                    .Select(option => new Honua.Sdk.Field.Forms.FieldChoice { Value = option, Label = option })
                    .ToArray(),
                Validation = new Honua.Sdk.Field.Forms.FieldValidationRule
                {
                    RegexPattern = ReadString(field, "Pattern"),
                    MinNumericValue = ReadDouble(field, "Min"),
                    MaxNumericValue = ReadDouble(field, "Max"),
                    MaxLength = ReadInt(field, "MaxLength")
                }
            });
        }

        return fields;
    }

    private static Honua.Sdk.Field.Forms.FormFieldType MapLegacyFieldType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "number" or "numeric" => Honua.Sdk.Field.Forms.FormFieldType.Numeric,
            "select" or "choice" or "singlechoice" => Honua.Sdk.Field.Forms.FormFieldType.SingleChoice,
            "date" => Honua.Sdk.Field.Forms.FormFieldType.Date,
            "datetime" => Honua.Sdk.Field.Forms.FormFieldType.DateTime,
            "boolean" or "bool" or "yesno" => Honua.Sdk.Field.Forms.FormFieldType.YesNo,
            "photo" or "image" => Honua.Sdk.Field.Forms.FormFieldType.Photo,
            "video" => Honua.Sdk.Field.Forms.FormFieldType.Video,
            "audio" => Honua.Sdk.Field.Forms.FormFieldType.Audio,
            "file" => Honua.Sdk.Field.Forms.FormFieldType.File,
            "location" => Honua.Sdk.Field.Forms.FormFieldType.Location,
            _ => Honua.Sdk.Field.Forms.FormFieldType.Text
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.True;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var value)
                ? value
                : null;
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out var value)
                ? value
                : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
            ? property.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray()
            : [];
    }

    private static NtsGeometry? ConvertToNtsGeometry(CoreModels.Geometry? geometry)
    {
        return geometry switch
        {
            CoreModels.Point point => point.Altitude.HasValue
                ? new NetTopologySuite.Geometries.Point(ToNtsCoordinate(point))
                : new NetTopologySuite.Geometries.Point(point.Longitude, point.Latitude),
            CoreModels.LineString line => new NetTopologySuite.Geometries.GeometryFactory(
                    new NetTopologySuite.Geometries.PrecisionModel(),
                    line.SRID)
                .CreateLineString(line.Coordinates
                    .Select(ToNtsCoordinate)
                    .ToArray()),
            CoreModels.Polygon polygon => CreateNtsPolygon(polygon),
            null => null,
            _ => throw new NotSupportedException($"Geometry type {geometry.Type} not supported")
        };
    }

    // Carry the Z ordinate when an altitude is present so 3D captures are not silently flattened
    // to 2D on the local-storage write path (the WKBWriter is configured with emitZ).
    private static NetTopologySuite.Geometries.Coordinate ToNtsCoordinate(CoreModels.Point point)
        => point.Altitude.HasValue
            ? new NetTopologySuite.Geometries.CoordinateZ(point.Longitude, point.Latitude, point.Altitude.Value)
            : new NetTopologySuite.Geometries.Coordinate(point.Longitude, point.Latitude);

    private static CoreModels.Point ToCorePoint(NetTopologySuite.Geometries.Coordinate coordinate)
        => new(coordinate.Y, coordinate.X, double.IsNaN(coordinate.Z) ? null : coordinate.Z);

    private static NetTopologySuite.Geometries.Polygon CreateNtsPolygon(CoreModels.Polygon polygon)
    {
        var factory = new NetTopologySuite.Geometries.GeometryFactory(
            new NetTopologySuite.Geometries.PrecisionModel(),
            polygon.SRID);

        var shell = CreateNtsLinearRing(factory, polygon.Coordinates.FirstOrDefault() ?? []);
        if (shell is null)
        {
            return factory.CreatePolygon();
        }

        // Preserve interior rings (holes) instead of keeping only the shell, matching the other two
        // geometry serializers in this project (CoreModels.ToGeoJson and FieldCollectionSyncTransports).
        var holes = polygon.Coordinates
            .Skip(1)
            .Select(ring => CreateNtsLinearRing(factory, ring))
            .Where(ring => ring is not null)
            .Cast<NetTopologySuite.Geometries.LinearRing>()
            .ToArray();

        return holes.Length > 0
            ? factory.CreatePolygon(shell, holes)
            : factory.CreatePolygon(shell);
    }

    private static NetTopologySuite.Geometries.LinearRing? CreateNtsLinearRing(
        NetTopologySuite.Geometries.GeometryFactory factory,
        IReadOnlyList<CoreModels.Point> ring)
    {
        var coordinates = ring.Select(ToNtsCoordinate).ToList();
        if (coordinates.Count == 0)
        {
            return null;
        }

        if (!coordinates[0].Equals2D(coordinates[^1]))
        {
            coordinates.Add(coordinates[0].Copy());
        }

        return factory.CreateLinearRing(coordinates.ToArray());
    }

    private static CoreModels.Geometry? ConvertFromNtsGeometry(NtsGeometry ntsGeometry)
    {
        if (ntsGeometry is NetTopologySuite.Geometries.Point point)
        {
            return point.Coordinate is { } coordinate
                ? ToCorePoint(coordinate)
                : new CoreModels.Point { Latitude = point.Y, Longitude = point.X };
        }

        if (ntsGeometry is NetTopologySuite.Geometries.LineString line)
        {
            return new CoreModels.LineString
            {
                Coordinates = line.Coordinates
                    .Select(ToCorePoint)
                    .ToList()
            };
        }

        if (ntsGeometry is NetTopologySuite.Geometries.Polygon polygon)
        {
            var rings = new List<List<CoreModels.Point>>
            {
                polygon.ExteriorRing.Coordinates.Select(ToCorePoint).ToList()
            };

            foreach (var interiorRing in polygon.InteriorRings)
            {
                rings.Add(interiorRing.Coordinates.Select(ToCorePoint).ToList());
            }

            return new CoreModels.Polygon { Coordinates = rings };
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
        await EnsureInitializedAsync();
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

    public async Task<OfflineCacheDiagnostics> GetOfflineCacheDiagnosticsAsync()
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            var fileInfo = new FileInfo(_databasePath);
            var metadataRows = await _connection.Table<LayerMetadata>().ToListAsync();
            var featureRows = await _connection.QueryAsync<LayerFeatureCount>(
                "SELECT layer_id AS layer_id, COUNT(*) AS feature_count FROM local_features GROUP BY layer_id ORDER BY layer_id");
            var operationCounts = await _connection.QueryAsync<OperationStatusCount>(
                "SELECT sync_status AS sync_status, COUNT(*) AS item_count FROM change_records GROUP BY sync_status");
            var attachmentCounts = await _connection.QueryAsync<AttachmentStatusCount>(
                "SELECT sync_status AS sync_status, COUNT(*) AS item_count FROM local_attachments GROUP BY sync_status");
            var unresolvedConflictCount = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM conflict_records WHERE resolved_at IS NULL");
            var conflicts = await _connection.QueryAsync<ConflictRecord>(
                "SELECT * FROM conflict_records WHERE resolved_at IS NULL ORDER BY created_at LIMIT 10");
            var latestSession = await _connection.QueryAsync<SyncSession>(
                "SELECT * FROM sync_sessions ORDER BY COALESCE(end_time, start_time) DESC LIMIT 1");

            var featureCounts = featureRows.ToDictionary(row => row.LayerId, row => row.FeatureCount);
            var metadataSources = metadataRows
                .OrderBy(row => row.Id)
                .Select(row => new OfflineSourceDiagnostics
                {
                    SourceId = row.Id.ToString(CultureInfo.InvariantCulture),
                    DisplayName = string.IsNullOrWhiteSpace(row.Name) ? $"Layer {row.Id}" : row.Name,
                    FeatureCount = featureCounts.GetValueOrDefault(row.Id),
                    LastSyncTime = row.LastSync,
                    SourceUrl = DiagnosticRedactor.RedactUrl(row.ServerUrl)
                })
                .ToList();

            var featureSources = featureCounts
                .OrderBy(row => row.Key)
                .Select(row =>
                {
                    var metadata = metadataRows.FirstOrDefault(layer => layer.Id == row.Key);
                    return new OfflineSourceDiagnostics
                    {
                        SourceId = row.Key.ToString(CultureInfo.InvariantCulture),
                        DisplayName = metadata == null || string.IsNullOrWhiteSpace(metadata.Name)
                            ? $"Layer {row.Key}"
                            : metadata.Name,
                        FeatureCount = row.Value,
                        LastSyncTime = metadata?.LastSync,
                        SourceUrl = DiagnosticRedactor.RedactUrl(metadata?.ServerUrl)
                    };
                })
                .ToList();

            var operations = MapOperationCounts(operationCounts, attachmentCounts, unresolvedConflictCount);
            var session = latestSession.FirstOrDefault();
            var lastSyncTimes = metadataRows
                .Select(row => row.LastSync)
                .Concat(latestSession.Select(row => (DateTime?)(row.EndTime ?? row.StartTime)))
                .Where(value => value.HasValue)
                .ToList();
            var lastSyncTime = lastSyncTimes.Count == 0 ? null : lastSyncTimes.Max();
            var metadataCreatedTimes = metadataRows.Select(row => row.CreatedAt).ToList();

            return new OfflineCacheDiagnostics
            {
                PackageId = Path.GetFileNameWithoutExtension(_databasePath),
                PackageFileName = DiagnosticRedactor.RedactPath(_databasePath),
                PackageSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                LastSyncTime = lastSyncTime,
                LocalGeneration = session?.LocalGeneration,
                ServerGeneration = session?.ServerGeneration,
                MetadataCache = new MetadataCacheDiagnostics
                {
                    Status = metadataRows.Count == 0 ? "Missing" : "Available",
                    SourceCount = metadataRows.Count,
                    LastUpdatedUtc = metadataCreatedTimes.Count == 0 ? null : metadataCreatedTimes.Max(),
                    Sources = metadataSources
                },
                FeatureCache = new FeatureCacheDiagnostics
                {
                    Status = featureRows.Count == 0 ? "Empty" : "Available",
                    SourceCount = featureRows.Count,
                    TotalFeatureCount = featureRows.Sum(row => row.FeatureCount),
                    SizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                    Sources = featureSources
                },
                Operations = operations,
                ConflictReview = conflicts.Select(MapConflictReviewItem).ToList()
            };
        }
        finally
        {
            _dbLock.Release();
        }
    }

    private static OfflineOperationDiagnostics MapOperationCounts(
        IEnumerable<OperationStatusCount> counts,
        IEnumerable<AttachmentStatusCount> attachmentCounts,
        int unresolvedConflictCount)
    {
        var byStatus = counts.ToDictionary(row => row.SyncStatus, row => row.ItemCount);
        var attachmentsByStatus = attachmentCounts.ToDictionary(row => row.SyncStatus, row => row.ItemCount);
        var attachmentPending =
            attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.PendingUpload) +
            attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.UploadFailed) +
            attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.PendingDownload) +
            attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.DownloadFailed) +
            attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.PendingDelete) +
            attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.DeleteFailed);

        return new OfflineOperationDiagnostics
        {
            PendingCount = byStatus.GetValueOrDefault(StorageSyncStatus.PendingUpload) +
                byStatus.GetValueOrDefault(StorageSyncStatus.PendingDownload) +
                attachmentPending,
            ClaimedCount = 0,
            SucceededCount = byStatus.GetValueOrDefault(StorageSyncStatus.Synced),
            FailedCount = byStatus.GetValueOrDefault(StorageSyncStatus.Error),
            RetryCount = 0,
            ConflictCount = unresolvedConflictCount + byStatus.GetValueOrDefault(StorageSyncStatus.Conflict),
            AttachmentPendingCount = attachmentPending,
            AttachmentSucceededCount = attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.Synced),
            AttachmentFailedCount =
                attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.UploadFailed) +
                attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.DownloadFailed) +
                attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.DeleteFailed),
            AttachmentUploadFailedCount = attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.UploadFailed),
            AttachmentDownloadFailedCount = attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.DownloadFailed),
            AttachmentDeleteFailedCount = attachmentsByStatus.GetValueOrDefault(AttachmentSyncStatus.DeleteFailed)
        };
    }

    private static OfflineConflictReviewItem MapConflictReviewItem(ConflictRecord conflict)
    {
        return new OfflineConflictReviewItem
        {
            ConflictId = conflict.Id,
            OperationId = conflict.Id,
            SourceId = conflict.LayerId.ToString(CultureInfo.InvariantCulture),
            FeatureId = conflict.FeatureId,
            ConflictType = conflict.ConflictType.ToString(),
            Status = conflict.Resolution == StorageConflictResolution.Manual && conflict.ResolvedAt == null
                ? "Deferred"
                : "Needs review",
            Reason = $"Local v{conflict.LocalVersion} conflicts with server v{conflict.ServerVersion}.",
            LocalState = DiagnosticRedactor.RedactJson(conflict.LocalData),
            ServerState = DiagnosticRedactor.RedactJson(conflict.ServerData),
            DetectedAtUtc = conflict.CreatedAt,
            ResolutionActions =
            [
                "AcceptLocal",
                "AcceptServer",
                "Manual"
            ]
        };
    }

    public async Task CompactAsync()
    {
        await EnsureInitializedAsync();
        await _dbLock.WaitAsync();
        try
        {
            await _connection.ExecuteAsync("VACUUM");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    #endregion

    #region IDisposable

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            // Close the SQLite connection without blocking and without forcing continuations
            // back onto the captured (UI) context.
            await _connection.CloseAsync().ConfigureAwait(false);
        }

        _dbLock?.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        // Prefer DisposeAsync. When a synchronous Dispose is unavoidable, close off the captured
        // context so the connection close cannot deadlock against a UI-thread continuation.
        _connection?.CloseAsync().ConfigureAwait(false).GetAwaiter().GetResult();
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

internal sealed class LayerFeatureCount
{
    [Column("layer_id")]
    public int LayerId { get; set; }

    [Column("feature_count")]
    public int FeatureCount { get; set; }
}

internal sealed class OperationStatusCount
{
    [Column("sync_status")]
    public StorageSyncStatus SyncStatus { get; set; }

    [Column("item_count")]
    public int ItemCount { get; set; }
}

internal sealed class AttachmentStatusCount
{
    [Column("sync_status")]
    public AttachmentSyncStatus SyncStatus { get; set; }

    [Column("item_count")]
    public int ItemCount { get; set; }
}
