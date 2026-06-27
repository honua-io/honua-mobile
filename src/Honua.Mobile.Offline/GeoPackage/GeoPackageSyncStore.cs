// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Honua.Mobile.Offline;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Abstractions.Scenes;
using Microsoft.Data.Sqlite;

using BoundingBox = Honua.Sdk.Geometry.GeographicBoundingBox;

namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>
/// SQLite-backed implementation of <see cref="IGeoPackageSyncStore"/> that persists
/// offline edit operations, sync cursors, map area metadata, and replicated features
/// in a GeoPackage-compliant database.
/// </summary>
public sealed class GeoPackageSyncStore : IGeoPackageSyncStore
{
    private const int DefaultSrsId = 4326;
    private const string DefaultCrsIdentifier = "EPSG:4326";
    private const string Wgs84Definition = "GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]]";
    private const string WebMercatorDefinition = "PROJCS[\"WGS 84 / Pseudo-Mercator\",GEOGCS[\"WGS 84\",DATUM[\"WGS_1984\",SPHEROID[\"WGS 84\",6378137,298.257223563]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Mercator_1SP\"],PARAMETER[\"central_meridian\",0],PARAMETER[\"scale_factor\",1],PARAMETER[\"false_easting\",0],PARAMETER[\"false_northing\",0],UNIT[\"metre\",1],AXIS[\"X\",EAST],AXIS[\"Y\",NORTH],AUTHORITY[\"EPSG\",\"3857\"]]";
    private readonly GeoPackageSyncStoreOptions _options;

