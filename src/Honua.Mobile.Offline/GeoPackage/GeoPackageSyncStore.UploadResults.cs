// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.GeoPackage;

public sealed partial class GeoPackageSyncStore
{
    // Blank IDs are ignored: the server already applied the edit, so a malformed ID must not block the queue.
    // A missing queue row is a no-op when an acknowledgement exists; with neither, the IDs would be lost, so throw.
    public async Task MarkUploadedAsync(string operationId, IReadOnlyList<string> serverFeatureIds, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(serverFeatureIds);

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: false);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = @"
INSERT INTO honua_upload_acknowledgements
    (operation_id, layer_key, target_collection, server_feature_ids_json, uploaded_at_utc)
SELECT operation_id, layer_key, target_collection, $ids, $uploaded_at
FROM honua_sync_queue WHERE operation_id = $operation_id;
";
        insert.Parameters.AddWithValue("$operation_id", operationId);
        insert.Parameters.AddWithValue("$ids", ToNonBlankIdsJson(serverFeatureIds));
        insert.Parameters.AddWithValue("$uploaded_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var inserted = await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (inserted == 0 && !await AcknowledgementExistsAsync(transaction, operationId, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"Operation '{operationId}' is no longer queued and has no acknowledgement; its server feature identifiers were not recorded.");
        }

        await DeleteQueuedOperationAsync(transaction, operationId, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    // Returns null for a database not yet initialized with acknowledgement support.
    public async Task<OfflineUploadAcknowledgement?> GetUploadAcknowledgementAsync(string operationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'honua_upload_acknowledgements';";
            if ((long)(await exists.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L) == 0)
            {
                return null;
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT layer_key, target_collection, server_feature_ids_json, uploaded_at_utc
FROM honua_upload_acknowledgements WHERE operation_id = $operation_id;
";
        command.Parameters.AddWithValue("$operation_id", operationId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        using var ids = JsonDocument.Parse(reader.GetString(2));
        return new OfflineUploadAcknowledgement
        {
            OperationId = operationId,
            LayerKey = reader.GetString(0),
            TargetCollection = reader.GetString(1),
            ServerFeatureIds = ids.RootElement.EnumerateArray().Select(id => id.GetString()!).ToArray(),
            UploadedAtUtc = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
        };
    }

    private static string ToNonBlankIdsJson(IReadOnlyList<string> serverFeatureIds)
    {
        var ids = new JsonArray();
        foreach (var id in serverFeatureIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            ids.Add(id);
        }

        return ids.ToJsonString();
    }

    private static async Task<bool> AcknowledgementExistsAsync(SqliteTransaction transaction, string operationId, CancellationToken ct)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM honua_upload_acknowledgements WHERE operation_id = $operation_id;";
        command.Parameters.AddWithValue("$operation_id", operationId);
        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L) != 0;
    }

    private static async Task DeleteQueuedOperationAsync(SqliteTransaction transaction, string operationId, CancellationToken ct)
    {
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM honua_sync_queue WHERE operation_id = $operation_id;";
        command.Parameters.AddWithValue("$operation_id", operationId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task InitializeUploadResultsAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
CREATE TABLE IF NOT EXISTS honua_upload_acknowledgements (
    operation_id TEXT PRIMARY KEY,
    layer_key TEXT NOT NULL,
    target_collection TEXT NOT NULL,
    server_feature_ids_json TEXT NOT NULL,
    uploaded_at_utc TEXT NOT NULL
);

INSERT OR IGNORE INTO gpkg_contents (table_name, data_type, identifier, description)
VALUES ('honua_upload_acknowledgements', 'attributes', 'honua_upload_acknowledgements', 'Durable upload results');

";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task RejectAcknowledgedOperationAsync(SqliteConnection connection, string operationId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM honua_upload_acknowledgements WHERE operation_id = $operation_id
) THEN 1 ELSE 0 END;";
        command.Parameters.AddWithValue("$operation_id", operationId);
        if ((long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false) ?? 0L) != 0)
        {
            throw new InvalidOperationException("An acknowledged operation ID cannot be reused; enqueue a new operation ID.");
        }
    }
}
