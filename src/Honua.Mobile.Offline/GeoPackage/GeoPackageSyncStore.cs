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

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_sync_queue', 'attributes', 'honua_sync_queue', 'Offline edit queue for Honua mobile sync', 4326);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_map_areas', 'attributes', 'honua_map_areas', 'Downloaded map area package catalog', 4326);

CREATE TABLE IF NOT EXISTS honua_features (
    layer_key TEXT NOT NULL,
    object_id INTEGER NOT NULL,
    feature_json TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL,
    PRIMARY KEY (layer_key, object_id)
);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description, srs_id)
VALUES ('honua_features', 'attributes', 'honua_features', 'Replicated feature cache for delta sync', 4326);
";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await EnsureColumnExistsAsync(connection, "honua_sync_queue", "claimed_at_utc", "TEXT", ct).ConfigureAwait(false);
    }

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

    public async Task<IReadOnlyList<OfflineEditOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default)
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
            var claimedOperationIds = await ReadPendingOperationIdsAsync(connection, maxCount, staleClaimCutoffUtc, ct).ConfigureAwait(false);
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

    public async Task UpsertFeatureAsync(string layerKey, string featureJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureJson);

        var objectId = ExtractObjectId(featureJson);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO honua_features (layer_key, object_id, feature_json, updated_at_utc)
VALUES ($layer_key, $object_id, $feature_json, $updated_at_utc)
ON CONFLICT(layer_key, object_id) DO UPDATE SET
  feature_json = excluded.feature_json,
  updated_at_utc = excluded.updated_at_utc;
";

        command.Parameters.AddWithValue("$layer_key", layerKey);
        command.Parameters.AddWithValue("$object_id", objectId);
        command.Parameters.AddWithValue("$feature_json", featureJson);
        command.Parameters.AddWithValue("$updated_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteFeatureAsync(string layerKey, long objectId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM honua_features WHERE layer_key = $layer_key AND object_id = $object_id;";
        command.Parameters.AddWithValue("$layer_key", layerKey);
        command.Parameters.AddWithValue("$object_id", objectId);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetFeaturesAsync(string layerKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerKey);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT feature_json FROM honua_features WHERE layer_key = $layer_key ORDER BY object_id ASC;";
        command.Parameters.AddWithValue("$layer_key", layerKey);

        var items = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            items.Add(reader.GetString(0));
        }

        return items;
    }

    private static long ExtractObjectId(string featureJson)
    {
        using var doc = JsonDocument.Parse(featureJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("attributes", out var attributes))
        {
            if (attributes.TryGetProperty("OBJECTID", out var oid) && oid.TryGetInt64(out var id))
            {
                return id;
            }

            if (attributes.TryGetProperty("objectid", out oid) && oid.TryGetInt64(out id))
            {
                return id;
            }

            if (attributes.TryGetProperty("ObjectID", out oid) && oid.TryGetInt64(out id))
            {
                return id;
            }

            if (attributes.TryGetProperty("FID", out oid) && oid.TryGetInt64(out id))
            {
                return id;
            }
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

    private static async Task ExecuteTransactionCommandAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ReadPendingOperationIdsAsync(
        SqliteConnection connection,
        int maxCount,
        DateTimeOffset staleClaimCutoffUtc,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT operation_id
FROM honua_sync_queue
WHERE status IN ('pending', 'retry')
   OR (
      status = 'in_progress'
      AND (
        claimed_at_utc IS NULL
        OR claimed_at_utc <= $stale_claim_cutoff_utc
      )
   )
ORDER BY priority ASC, created_at_utc ASC
LIMIT $limit;
";
        command.Parameters.AddWithValue("$limit", maxCount);
        command.Parameters.AddWithValue("$stale_claim_cutoff_utc", staleClaimCutoffUtc.ToString("O", CultureInfo.InvariantCulture));

        var operationIds = new List<string>(capacity: maxCount);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            operationIds.Add(reader.GetString(0));
        }

        return operationIds;
    }

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

    private static async Task EnsureColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken ct)
    {
        await using var infoCommand = connection.CreateCommand();
        infoCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await infoCommand.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private SqliteConnection OpenConnection() => new($"Data Source={_options.DatabasePath}");
}