    /// <summary>
    /// Initializes a new <see cref="GeoPackageSyncStore"/> with the specified options.
    /// </summary>
    /// <param name="options">Store configuration including the database path.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is <see langword="null"/>.</exception>
    public GeoPackageSyncStore(GeoPackageSyncStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_options.AutoCreateDirectory)
        {
            var directory = Path.GetDirectoryName(_options.DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var sql = @"
CREATE TABLE IF NOT EXISTS gpkg_spatial_ref_sys (
    srs_name TEXT NOT NULL,
    srs_id INTEGER NOT NULL PRIMARY KEY,
    organization TEXT NOT NULL,
    organization_coordsys_id INTEGER NOT NULL,
    definition TEXT NOT NULL,
    description TEXT
);

INSERT OR IGNORE INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description)
VALUES ('Undefined Cartesian', -1, 'NONE', -1, 'undefined', 'undefined Cartesian coordinate reference system');

INSERT OR IGNORE INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description)
VALUES ('Undefined Geographic', 0, 'NONE', 0, 'undefined', 'undefined geographic coordinate reference system');

INSERT OR IGNORE INTO gpkg_spatial_ref_sys (srs_name, srs_id, organization, organization_coordsys_id, definition, description)
VALUES ('WGS 84 geodetic', 4326, 'EPSG', 4326, 'GEOGCS[""WGS 84"",DATUM[""WGS_1984"",SPHEROID[""WGS 84"",6378137,298.257223563]],PRIMEM[""Greenwich"",0],UNIT[""degree"",0.0174532925199433]]', 'longitude/latitude coordinates in decimal degrees on the WGS 84 spheroid');

CREATE TABLE IF NOT EXISTS gpkg_contents (
    table_name TEXT NOT NULL PRIMARY KEY,
    data_type TEXT NOT NULL,
    identifier TEXT UNIQUE,
    description TEXT DEFAULT '',
    last_change DATETIME NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    min_x DOUBLE,
    min_y DOUBLE,
    max_x DOUBLE,
    max_y DOUBLE,
    srs_id INTEGER,
    CONSTRAINT fk_gc_r_srs_id FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
);

CREATE TABLE IF NOT EXISTS gpkg_geometry_columns (
    table_name TEXT NOT NULL,
    column_name TEXT NOT NULL,
    geometry_type_name TEXT NOT NULL,
    srs_id INTEGER NOT NULL,
    z TINYINT NOT NULL,
    m TINYINT NOT NULL,
    PRIMARY KEY (table_name, column_name),
    CONSTRAINT fk_ggc_tn FOREIGN KEY (table_name) REFERENCES gpkg_contents(table_name),
    CONSTRAINT fk_ggc_srs FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
);

CREATE TABLE IF NOT EXISTS honua_sync_queue (
    operation_id TEXT PRIMARY KEY,
    layer_key TEXT NOT NULL,
    target_collection TEXT NOT NULL,
    operation_type TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    priority INTEGER NOT NULL,
    created_at_utc TEXT NOT NULL,
    claimed_at_utc TEXT,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    last_error TEXT,
    status TEXT NOT NULL DEFAULT 'pending'
);

CREATE INDEX IF NOT EXISTS idx_honua_sync_queue_pending ON honua_sync_queue(status, priority, created_at_utc);

CREATE TABLE IF NOT EXISTS honua_sync_state (
    cursor_key TEXT PRIMARY KEY,
    cursor_value TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS honua_map_areas (
    area_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    bbox_json TEXT NOT NULL,
    min_zoom INTEGER NOT NULL,
    max_zoom INTEGER NOT NULL,
    geopackage_path TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS honua_scene_packages (
    package_id TEXT PRIMARY KEY,
    scene_id TEXT NOT NULL,
    display_name TEXT,
    edition_gate TEXT NOT NULL,
    server_revision TEXT NOT NULL,
    extent_json TEXT,
    package_directory TEXT NOT NULL,
    manifest_path TEXT NOT NULL,
    state TEXT NOT NULL,
    declared_bytes INTEGER NOT NULL,
    downloaded_bytes INTEGER NOT NULL,
    required_asset_count INTEGER NOT NULL,
    downloaded_asset_count INTEGER NOT NULL,
    missing_optional_asset_keys_json TEXT NOT NULL DEFAULT '[]',
    stale_after_utc TEXT,
    offline_use_expires_at_utc TEXT,
    auth_expires_at_utc TEXT,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS honua_feature_layers (
    layer_key TEXT PRIMARY KEY,
    crs_identifier TEXT NOT NULL DEFAULT 'EPSG:4326',
    srs_id INTEGER NOT NULL DEFAULT 4326,
    updated_at_utc TEXT NOT NULL,
    CONSTRAINT fk_hfl_srs_id FOREIGN KEY (srs_id) REFERENCES gpkg_spatial_ref_sys(srs_id)
);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_sync_queue', 'attributes', 'honua_sync_queue', 'Offline edit queue for Honua mobile sync', 4326);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_map_areas', 'attributes', 'honua_map_areas', 'Downloaded map area package catalog', 4326);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_scene_packages', 'attributes', 'honua_scene_packages', 'Downloaded immutable 3D scene package catalog', 4326);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_feature_layers', 'attributes', 'honua_feature_layers', 'Feature cache layer CRS metadata', 4326);

CREATE TABLE IF NOT EXISTS honua_features (
    layer_key TEXT NOT NULL,
    object_id INTEGER NOT NULL,
    feature_json TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    expires_at_utc TEXT,
    min_x DOUBLE,
    min_y DOUBLE,
    max_x DOUBLE,
    max_y DOUBLE,
    PRIMARY KEY (layer_key, object_id)
);

CREATE TABLE IF NOT EXISTS honua_feature_index_keys (
    index_id INTEGER PRIMARY KEY AUTOINCREMENT,
    layer_key TEXT NOT NULL,
    object_id INTEGER NOT NULL,
    UNIQUE(layer_key, object_id)
);

CREATE VIRTUAL TABLE IF NOT EXISTS rtree_honua_features USING rtree(
    index_id,
    min_x,
    max_x,
    min_y,
    max_y
);

CREATE INDEX IF NOT EXISTS idx_honua_features_layer_expires ON honua_features(layer_key, expires_at_utc);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_features', 'attributes', 'honua_features', 'Replicated feature cache for delta sync', 4326);
";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await EnsureColumnExistsAsync(connection, "honua_sync_queue", "claimed_at_utc", "TEXT", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(
            connection,
            "honua_scene_packages",
            "missing_optional_asset_keys_json",
            "TEXT NOT NULL DEFAULT '[]'",
            ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(connection, "honua_features", "expires_at_utc", "TEXT", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(connection, "honua_features", "min_x", "DOUBLE", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(connection, "honua_features", "min_y", "DOUBLE", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(connection, "honua_features", "max_x", "DOUBLE", ct).ConfigureAwait(false);
        await EnsureColumnExistsAsync(connection, "honua_features", "max_y", "DOUBLE", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(OfflineEditOperation operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO honua_sync_queue (operation_id, layer_key, target_collection, operation_type, payload_json, priority, created_at_utc, claimed_at_utc, attempt_count, status)
VALUES ($operation_id, $layer_key, $target_collection, $operation_type, $payload_json, $priority, $created_at_utc, NULL, $attempt_count, 'pending')
ON CONFLICT(operation_id) DO UPDATE SET
  layer_key = excluded.layer_key,
  target_collection = excluded.target_collection,
  operation_type = excluded.operation_type,
  payload_json = excluded.payload_json,
  priority = excluded.priority,
  created_at_utc = excluded.created_at_utc,
  claimed_at_utc = NULL,
  attempt_count = excluded.attempt_count,
  status = 'pending',
  last_error = NULL;
";

        command.Parameters.AddWithValue("$operation_id", operation.OperationId);
        command.Parameters.AddWithValue("$layer_key", operation.LayerKey);
        command.Parameters.AddWithValue("$target_collection", operation.TargetCollection);
        command.Parameters.AddWithValue("$operation_type", operation.OperationType.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$payload_json", operation.PayloadJson);
        command.Parameters.AddWithValue("$priority", operation.Priority);
        command.Parameters.AddWithValue("$created_at_utc", operation.CreatedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$attempt_count", operation.AttemptCount);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfflineEditOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default)
        => await GetPendingCoreAsync(maxCount, layerKeyPrefix: null, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfflineEditOperation>> GetPendingByLayerKeyPrefixAsync(
        string layerKeyPrefix,
        int maxCount,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKeyPrefix);
        return await GetPendingCoreAsync(maxCount, layerKeyPrefix, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<OfflineEditOperation>> GetPendingCoreAsync(
        int maxCount,
        string? layerKeyPrefix,
        CancellationToken ct)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await ExecuteTransactionCommandAsync(connection, "BEGIN IMMEDIATE;", ct).ConfigureAwait(false);
        try
        {
            var leaseTimeout = _options.InProgressLeaseTimeout < TimeSpan.Zero
                ? TimeSpan.Zero
                : _options.InProgressLeaseTimeout;
            var staleClaimCutoffUtc = DateTimeOffset.UtcNow - leaseTimeout;
            var claimedOperationIds = await ReadPendingOperationIdsAsync(
                connection,
                maxCount,
                staleClaimCutoffUtc,
                layerKeyPrefix,
                ct).ConfigureAwait(false);
            if (claimedOperationIds.Count == 0)
            {
                await ExecuteTransactionCommandAsync(connection, "COMMIT;", ct).ConfigureAwait(false);
                return [];
            }

            await using (var claimCommand = connection.CreateCommand())
            {
                var idParameters = AddOperationIdParameters(claimCommand, claimedOperationIds);
                claimCommand.CommandText = $@"
UPDATE honua_sync_queue
SET status = 'in_progress',
    claimed_at_utc = $claimed_at_utc
WHERE status IN ('pending', 'retry', 'in_progress')
  AND operation_id IN ({idParameters});
";
                claimCommand.Parameters.AddWithValue("$claimed_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

                await claimCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            var items = new List<OfflineEditOperation>(capacity: claimedOperationIds.Count);
            await using (var command = connection.CreateCommand())
            {
                var idParameters = AddOperationIdParameters(command, claimedOperationIds);
                command.CommandText = $@"
SELECT operation_id, layer_key, target_collection, operation_type, payload_json, priority, created_at_utc, attempt_count
FROM honua_sync_queue
WHERE operation_id IN ({idParameters})
ORDER BY priority ASC, created_at_utc ASC;
";

                await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    items.Add(ReadOfflineEditOperation(reader));
                }
            }

            await ExecuteTransactionCommandAsync(connection, "COMMIT;", ct).ConfigureAwait(false);
            return items;
        }
        catch
        {
            try
            {
                await ExecuteTransactionCommandAsync(connection, "ROLLBACK;", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Preserve original exception when rollback also fails.
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM honua_sync_queue WHERE status IN ('pending', 'retry');";
        var count = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
        return (int)count;
    }

    /// <inheritdoc />
    public async Task MarkSucceededAsync(string operationId, CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM honua_sync_queue WHERE operation_id = $operation_id;";
        command.Parameters.AddWithValue("$operation_id", operationId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkPendingAsync(string operationId, CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE honua_sync_queue
SET status = 'pending',
    claimed_at_utc = NULL,
    last_error = NULL
WHERE operation_id = $operation_id;
";
        command.Parameters.AddWithValue("$operation_id", operationId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(string operationId, string failureReason, bool retryable, CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE honua_sync_queue
SET status = $status,
    attempt_count = attempt_count + 1,
    claimed_at_utc = NULL,
    last_error = $last_error
WHERE operation_id = $operation_id;
";

        command.Parameters.AddWithValue("$status", retryable ? "retry" : "failed");
        command.Parameters.AddWithValue("$last_error", failureReason);
        command.Parameters.AddWithValue("$operation_id", operationId);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetSyncCursorAsync(string cursorKey, string cursorValue, CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO honua_sync_state (cursor_key, cursor_value, updated_at_utc)
VALUES ($cursor_key, $cursor_value, $updated_at_utc)
ON CONFLICT(cursor_key) DO UPDATE SET
  cursor_value = excluded.cursor_value,
  updated_at_utc = excluded.updated_at_utc;
";

        command.Parameters.AddWithValue("$cursor_key", cursorKey);
        command.Parameters.AddWithValue("$cursor_value", cursorValue);
        command.Parameters.AddWithValue("$updated_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> GetSyncCursorAsync(string cursorKey, CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT cursor_value FROM honua_sync_state WHERE cursor_key = $cursor_key LIMIT 1;";
        command.Parameters.AddWithValue("$cursor_key", cursorKey);

        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value as string;
    }

    /// <inheritdoc />
    public async Task UpsertMapAreaAsync(MapAreaPackage mapArea, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mapArea);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var bbox = HonuaMobileOfflineJson.Serialize(mapArea.BoundingBox);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO honua_map_areas (area_id, name, bbox_json, min_zoom, max_zoom, geopackage_path, updated_at_utc)
VALUES ($area_id, $name, $bbox_json, $min_zoom, $max_zoom, $geopackage_path, $updated_at_utc)
ON CONFLICT(area_id) DO UPDATE SET
  name = excluded.name,
  bbox_json = excluded.bbox_json,
  min_zoom = excluded.min_zoom,
  max_zoom = excluded.max_zoom,
  geopackage_path = excluded.geopackage_path,
  updated_at_utc = excluded.updated_at_utc;
";

        command.Parameters.AddWithValue("$area_id", mapArea.AreaId);
        command.Parameters.AddWithValue("$name", mapArea.Name);
        command.Parameters.AddWithValue("$bbox_json", bbox);
        command.Parameters.AddWithValue("$min_zoom", mapArea.MinZoom);
        command.Parameters.AddWithValue("$max_zoom", mapArea.MaxZoom);
        command.Parameters.AddWithValue("$geopackage_path", mapArea.GeoPackagePath);
        command.Parameters.AddWithValue("$updated_at_utc", mapArea.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MapAreaPackage>> ListMapAreasAsync(CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT area_id, name, bbox_json, min_zoom, max_zoom, geopackage_path, updated_at_utc FROM honua_map_areas ORDER BY name ASC;";

        var items = new List<MapAreaPackage>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var bbox = HonuaMobileOfflineJson.Deserialize<BoundingBox>(reader.GetString(2))
                ?? throw new InvalidOperationException("Invalid bbox payload in honua_map_areas.");

            items.Add(new MapAreaPackage
            {
                AreaId = reader.GetString(0),
                Name = reader.GetString(1),
                BoundingBox = bbox,
                MinZoom = reader.GetInt32(3),
                MaxZoom = reader.GetInt32(4),
                GeoPackagePath = reader.GetString(5),
                UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            });
        }

        return items;
    }

    /// <inheritdoc />
    public async Task UpsertScenePackageAsync(ScenePackageRecord scenePackage, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scenePackage);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var extent = scenePackage.Extent is null
            ? null
            : HonuaMobileOfflineJson.Serialize(scenePackage.Extent);
        var missingOptionalAssetKeysJson = HonuaMobileOfflineJson.Serialize(scenePackage.MissingOptionalAssetKeys.ToArray());

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO honua_scene_packages (
  package_id,
  scene_id,
  display_name,
  edition_gate,
  server_revision,
  extent_json,
  package_directory,
  manifest_path,
  state,
  declared_bytes,
  downloaded_bytes,
  required_asset_count,
  downloaded_asset_count,
  missing_optional_asset_keys_json,
  stale_after_utc,
  offline_use_expires_at_utc,
  auth_expires_at_utc,
  updated_at_utc)
VALUES (
  $package_id,
  $scene_id,
  $display_name,
  $edition_gate,
  $server_revision,
  $extent_json,
  $package_directory,
  $manifest_path,
  $state,
  $declared_bytes,
  $downloaded_bytes,
  $required_asset_count,
  $downloaded_asset_count,
  $missing_optional_asset_keys_json,
  $stale_after_utc,
  $offline_use_expires_at_utc,
  $auth_expires_at_utc,
  $updated_at_utc)
ON CONFLICT(package_id) DO UPDATE SET
  scene_id = excluded.scene_id,
  display_name = excluded.display_name,
  edition_gate = excluded.edition_gate,
  server_revision = excluded.server_revision,
  extent_json = excluded.extent_json,
  package_directory = excluded.package_directory,
  manifest_path = excluded.manifest_path,
  state = excluded.state,
  declared_bytes = excluded.declared_bytes,
  downloaded_bytes = excluded.downloaded_bytes,
  required_asset_count = excluded.required_asset_count,
  downloaded_asset_count = excluded.downloaded_asset_count,
  missing_optional_asset_keys_json = excluded.missing_optional_asset_keys_json,
  stale_after_utc = excluded.stale_after_utc,
  offline_use_expires_at_utc = excluded.offline_use_expires_at_utc,
  auth_expires_at_utc = excluded.auth_expires_at_utc,
  updated_at_utc = excluded.updated_at_utc;
";

        command.Parameters.AddWithValue("$package_id", scenePackage.PackageId);
        command.Parameters.AddWithValue("$scene_id", scenePackage.SceneId);
        command.Parameters.AddWithValue("$display_name", ToDbValue(scenePackage.DisplayName));
        command.Parameters.AddWithValue("$edition_gate", scenePackage.EditionGate);
        command.Parameters.AddWithValue("$server_revision", scenePackage.ServerRevision);
        command.Parameters.AddWithValue("$extent_json", ToDbValue(extent));
        command.Parameters.AddWithValue("$package_directory", scenePackage.PackageDirectory);
        command.Parameters.AddWithValue("$manifest_path", scenePackage.ManifestPath);
        command.Parameters.AddWithValue("$state", scenePackage.State.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("$declared_bytes", scenePackage.DeclaredBytes);
        command.Parameters.AddWithValue("$downloaded_bytes", scenePackage.DownloadedBytes);
        command.Parameters.AddWithValue("$required_asset_count", scenePackage.RequiredAssetCount);
        command.Parameters.AddWithValue("$downloaded_asset_count", scenePackage.DownloadedAssetCount);
        command.Parameters.AddWithValue("$missing_optional_asset_keys_json", missingOptionalAssetKeysJson);
        command.Parameters.AddWithValue("$stale_after_utc", ToDbValue(scenePackage.StaleAfterUtc));
        command.Parameters.AddWithValue("$offline_use_expires_at_utc", ToDbValue(scenePackage.OfflineUseExpiresAtUtc));
        command.Parameters.AddWithValue("$auth_expires_at_utc", ToDbValue(scenePackage.AuthExpiresAtUtc));
        command.Parameters.AddWithValue("$updated_at_utc", scenePackage.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScenePackageRecord>> ListScenePackagesAsync(CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT
  package_id,
  scene_id,
  display_name,
  edition_gate,
  server_revision,
  extent_json,
  package_directory,
  manifest_path,
  state,
  declared_bytes,
  downloaded_bytes,
  required_asset_count,
  downloaded_asset_count,
  missing_optional_asset_keys_json,
  stale_after_utc,
  offline_use_expires_at_utc,
  auth_expires_at_utc,
  updated_at_utc
FROM honua_scene_packages
ORDER BY scene_id ASC, package_id ASC;
";

        var items = new List<ScenePackageRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            items.Add(ReadScenePackageRecord(reader));
        }

        return items;
    }

    /// <inheritdoc />
    public async Task DeleteScenePackageAsync(string packageId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM honua_scene_packages WHERE package_id = $package_id;";
        command.Parameters.AddWithValue("$package_id", packageId);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GeoPackageFeatureLayerMetadata?> GetFeatureLayerMetadataAsync(string layerKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT layer_key, crs_identifier, srs_id, updated_at_utc
FROM honua_feature_layers
WHERE layer_key = $layer_key
LIMIT 1;
";
        command.Parameters.AddWithValue("$layer_key", layerKey);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new GeoPackageFeatureLayerMetadata
        {
            LayerKey = reader.GetString(0),
            CrsIdentifier = reader.GetString(1),
            SrsId = reader.GetInt32(2),
            UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };
    }

    /// <inheritdoc />
    public async Task UpsertFeatureAsync(string layerKey, string featureJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureJson);

        using var activity = MobileStorageTelemetry.ActivitySource.StartActivity("honua.mobile.storage.feature.upsert", ActivityKind.Internal);
        activity?.SetTag("layer_key", layerKey);

        var feature = ParseFeatureCacheRecord(featureJson, activity);
        var expiresAtUtc = ResolveFeatureExpiresAtUtc(layerKey);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await ExecuteTransactionCommandAsync(connection, "BEGIN IMMEDIATE;", ct).ConfigureAwait(false);
        try
        {
            await WriteFeatureCoreAsync(connection, layerKey, feature, featureJson, expiresAtUtc, ct).ConfigureAwait(false);
            await ExecuteTransactionCommandAsync(connection, "COMMIT;", ct).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Error);
            throw GeoPackageStorageProblems.WriteFailed(ex);
        }
        catch
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpsertFeaturesAsync(string layerKey, IReadOnlyList<string> featureJsons, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);
        ArgumentNullException.ThrowIfNull(featureJsons);

        if (featureJsons.Count == 0)
        {
            return;
        }

        using var activity = MobileStorageTelemetry.ActivitySource.StartActivity("honua.mobile.storage.feature.upsert.batch", ActivityKind.Internal);
        activity?.SetTag("layer_key", layerKey);
        activity?.SetTag("feature_count", featureJsons.Count);

        // Parse and validate every payload before opening the connection so a malformed
        // feature fails fast without leaving a half-applied write lock open.
        var parsed = new FeatureCacheRecord[featureJsons.Count];
        for (var i = 0; i < featureJsons.Count; i++)
        {
            var featureJson = featureJsons[i];
            ArgumentException.ThrowIfNullOrWhiteSpace(featureJson, $"{nameof(featureJsons)}[{i}]");
            parsed[i] = ParseFeatureCacheRecord(featureJson, activity);
        }

        var expiresAtUtc = ResolveFeatureExpiresAtUtc(layerKey);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        // A single BEGIN IMMEDIATE / COMMIT around the whole batch means one fsync for the
        // entire change set instead of one per feature.
        await ExecuteTransactionCommandAsync(connection, "BEGIN IMMEDIATE;", ct).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < parsed.Length; i++)
            {
                await WriteFeatureCoreAsync(connection, layerKey, parsed[i], featureJsons[i], expiresAtUtc, ct).ConfigureAwait(false);
            }

            await ExecuteTransactionCommandAsync(connection, "COMMIT;", ct).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Error);
            throw GeoPackageStorageProblems.WriteFailed(ex);
        }
        catch
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteFeatureAsync(string layerKey, long objectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await ExecuteTransactionCommandAsync(connection, "BEGIN IMMEDIATE;", ct).ConfigureAwait(false);
        try
        {
            await RemoveFeatureCoreAsync(connection, layerKey, objectId, ct).ConfigureAwait(false);
            await ExecuteTransactionCommandAsync(connection, "COMMIT;", ct).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            throw GeoPackageStorageProblems.DeleteFailed(ex);
        }
        catch
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteFeaturesAsync(string layerKey, IReadOnlyList<long> objectIds, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);
        ArgumentNullException.ThrowIfNull(objectIds);

        if (objectIds.Count == 0)
        {
            return;
        }

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await ExecuteTransactionCommandAsync(connection, "BEGIN IMMEDIATE;", ct).ConfigureAwait(false);
        try
        {
            foreach (var objectId in objectIds)
            {
                await RemoveFeatureCoreAsync(connection, layerKey, objectId, ct).ConfigureAwait(false);
            }

            await ExecuteTransactionCommandAsync(connection, "COMMIT;", ct).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            throw GeoPackageStorageProblems.DeleteFailed(ex);
        }
        catch
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    private static FeatureCacheRecord ParseFeatureCacheRecord(string featureJson, Activity? activity)
    {
        try
        {
            return ExtractFeatureCacheRecord(featureJson);
        }
        catch (JsonException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw GeoPackageStorageProblems.InvalidFeatureJson(ex);
        }
        catch (InvalidOperationException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            throw GeoPackageStorageProblems.InvalidFeatureJson(ex);
        }
    }

    private async Task WriteFeatureCoreAsync(
        SqliteConnection connection,
        string layerKey,
        FeatureCacheRecord feature,
        string featureJson,
        DateTimeOffset? expiresAtUtc,
        CancellationToken ct)
    {
        var objectId = feature.ObjectId;
        var extent = feature.Extent;
        var spatialReference = feature.SpatialReference ?? FeatureSpatialReference.Default;

        await UpsertFeatureLayerMetadataAsync(
            connection,
            layerKey,
            spatialReference,
            feature.SpatialReference is not null,
            ct).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
INSERT INTO honua_features (layer_key, object_id, feature_json, updated_at_utc, expires_at_utc, min_x, min_y, max_x, max_y)
VALUES ($layer_key, $object_id, $feature_json, $updated_at_utc, $expires_at_utc, $min_x, $min_y, $max_x, $max_y)
ON CONFLICT(layer_key, object_id) DO UPDATE SET
  feature_json = excluded.feature_json,
  updated_at_utc = excluded.updated_at_utc,
  expires_at_utc = excluded.expires_at_utc,
  min_x = excluded.min_x,
  min_y = excluded.min_y,
  max_x = excluded.max_x,
  max_y = excluded.max_y;
";

            command.Parameters.AddWithValue("$layer_key", layerKey);
            command.Parameters.AddWithValue("$object_id", objectId);
            command.Parameters.AddWithValue("$feature_json", featureJson);
            command.Parameters.AddWithValue("$updated_at_utc", _options.TimeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$expires_at_utc", ToDbValue(expiresAtUtc));
            command.Parameters.AddWithValue("$min_x", ToDbValue(extent?.MinX));
            command.Parameters.AddWithValue("$min_y", ToDbValue(extent?.MinY));
            command.Parameters.AddWithValue("$max_x", ToDbValue(extent?.MaxX));
            command.Parameters.AddWithValue("$max_y", ToDbValue(extent?.MaxY));

            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await UpsertFeatureIndexAsync(connection, layerKey, objectId, extent, ct).ConfigureAwait(false);
    }

    private static async Task RemoveFeatureCoreAsync(
        SqliteConnection connection,
        string layerKey,
        long objectId,
        CancellationToken ct)
    {
        var indexId = await GetFeatureIndexIdAsync(connection, layerKey, objectId, ct).ConfigureAwait(false);
        if (indexId.HasValue)
        {
            await DeleteFeatureIndexAsync(connection, indexId.Value, ct).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM honua_features WHERE layer_key = $layer_key AND object_id = $object_id;";
        command.Parameters.AddWithValue("$layer_key", layerKey);
        command.Parameters.AddWithValue("$object_id", objectId);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetFeaturesAsync(string layerKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);

        using var activity = MobileStorageTelemetry.ActivitySource.StartActivity("honua.mobile.storage.feature.query", ActivityKind.Internal);
        activity?.SetTag("layer_key", layerKey);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT feature_json
FROM honua_features
WHERE layer_key = $layer_key
  AND (expires_at_utc IS NULL OR expires_at_utc > $now_utc)
ORDER BY object_id ASC;
";
        command.Parameters.AddWithValue("$layer_key", layerKey);
        command.Parameters.AddWithValue("$now_utc", _options.TimeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));

        var items = new List<string>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(reader.GetString(0));
            }

            MobileStorageTelemetry.RecordQuery("success");
            return items;
        }
        catch (SqliteException ex)
        {
            MobileStorageTelemetry.RecordQuery("failure");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw GeoPackageStorageProblems.QueryFailed(ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetFeaturesAsync(string layerKey, BoundingBox boundingBox, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(boundingBox);

        // The SDK <see cref="GeographicBoundingBox"/> overload is, by contract, WGS84 degrees.
        return await GetFeaturesByBoundingBoxCoreAsync(
            layerKey,
            boundingBox.MinLongitude,
            boundingBox.MinLatitude,
            boundingBox.MaxLongitude,
            boundingBox.MaxLatitude,
            DefaultCrsIdentifier,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetFeaturesAsync(string layerKey, FeatureBoundingBox boundingBox, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(boundingBox);

        // FeatureBoundingBox carries its own CRS and may be in the layer's native
        // projected reference (e.g. Web Mercator EPSG:3857), so its coordinates must
        // NOT be funneled through the geographic-only GeographicBoundingBox type, whose
        // validation rejects out-of-[-180,180] longitudes. The R-tree query runs in the
        // layer CRS, so projected query coordinates are valid here.
        var queryCrs = NormalizeCrsIdentifier(boundingBox.Crs) ?? DefaultCrsIdentifier;
        return await GetFeaturesByBoundingBoxCoreAsync(
            layerKey,
            boundingBox.MinX,
            boundingBox.MinY,
            boundingBox.MaxX,
            boundingBox.MaxY,
            queryCrs,
            ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> GetFeaturesByBoundingBoxCoreAsync(
        string layerKey,
        double minX,
        double minY,
        double maxX,
        double maxY,
        string queryCrs,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);
        ValidateBoundingBox(minX, minY, maxX, maxY);

        using var activity = MobileStorageTelemetry.ActivitySource.StartActivity("honua.mobile.storage.feature.query_bbox", ActivityKind.Internal);
        activity?.SetTag("layer_key", layerKey);
        activity?.SetTag("query_crs", queryCrs);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        try
        {
            await EnsureQueryCrsMatchesLayerAsync(connection, layerKey, queryCrs, ct).ConfigureAwait(false);
        }
        catch (GeoPackageStorageException)
        {
            MobileStorageTelemetry.RecordQuery("failure");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT f.feature_json
FROM honua_features f
JOIN honua_feature_index_keys k
  ON k.layer_key = f.layer_key
 AND k.object_id = f.object_id
JOIN rtree_honua_features r
  ON r.index_id = k.index_id
WHERE f.layer_key = $layer_key
  AND (f.expires_at_utc IS NULL OR f.expires_at_utc > $now_utc)
  AND r.min_x <= $max_x
  AND r.max_x >= $min_x
  AND r.min_y <= $max_y
  AND r.max_y >= $min_y
ORDER BY f.object_id ASC;
";
        command.Parameters.AddWithValue("$layer_key", layerKey);
        command.Parameters.AddWithValue("$now_utc", _options.TimeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$min_x", minX);
        command.Parameters.AddWithValue("$min_y", minY);
        command.Parameters.AddWithValue("$max_x", maxX);
        command.Parameters.AddWithValue("$max_y", maxY);

        var items = new List<string>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(reader.GetString(0));
            }

            MobileStorageTelemetry.RecordQuery("success");
            return items;
        }
        catch (SqliteException ex)
        {
            MobileStorageTelemetry.RecordQuery("failure");
            activity?.SetStatus(ActivityStatusCode.Error);
            throw GeoPackageStorageProblems.QueryFailed(ex);
        }
    }

    /// <inheritdoc />
    public async Task<int> EvictExpiredFeaturesAsync(CancellationToken ct = default)
    {
        var nowUtc = _options.TimeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await ExecuteTransactionCommandAsync(connection, "BEGIN IMMEDIATE;", ct).ConfigureAwait(false);
        try
        {
            var expiredIndexIds = await ReadExpiredFeatureIndexIdsAsync(connection, nowUtc, ct).ConfigureAwait(false);
            if (expiredIndexIds.Count > 0)
            {
                await DeleteFeatureIndexesAsync(connection, expiredIndexIds, ct).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM honua_features WHERE expires_at_utc IS NOT NULL AND expires_at_utc <= $now_utc;";
            command.Parameters.AddWithValue("$now_utc", nowUtc);
            var evicted = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await ExecuteTransactionCommandAsync(connection, "COMMIT;", ct).ConfigureAwait(false);
            MobileStorageTelemetry.RecordEvictions(evicted);
            return evicted;
        }
        catch (SqliteException ex)
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            throw GeoPackageStorageProblems.EvictionFailed(ex);
        }
        catch
        {
            await RollbackQuietlyAsync(connection).ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<long> GetStorageSizeBytesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var bytes = File.Exists(_options.DatabasePath)
            ? new FileInfo(_options.DatabasePath).Length
            : 0L;
        MobileStorageTelemetry.RecordSizeBytes(bytes);
        return Task.FromResult(bytes);
    }

    private DateTimeOffset? ResolveFeatureExpiresAtUtc(string layerKey)
    {
        TimeSpan? ttl = null;
        if (_options.LayerFeatureCacheTtls.TryGetValue(layerKey, out var layerTtl))
        {
            ttl = layerTtl;
        }
        else
        {
            ttl = _options.DefaultFeatureCacheTtl;
        }

        if (!ttl.HasValue || ttl.Value <= TimeSpan.Zero)
        {
            return null;
        }

        return _options.TimeProvider.GetUtcNow().Add(ttl.Value);
    }

    private static FeatureCacheRecord ExtractFeatureCacheRecord(string featureJson)
    {
        using var doc = JsonDocument.Parse(featureJson);
        var root = doc.RootElement;
        var geometry = root.TryGetProperty("geometry", out var geometryElement)
            ? geometryElement
            : root;

        var objectId = ExtractObjectId(root);
        return new FeatureCacheRecord(
            objectId,
            ExtractFeatureExtent(root, geometry),
            ExtractFeatureSpatialReference(root, geometry));
    }

    private static FeatureSpatialReference? ExtractFeatureSpatialReference(JsonElement root, JsonElement geometry)
    {
        if (TryReadSpatialReference(geometry, out var geometrySpatialReference))
        {
            return geometrySpatialReference;
        }

        if (TryReadSpatialReference(root, out var rootSpatialReference))
        {
            return rootSpatialReference;
        }

        return null;
    }

    private static FeatureExtent? ExtractFeatureExtent(JsonElement root, JsonElement geometry)
    {
        if (TryReadPointExtent(geometry, out var pointExtent))
        {
            return pointExtent;
        }

        if (TryReadEnvelopeExtent(geometry, out var envelopeExtent))
        {
            return envelopeExtent;
        }

        if (TryReadGeoJsonBboxExtent(root, out var bboxExtent))
        {
            return bboxExtent;
        }

        if (TryReadGeoJsonPointExtent(geometry, out var geoJsonPointExtent))
        {
            return geoJsonPointExtent;
        }

        if (TryReadGeoJsonCoordinatesExtent(geometry, out var geoJsonCoordinatesExtent))
        {
            return geoJsonCoordinatesExtent;
        }

        return null;
    }

    private static bool TryReadPointExtent(JsonElement geometry, out FeatureExtent extent)
    {
        if (TryGetDouble(geometry, "x", out var x) && TryGetDouble(geometry, "y", out var y))
        {
            extent = new FeatureExtent(x, y, x, y);
            return true;
        }

        extent = default;
        return false;
    }

    private static bool TryReadEnvelopeExtent(JsonElement geometry, out FeatureExtent extent)
    {
        if (TryGetDouble(geometry, "xmin", out var minX) &&
            TryGetDouble(geometry, "ymin", out var minY) &&
            TryGetDouble(geometry, "xmax", out var maxX) &&
            TryGetDouble(geometry, "ymax", out var maxY))
        {
            extent = new FeatureExtent(minX, minY, maxX, maxY);
            return true;
        }

        extent = default;
        return false;
    }

    private static bool TryReadGeoJsonBboxExtent(JsonElement element, out FeatureExtent extent)
    {
        if (element.TryGetProperty("bbox", out var bbox) && bbox.ValueKind == JsonValueKind.Array)
        {
            var axisCount = bbox.GetArrayLength() / 2;
            if (axisCount >= 2 &&
                bbox.GetArrayLength() % 2 == 0 &&
                bbox[0].TryGetDouble(out var minX) &&
                bbox[1].TryGetDouble(out var minY) &&
                bbox[axisCount].TryGetDouble(out var maxX) &&
                bbox[axisCount + 1].TryGetDouble(out var maxY))
            {
                extent = new FeatureExtent(minX, minY, maxX, maxY);
                return true;
            }
        }

        extent = default;
        return false;
    }

    private static bool TryReadGeoJsonPointExtent(JsonElement geometry, out FeatureExtent extent)
    {
        if (geometry.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            string.Equals(type.GetString(), "Point", StringComparison.OrdinalIgnoreCase) &&
            geometry.TryGetProperty("coordinates", out var coordinates) &&
            coordinates.ValueKind == JsonValueKind.Array &&
            coordinates.GetArrayLength() >= 2 &&
            coordinates[0].TryGetDouble(out var x) &&
            coordinates[1].TryGetDouble(out var y))
        {
            extent = new FeatureExtent(x, y, x, y);
            return true;
        }

        extent = default;
        return false;
    }

    private static bool TryReadGeoJsonCoordinatesExtent(JsonElement geometry, out FeatureExtent extent)
    {
        if (!geometry.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            !geometry.TryGetProperty("coordinates", out var coordinates) ||
            coordinates.ValueKind != JsonValueKind.Array)
        {
            extent = default;
            return false;
        }

        var builder = new FeatureExtentBuilder();
        AccumulateGeoJsonCoordinateExtent(coordinates, ref builder);
        if (builder.HasValue)
        {
            extent = builder.ToFeatureExtent();
            return true;
        }

        extent = default;
        return false;
    }

    private static void AccumulateGeoJsonCoordinateExtent(JsonElement coordinates, ref FeatureExtentBuilder builder)
    {
        if (coordinates.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        if (coordinates.GetArrayLength() >= 2 &&
            coordinates[0].ValueKind == JsonValueKind.Number &&
            coordinates[1].ValueKind == JsonValueKind.Number &&
            coordinates[0].TryGetDouble(out var x) &&
            coordinates[1].TryGetDouble(out var y))
        {
            builder.Include(x, y);
            return;
        }

        foreach (var item in coordinates.EnumerateArray())
        {
            AccumulateGeoJsonCoordinateExtent(item, ref builder);
        }
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.TryGetDouble(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static long ExtractObjectId(JsonElement root)
    {
        if (root.TryGetProperty("attributes", out var attributes) &&
            TryReadObjectId(attributes, out var id))
        {
            return id;
        }

        if (root.TryGetProperty("properties", out var properties) &&
            TryReadObjectId(properties, out id))
        {
            return id;
        }

        if (root.TryGetProperty("OBJECTID", out var topOid) && topOid.TryGetInt64(out var topId))
        {
            return topId;
        }

        if (root.TryGetProperty("objectid", out topOid) && topOid.TryGetInt64(out topId))
        {
            return topId;
        }

        if (root.TryGetProperty("ObjectID", out topOid) && topOid.TryGetInt64(out topId))
        {
            return topId;
        }

        if (root.TryGetProperty("FID", out topOid) && topOid.TryGetInt64(out topId))
        {
            return topId;
        }

        throw new InvalidOperationException("Feature JSON does not contain a recognizable object ID field (OBJECTID, objectid, ObjectID, or FID).");
    }

    private static bool TryReadObjectId(JsonElement attributes, out long objectId)
    {
        if (attributes.ValueKind != JsonValueKind.Object)
        {
            objectId = 0;
            return false;
        }

        if (attributes.TryGetProperty("OBJECTID", out var oid) && oid.TryGetInt64(out objectId))
        {
            return true;
        }

        if (attributes.TryGetProperty("objectid", out oid) && oid.TryGetInt64(out objectId))
        {
            return true;
        }

        if (attributes.TryGetProperty("ObjectID", out oid) && oid.TryGetInt64(out objectId))
        {
            return true;
        }

        if (attributes.TryGetProperty("FID", out oid) && oid.TryGetInt64(out objectId))
        {
            return true;
        }

        objectId = 0;
        return false;
    }

    private static bool TryReadSpatialReference(JsonElement element, out FeatureSpatialReference spatialReference)
    {
        if (element.TryGetProperty("spatialReference", out var spatialReferenceElement) &&
            spatialReferenceElement.ValueKind == JsonValueKind.Object &&
            TryReadFeatureServerSpatialReference(spatialReferenceElement, out spatialReference))
        {
            return true;
        }

        if (element.TryGetProperty("crs", out var crsElement) &&
            crsElement.ValueKind == JsonValueKind.Object &&
            TryReadGeoJsonCrs(crsElement, out spatialReference))
        {
            return true;
        }

        spatialReference = default;
        return false;
    }

    private static bool TryReadFeatureServerSpatialReference(JsonElement spatialReferenceElement, out FeatureSpatialReference spatialReference)
    {
        if (TryGetInt32(spatialReferenceElement, "latestWkid", out var latestWkid) && latestWkid > 0)
        {
            spatialReference = FeatureSpatialReference.FromWkid(latestWkid);
            return true;
        }

        if (TryGetInt32(spatialReferenceElement, "wkid", out var wkid) && wkid > 0)
        {
            spatialReference = FeatureSpatialReference.FromWkid(wkid);
            return true;
        }

        if (TryGetString(spatialReferenceElement, "wkt", out var wkt))
        {
            spatialReference = FeatureSpatialReference.FromWkt(wkt);
            return true;
        }

        spatialReference = default;
        return false;
    }

    private static bool TryReadGeoJsonCrs(JsonElement crsElement, out FeatureSpatialReference spatialReference)
    {
        if (crsElement.TryGetProperty("properties", out var properties) &&
            TryGetString(properties, "name", out var name) &&
            TryParseCrsIdentifier(name, out spatialReference))
        {
            return true;
        }

        if (TryGetString(crsElement, "name", out name) &&
            TryParseCrsIdentifier(name, out spatialReference))
        {
            return true;
        }

        spatialReference = default;
        return false;
    }

    private static bool TryParseCrsIdentifier(string value, out FeatureSpatialReference spatialReference)
    {
        var normalized = NormalizeCrsIdentifier(value);
        if (normalized is not null &&
            normalized.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(normalized.AsSpan("EPSG:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epsgCode) &&
            epsgCode > 0)
        {
            spatialReference = FeatureSpatialReference.FromWkid(epsgCode);
            return true;
        }

        spatialReference = default;
        return false;
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static async Task ExecuteTransactionCommandAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task RollbackQuietlyAsync(SqliteConnection connection)
    {
        try
        {
            await ExecuteTransactionCommandAsync(connection, "ROLLBACK;", CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Preserve the original exception.
        }
    }

    private static async Task UpsertFeatureLayerMetadataAsync(
        SqliteConnection connection,
        string layerKey,
        FeatureSpatialReference spatialReference,
        bool isExplicit,
        CancellationToken ct)
    {
        await UpsertSpatialReferenceAsync(connection, spatialReference, ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO honua_feature_layers (layer_key, crs_identifier, srs_id, updated_at_utc)
VALUES ($layer_key, $crs_identifier, $srs_id, $updated_at_utc)
ON CONFLICT(layer_key) DO UPDATE SET
  crs_identifier = CASE WHEN $is_explicit = 1 THEN excluded.crs_identifier ELSE honua_feature_layers.crs_identifier END,
  srs_id = CASE WHEN $is_explicit = 1 THEN excluded.srs_id ELSE honua_feature_layers.srs_id END,
  updated_at_utc = CASE WHEN $is_explicit = 1 THEN excluded.updated_at_utc ELSE honua_feature_layers.updated_at_utc END;
";
        command.Parameters.AddWithValue("$layer_key", layerKey);
        command.Parameters.AddWithValue("$crs_identifier", spatialReference.CrsIdentifier);
        command.Parameters.AddWithValue("$srs_id", spatialReference.SrsId);
        command.Parameters.AddWithValue("$updated_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$is_explicit", isExplicit ? 1 : 0);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task UpsertSpatialReferenceAsync(
        SqliteConnection connection,
        FeatureSpatialReference spatialReference,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO gpkg_spatial_ref_sys (
  srs_name,
  srs_id,
  organization,
  organization_coordsys_id,
  definition,
  description)
VALUES (
  $srs_name,
  $srs_id,
  $organization,
  $organization_coordsys_id,
  $definition,
  $description)
ON CONFLICT(srs_id) DO UPDATE SET
  srs_name = excluded.srs_name,
  organization = excluded.organization,
  organization_coordsys_id = excluded.organization_coordsys_id,
  definition = excluded.definition,
  description = excluded.description;
";
        command.Parameters.AddWithValue("$srs_name", spatialReference.SrsName);
        command.Parameters.AddWithValue("$srs_id", spatialReference.SrsId);
        command.Parameters.AddWithValue("$organization", spatialReference.Organization);
        command.Parameters.AddWithValue("$organization_coordsys_id", spatialReference.OrganizationCoordsysId);
        command.Parameters.AddWithValue("$definition", spatialReference.Definition);
        command.Parameters.AddWithValue("$description", spatialReference.Description);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task EnsureQueryCrsMatchesLayerAsync(
        SqliteConnection connection,
        string layerKey,
        string queryCrs,
        CancellationToken ct)
    {
        var layerCrs = await ReadFeatureLayerCrsIdentifierAsync(connection, layerKey, ct).ConfigureAwait(false);
        if (layerCrs is null)
        {
            var backfilled = await TryBackfillFeatureLayerCrsAsync(connection, layerKey, ct).ConfigureAwait(false);
            if (backfilled is null)
            {
                return;
            }

            layerCrs = backfilled.Value.CrsIdentifier;
        }

        if (!string.Equals(NormalizeCrsIdentifier(layerCrs), NormalizeCrsIdentifier(queryCrs), StringComparison.OrdinalIgnoreCase))
        {
            throw GeoPackageStorageProblems.CrsMismatch(layerKey, layerCrs, queryCrs);
        }
    }

    private static async Task<string?> ReadFeatureLayerCrsIdentifierAsync(
        SqliteConnection connection,
        string layerKey,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT crs_identifier FROM honua_feature_layers WHERE layer_key = $layer_key LIMIT 1;";
        command.Parameters.AddWithValue("$layer_key", layerKey);

        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value as string;
    }

    private static async Task<FeatureSpatialReference?> TryBackfillFeatureLayerCrsAsync(
        SqliteConnection connection,
        string layerKey,
        CancellationToken ct)
    {
        var spatialReference = await TryReadCachedLayerSpatialReferenceAsync(connection, layerKey, ct).ConfigureAwait(false);
        if (spatialReference is null)
        {
            return null;
        }

        await UpsertFeatureLayerMetadataAsync(connection, layerKey, spatialReference.Value, isExplicit: true, ct)
            .ConfigureAwait(false);
        return spatialReference;
    }

    private static async Task<FeatureSpatialReference?> TryReadCachedLayerSpatialReferenceAsync(
        SqliteConnection connection,
        string layerKey,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT feature_json
FROM honua_features
WHERE layer_key = $layer_key
ORDER BY object_id ASC;
";
        command.Parameters.AddWithValue("$layer_key", layerKey);

        FeatureSpatialReference? layerSpatialReference = null;
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!TryReadCachedFeatureSpatialReference(reader.GetString(0), out var featureSpatialReference))
            {
                continue;
            }

            if (layerSpatialReference is null)
            {
                layerSpatialReference = featureSpatialReference;
                continue;
            }

            if (!string.Equals(
                    NormalizeCrsIdentifier(layerSpatialReference.Value.CrsIdentifier),
                    NormalizeCrsIdentifier(featureSpatialReference.CrsIdentifier),
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return layerSpatialReference;
    }

    private static bool TryReadCachedFeatureSpatialReference(string featureJson, out FeatureSpatialReference spatialReference)
    {
        try
        {
            using var doc = JsonDocument.Parse(featureJson);
            var root = doc.RootElement;
            var geometry = root.TryGetProperty("geometry", out var geometryElement)
                ? geometryElement
                : root;

            var value = ExtractFeatureSpatialReference(root, geometry);
            if (value is not null)
            {
                spatialReference = value.Value;
                return true;
            }
        }
        catch (JsonException)
        {
            // Legacy cache rows should not make otherwise queryable layers unreadable.
        }

        spatialReference = default;
        return false;
    }

    private static void ValidateBoundingBox(double minX, double minY, double maxX, double maxY)
    {
        if (!double.IsFinite(minX) ||
            !double.IsFinite(minY) ||
            !double.IsFinite(maxX) ||
            !double.IsFinite(maxY))
        {
            throw GeoPackageStorageProblems.InvalidBoundingBox("Bounding box coordinates must be finite.");
        }

        if (maxX < minX)
        {
            throw GeoPackageStorageProblems.InvalidBoundingBox("Bounding box max X must be greater than or equal to min X.");
        }

        if (maxY < minY)
        {
            throw GeoPackageStorageProblems.InvalidBoundingBox("Bounding box max Y must be greater than or equal to min Y.");
        }
    }

    private static string? NormalizeCrsIdentifier(string? crs)
    {
        if (string.IsNullOrWhiteSpace(crs))
        {
            return null;
        }

        var trimmed = crs.Trim();
        const string epsgPrefix = "EPSG:";
        var epsgIndex = trimmed.LastIndexOf(epsgPrefix, StringComparison.OrdinalIgnoreCase);
        if (epsgIndex >= 0 &&
            int.TryParse(trimmed.AsSpan(epsgIndex + epsgPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epsgCode))
        {
            return $"EPSG:{epsgCode}";
        }

        const string ogcCrsPrefix = "http://www.opengis.net/def/crs/EPSG/0/";
        if (trimmed.StartsWith(ogcCrsPrefix, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed.AsSpan(ogcCrsPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out epsgCode))
        {
            return $"EPSG:{epsgCode}";
        }

        return trimmed.ToUpperInvariant();
    }

    private static async Task UpsertFeatureIndexAsync(
        SqliteConnection connection,
        string layerKey,
        long objectId,
        FeatureExtent? extent,
        CancellationToken ct)
    {
        var indexId = await EnsureFeatureIndexIdAsync(connection, layerKey, objectId, ct).ConfigureAwait(false);
        if (extent is null)
        {
            await DeleteFeatureIndexAsync(connection, indexId, ct).ConfigureAwait(false);
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT OR REPLACE INTO rtree_honua_features (index_id, min_x, max_x, min_y, max_y)
VALUES ($index_id, $min_x, $max_x, $min_y, $max_y);
";
        command.Parameters.AddWithValue("$index_id", indexId);
        command.Parameters.AddWithValue("$min_x", extent.Value.MinX);
        command.Parameters.AddWithValue("$max_x", extent.Value.MaxX);
        command.Parameters.AddWithValue("$min_y", extent.Value.MinY);
        command.Parameters.AddWithValue("$max_y", extent.Value.MaxY);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<long> EnsureFeatureIndexIdAsync(
        SqliteConnection connection,
        string layerKey,
        long objectId,
        CancellationToken ct)
    {
        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.CommandText = @"
INSERT INTO honua_feature_index_keys (layer_key, object_id)
VALUES ($layer_key, $object_id)
ON CONFLICT(layer_key, object_id) DO NOTHING;
";
            insertCommand.Parameters.AddWithValue("$layer_key", layerKey);
            insertCommand.Parameters.AddWithValue("$object_id", objectId);
            await insertCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return await GetFeatureIndexIdAsync(connection, layerKey, objectId, ct).ConfigureAwait(false)
            ?? throw GeoPackageStorageProblems.WriteFailed(new InvalidOperationException("GeoPackage feature index key was not created."));
    }

    private static async Task<long?> GetFeatureIndexIdAsync(
        SqliteConnection connection,
        string layerKey,
        long objectId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT index_id
FROM honua_feature_index_keys
WHERE layer_key = $layer_key
  AND object_id = $object_id
LIMIT 1;
";
        command.Parameters.AddWithValue("$layer_key", layerKey);
        command.Parameters.AddWithValue("$object_id", objectId);
        var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task DeleteFeatureIndexAsync(SqliteConnection connection, long indexId, CancellationToken ct)
    {
        await using (var rtreeCommand = connection.CreateCommand())
        {
            rtreeCommand.CommandText = "DELETE FROM rtree_honua_features WHERE index_id = $index_id;";
            rtreeCommand.Parameters.AddWithValue("$index_id", indexId);
            await rtreeCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var keyCommand = connection.CreateCommand();
        keyCommand.CommandText = "DELETE FROM honua_feature_index_keys WHERE index_id = $index_id;";
        keyCommand.Parameters.AddWithValue("$index_id", indexId);
        await keyCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<long>> ReadExpiredFeatureIndexIdsAsync(
        SqliteConnection connection,
        string nowUtc,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT k.index_id
FROM honua_feature_index_keys k
JOIN honua_features f
  ON f.layer_key = k.layer_key
 AND f.object_id = k.object_id
WHERE f.expires_at_utc IS NOT NULL
  AND f.expires_at_utc <= $now_utc;
";
        command.Parameters.AddWithValue("$now_utc", nowUtc);

        var indexIds = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            indexIds.Add(reader.GetInt64(0));
        }

        return indexIds;
    }

    private static async Task DeleteFeatureIndexesAsync(
        SqliteConnection connection,
        IReadOnlyList<long> indexIds,
        CancellationToken ct)
    {
        if (indexIds.Count == 0)
        {
            return;
        }

        await using (var rtreeCommand = connection.CreateCommand())
        {
            var parameters = AddIndexIdParameters(rtreeCommand, indexIds);
            rtreeCommand.CommandText = $"DELETE FROM rtree_honua_features WHERE index_id IN ({parameters});";
            await rtreeCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var keyCommand = connection.CreateCommand();
        var keyParameters = AddIndexIdParameters(keyCommand, indexIds);
        keyCommand.CommandText = $"DELETE FROM honua_feature_index_keys WHERE index_id IN ({keyParameters});";
        await keyCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ReadPendingOperationIdsAsync(
        SqliteConnection connection,
        int maxCount,
        DateTimeOffset staleClaimCutoffUtc,
        string? layerKeyPrefix,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT operation_id
FROM honua_sync_queue
WHERE ($layer_key_prefix IS NULL OR layer_key LIKE $layer_key_prefix_like ESCAPE '\')
  AND (
    status IN ('pending', 'retry')
    OR (
        status = 'in_progress'
        AND (
          claimed_at_utc IS NULL
          OR claimed_at_utc <= $stale_claim_cutoff_utc
        )
    )
  )
ORDER BY priority ASC, created_at_utc ASC
LIMIT $limit;
";
        command.Parameters.AddWithValue("$limit", maxCount);
        command.Parameters.AddWithValue("$stale_claim_cutoff_utc", staleClaimCutoffUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$layer_key_prefix", ToDbValue(layerKeyPrefix));
        command.Parameters.AddWithValue("$layer_key_prefix_like", layerKeyPrefix is null ? DBNull.Value : EscapeLikePattern(layerKeyPrefix) + "%");

        var operationIds = new List<string>(capacity: maxCount);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            operationIds.Add(reader.GetString(0));
        }

        return operationIds;
    }

    private static string EscapeLikePattern(string value)
        => value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    private static OfflineEditOperation ReadOfflineEditOperation(SqliteDataReader reader)
    {
        var operationType = Enum.Parse<OfflineOperationType>(reader.GetString(3), ignoreCase: true);
        var createdAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        return new OfflineEditOperation
        {
            OperationId = reader.GetString(0),
            LayerKey = reader.GetString(1),
            TargetCollection = reader.GetString(2),
            OperationType = operationType,
            PayloadJson = reader.GetString(4),
            Priority = reader.GetInt32(5),
            CreatedAtUtc = createdAt,
            AttemptCount = reader.GetInt32(7),
        };
    }

    private static ScenePackageRecord ReadScenePackageRecord(SqliteDataReader reader)
    {
        var extentJson = reader.IsDBNull(5) ? null : reader.GetString(5);
        var extent = string.IsNullOrWhiteSpace(extentJson)
            ? null
            : HonuaMobileOfflineJson.Deserialize<HonuaSceneBounds>(extentJson)
                ?? throw new InvalidOperationException("Invalid extent payload in honua_scene_packages.");

        return new ScenePackageRecord
        {
            PackageId = reader.GetString(0),
            SceneId = reader.GetString(1),
            DisplayName = reader.IsDBNull(2) ? null : reader.GetString(2),
            EditionGate = reader.GetString(3),
            ServerRevision = reader.GetString(4),
            Extent = extent,
            PackageDirectory = reader.GetString(6),
            ManifestPath = reader.GetString(7),
            State = Enum.Parse<HonuaScenePackageState>(reader.GetString(8), ignoreCase: true),
            DeclaredBytes = reader.GetInt64(9),
            DownloadedBytes = reader.GetInt64(10),
            RequiredAssetCount = reader.GetInt32(11),
            DownloadedAssetCount = reader.GetInt32(12),
            MissingOptionalAssetKeys = ReadStringArrayJson(reader.GetString(13)),
            StaleAfterUtc = ReadNullableDateTimeOffset(reader, 14),
            OfflineUseExpiresAtUtc = ReadNullableDateTimeOffset(reader, 15),
            AuthExpiresAtUtc = ReadNullableDateTimeOffset(reader, 16),
            UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(17), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        };
    }

    private static IReadOnlyList<string> ReadStringArrayJson(string json)
        => HonuaMobileOfflineJson.Deserialize<string[]>(json) ?? [];

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static object ToDbValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static object ToDbValue(DateTimeOffset? value)
        => value.HasValue
            ? value.Value.ToString("O", CultureInfo.InvariantCulture)
            : DBNull.Value;

    private static object ToDbValue(double? value)
        => value.HasValue ? value.Value : DBNull.Value;

    private static string AddOperationIdParameters(SqliteCommand command, IReadOnlyList<string> operationIds)
    {
        var parameterNames = new string[operationIds.Count];
        for (var i = 0; i < operationIds.Count; i++)
        {
            var parameterName = $"$id_{i}";
            command.Parameters.AddWithValue(parameterName, operationIds[i]);
            parameterNames[i] = parameterName;
        }

        return string.Join(", ", parameterNames);
    }

    private static string AddIndexIdParameters(SqliteCommand command, IReadOnlyList<long> indexIds)
    {
        var parameterNames = new string[indexIds.Count];
        for (var i = 0; i < indexIds.Count; i++)
        {
            var parameterName = $"$index_id_{i}";
            command.Parameters.AddWithValue(parameterName, indexIds[i]);
            parameterNames[i] = parameterName;
        }

        return string.Join(", ", parameterNames);
    }

    private readonly record struct FeatureCacheRecord(
        long ObjectId,
        FeatureExtent? Extent,
        FeatureSpatialReference? SpatialReference);

    private readonly record struct FeatureExtent(double MinX, double MinY, double MaxX, double MaxY);

    private struct FeatureExtentBuilder
    {
        private double _minX;
        private double _minY;
        private double _maxX;
        private double _maxY;

        public bool HasValue { get; private set; }

        public void Include(double x, double y)
        {
            if (!HasValue)
            {
                _minX = _maxX = x;
                _minY = _maxY = y;
                HasValue = true;
                return;
            }

            _minX = Math.Min(_minX, x);
            _minY = Math.Min(_minY, y);
            _maxX = Math.Max(_maxX, x);
            _maxY = Math.Max(_maxY, y);
        }

        public readonly FeatureExtent ToFeatureExtent() => new(_minX, _minY, _maxX, _maxY);
    }

    private readonly record struct FeatureSpatialReference(
        int SrsId,
        string CrsIdentifier,
        string SrsName,
        string Organization,
        int OrganizationCoordsysId,
        string Definition,
        string Description)
    {
        public static FeatureSpatialReference Default => new(
            DefaultSrsId,
            DefaultCrsIdentifier,
            "WGS 84 geodetic",
            "EPSG",
            DefaultSrsId,
            Wgs84Definition,
            "longitude/latitude coordinates in decimal degrees on the WGS 84 spheroid");

        public static FeatureSpatialReference FromWkid(int wkid)
        {
            if (wkid is 3857 or 102100 or 102113)
            {
                return new FeatureSpatialReference(
                    3857,
                    "EPSG:3857",
                    "WGS 84 / Pseudo-Mercator",
                    "EPSG",
                    3857,
                    WebMercatorDefinition,
                    "Web Mercator projected coordinates in metres.");
            }

            if (wkid == DefaultSrsId)
            {
                return Default;
            }

            return new FeatureSpatialReference(
                wkid,
                $"EPSG:{wkid}",
                $"EPSG {wkid}",
                "EPSG",
                wkid,
                "undefined",
                $"Spatial reference observed from source metadata as EPSG:{wkid}.");
        }

        public static FeatureSpatialReference FromWkt(string wkt)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(wkt));
            var srsId = 1_000_000_000 + (int)(BitConverter.ToUInt32(hash, 0) % 1_000_000_000);
            return new FeatureSpatialReference(
                srsId,
                $"WKT:{srsId}",
                $"Honua source WKT {srsId}",
                "NONE",
                srsId,
                wkt,
                "Spatial reference observed from source WKT metadata.");
        }
    }

    private static class GeoPackageStorageProblems
    {
        public static GeoPackageStorageException InvalidFeatureJson(Exception innerException)
            => Create(
                "invalid-feature-json",
                "GeoPackage feature cache received invalid feature JSON.",
                "The cached feature payload could not be parsed or did not include a supported object ID.",
                innerException);

        public static GeoPackageStorageException InvalidBoundingBox(string detail)
            => Create("invalid-bounding-box", "GeoPackage feature query received an invalid bounding box.", detail);

        public static GeoPackageStorageException CrsMismatch(string layerKey, string layerCrs, string queryCrs)
            => Create(
                "crs-mismatch",
                "GeoPackage feature query CRS does not match the cached layer CRS.",
                $"Layer '{layerKey}' is cached as '{layerCrs}', but the query was '{queryCrs}'.");

        public static GeoPackageStorageException WriteFailed(Exception innerException)
            => Create(
                "write-failed",
                "GeoPackage feature cache write failed.",
                "The local GeoPackage provider rejected the feature cache write.",
                innerException);

        public static GeoPackageStorageException DeleteFailed(Exception innerException)
            => Create(
                "delete-failed",
                "GeoPackage feature cache delete failed.",
                "The local GeoPackage provider rejected the feature cache delete.",
                innerException);

        public static GeoPackageStorageException QueryFailed(Exception innerException)
            => Create(
                "query-failed",
                "GeoPackage feature cache query failed.",
                "The local GeoPackage provider rejected the feature cache query.",
                innerException);

        public static GeoPackageStorageException EvictionFailed(Exception innerException)
            => Create(
                "eviction-failed",
                "GeoPackage feature cache eviction failed.",
                "The local GeoPackage provider rejected the cache eviction.",
                innerException);

        private static GeoPackageStorageException Create(string code, string title, string detail)
            => new(new GeoPackageStorageProblem
            {
                Type = $"https://honua.io/problems/mobile/storage/{code}",
                Title = title,
                Code = code,
                Detail = detail,
            });

        private static GeoPackageStorageException Create(string code, string title, string detail, Exception innerException)
            => new(new GeoPackageStorageProblem
            {
                Type = $"https://honua.io/problems/mobile/storage/{code}",
                Title = title,
                Code = code,
                Detail = detail,
            }, innerException);
    }

    private static void ValidateSqlIdentifier(string identifier, string parameterName)
    {
        if (!Regex.IsMatch(identifier, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            throw new ArgumentException($"Invalid SQL identifier: '{identifier}'", parameterName);
        }
    }

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken ct)
    {
        ValidateSqlIdentifier(tableName, nameof(tableName));
        ValidateSqlIdentifier(columnName, nameof(columnName));

        await using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText = $"PRAGMA table_info(\"{tableName}\");";

        await using var reader = await infoCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{columnName}\" {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
        };

        return new SqliteConnection(builder.ToString());
    }
}
