using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.GeoPackage;

public sealed class GeoPackageSyncStore : IGeoPackageSyncStore
{
    private readonly GeoPackageSyncStoreOptions _options;

    public GeoPackageSyncStore(GeoPackageSyncStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

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

CREATE TABLE IF NOT EXISTS honua_sync_queue (
    operation_id TEXT PRIMARY KEY,
    layer_key TEXT NOT NULL,
    target_collection TEXT NOT NULL,
    operation_type TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    priority INTEGER NOT NULL,
    created_at_utc TEXT NOT NULL,
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

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_sync_queue', 'attributes', 'honua_sync_queue', 'Offline edit queue for Honua mobile sync', 4326);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_map_areas', 'attributes', 'honua_map_areas', 'Downloaded map area package catalog', 4326);
";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(OfflineEditOperation operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO honua_sync_queue (operation_id, layer_key, target_collection, operation_type, payload_json, priority, created_at_utc, attempt_count, status)
VALUES ($operation_id, $layer_key, $target_collection, $operation_type, $payload_json, $priority, $created_at_utc, $attempt_count, 'pending')
ON CONFLICT(operation_id) DO UPDATE SET
  layer_key = excluded.layer_key,
  target_collection = excluded.target_collection,
  operation_type = excluded.operation_type,
  payload_json = excluded.payload_json,
  priority = excluded.priority,
  created_at_utc = excluded.created_at_utc,
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

    public async Task<IReadOnlyList<OfflineEditOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default)
    {
        if (maxCount <= 0)
        {
            return [];
        }

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT operation_id, layer_key, target_collection, operation_type, payload_json, priority, created_at_utc, attempt_count
FROM honua_sync_queue
WHERE status IN ('pending', 'retry')
ORDER BY priority ASC, created_at_utc ASC
LIMIT $limit;
";
        command.Parameters.AddWithValue("$limit", maxCount);

        var items = new List<OfflineEditOperation>(capacity: maxCount);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var operationType = Enum.Parse<OfflineOperationType>(reader.GetString(3), ignoreCase: true);
            var createdAt = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            items.Add(new OfflineEditOperation
            {
                OperationId = reader.GetString(0),
                LayerKey = reader.GetString(1),
                TargetCollection = reader.GetString(2),
                OperationType = operationType,
                PayloadJson = reader.GetString(4),
                Priority = reader.GetInt32(5),
                CreatedAtUtc = createdAt,
                AttemptCount = reader.GetInt32(7),
            });
        }

        return items;
    }

    public async Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM honua_sync_queue WHERE status IN ('pending', 'retry');";
        var count = (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L);
        return (int)count;
    }

    public async Task MarkSucceededAsync(string operationId, CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM honua_sync_queue WHERE operation_id = $operation_id;";
        command.Parameters.AddWithValue("$operation_id", operationId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(string operationId, string failureReason, bool retryable, CancellationToken ct = default)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE honua_sync_queue
SET status = $status,
    attempt_count = attempt_count + 1,
    last_error = $last_error
WHERE operation_id = $operation_id;
";

        command.Parameters.AddWithValue("$status", retryable ? "retry" : "failed");
        command.Parameters.AddWithValue("$last_error", failureReason);
        command.Parameters.AddWithValue("$operation_id", operationId);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

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

    public async Task UpsertMapAreaAsync(MapAreaPackage mapArea, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mapArea);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var bbox = JsonSerializer.Serialize(mapArea.BoundingBox);

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
            var bbox = JsonSerializer.Deserialize<BoundingBox>(reader.GetString(2))
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

    private SqliteConnection OpenConnection() => new($"Data Source={_options.DatabasePath}");
}
