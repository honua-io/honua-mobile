// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.GeoPackage;

public sealed partial class GeoPackageSyncStore
{
    public async Task MarkUploadedAsync(string operationId, IReadOnlyList<string> serverFeatureIds, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(serverFeatureIds);
        var ids = new JsonArray();
        foreach (var id in serverFeatureIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ids.Add(id);
        }

        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
INSERT INTO honua_upload_acknowledgements
    (operation_id, layer_key, target_collection, server_feature_ids_json, uploaded_at_utc)
SELECT operation_id, layer_key, target_collection, $ids, $uploaded_at
FROM honua_sync_queue WHERE operation_id = $operation_id;
DELETE FROM honua_sync_queue WHERE operation_id = $operation_id;
";
        command.Parameters.AddWithValue("$operation_id", operationId);
        command.Parameters.AddWithValue("$ids", ids.ToJsonString());
        command.Parameters.AddWithValue("$uploaded_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<OfflineUploadAcknowledgement?> GetUploadAcknowledgementAsync(string operationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
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
