// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>An application-owned persisted document, identified by an opaque string.</summary>
public sealed record OfflineDocument(string Id, string Json);

public sealed partial class GeoPackageSyncStore
{
    private const string DocumentSchema = """
        CREATE TABLE IF NOT EXISTS honua_local_documents (
            collection TEXT NOT NULL,
            document_id TEXT NOT NULL,
            document_json TEXT NOT NULL,
            PRIMARY KEY (collection, document_id)
        );
        CREATE TABLE IF NOT EXISTS honua_local_document_imports (
            collection TEXT NOT NULL PRIMARY KEY
        );
        """;

    /// <summary>Whether this document collection's legacy import has committed.</summary>
    public async Task<bool> IsDocumentCollectionImportedAsync(string collection, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM honua_local_document_imports WHERE collection = $collection;";
        command.Parameters.AddWithValue("$collection", collection);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is 1L;
    }

    /// <summary>Atomically imports an application's legacy documents once, including for an empty collection.</summary>
    public async Task ImportDocumentCollectionAsync(
        string collection, IReadOnlyList<OfflineDocument> documents, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(documents);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO honua_local_document_imports (collection) VALUES ($collection)
            ON CONFLICT(collection) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$collection", collection);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
        {
            return;
        }

        foreach (var document in documents)
        {
            await SaveDocumentCoreAsync(connection, transaction, collection, document, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Lists the opaque documents in a collection.</summary>
    public async Task<IReadOnlyList<OfflineDocument>> LoadDocumentsAsync(string collection, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document_id, document_json FROM honua_local_documents
            WHERE collection = $collection ORDER BY document_id;
            """;
        command.Parameters.AddWithValue("$collection", collection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var documents = new List<OfflineDocument>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            documents.Add(new OfflineDocument(reader.GetString(0), reader.GetString(1)));
        }

        return documents;
    }

    /// <summary>Inserts or replaces an opaque document.</summary>
    public async Task SaveDocumentAsync(string collection, OfflineDocument document, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await SaveDocumentCoreAsync(connection, null, collection, document, ct).ConfigureAwait(false);
    }

    /// <summary>Deletes an opaque document by ID.</summary>
    public async Task DeleteDocumentAsync(string collection, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM honua_local_documents WHERE collection = $collection AND document_id = $id;
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task SaveDocumentCoreAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string collection, OfflineDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Id);
        ArgumentNullException.ThrowIfNull(document.Json);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO honua_local_documents (collection, document_id, document_json)
            VALUES ($collection, $id, $json)
            ON CONFLICT(collection, document_id) DO UPDATE SET document_json = excluded.document_json;
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$id", document.Id);
        command.Parameters.AddWithValue("$json", document.Json);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
