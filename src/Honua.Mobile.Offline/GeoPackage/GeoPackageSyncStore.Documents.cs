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
    /// <exception cref="InvalidOperationException">A document being imported has a pending operation.</exception>
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
            ValidateDocument(document);
            ThrowIfDocumentHasPendingOperation(
                await HasPendingOperationAsync(connection, transaction, collection, document.Id, ct).ConfigureAwait(false));
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

    /// <summary>Reads one opaque document by ID.</summary>
    public async Task<OfflineDocument?> LoadDocumentAsync(string collection, string documentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        var json = await ReadDocumentJsonAsync(connection, null, collection, documentId, ct).ConfigureAwait(false);
        return json is null ? null : new OfflineDocument(documentId, json);
    }

    /// <summary>
    /// Updates a document only if its current JSON matches and it has no pending operation.
    /// A null expected value requires a missing document.
    /// </summary>
    public Task<bool> CompareExchangeDocumentAsync(
        string collection, OfflineDocument document, string? expectedJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ValidateDocument(document);
        return ChangeDocumentAsync(collection, document.Id, document, expectedJson, compare: true, ct);
    }

    /// <summary>Deletes a matching document only if it has no pending operation.</summary>
    public Task<bool> CompareDeleteDocumentAsync(
        string collection, string documentId, string expectedJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(expectedJson);
        return ChangeDocumentAsync(collection, documentId, null, expectedJson, compare: true, ct);
    }

    private async Task<bool> ChangeDocumentAsync(
        string collection, string documentId, OfflineDocument? document, string? expectedJson, bool compare, CancellationToken ct)
    {
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        if (compare && !await DocumentMatchesAsync(connection, transaction, collection, documentId, expectedJson, ct).ConfigureAwait(false))
        {
            return false;
        }

        if (await HasPendingOperationAsync(connection, transaction, collection, documentId, ct).ConfigureAwait(false))
        {
            return false;
        }

        if (document is null)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.Parameters.AddWithValue("$collection", collection);
            command.Parameters.AddWithValue("$document", documentId);
            command.CommandText = """
                DELETE FROM honua_local_documents WHERE collection = $collection AND document_id = $document;
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await SaveDocumentCoreAsync(connection, transaction, collection, document, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Inserts or replaces an opaque document.</summary>
    /// <exception cref="InvalidOperationException">The document has a pending operation.</exception>
    public async Task SaveDocumentAsync(string collection, OfflineDocument document, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ValidateDocument(document);
        ThrowIfDocumentHasPendingOperation(
            !await ChangeDocumentAsync(collection, document.Id, document, null, compare: false, ct).ConfigureAwait(false));
    }

    /// <summary>Reads one opaque document, or returns null if it does not exist.</summary>
    public async Task<OfflineDocument?> GetDocumentAsync(string collection, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document_json FROM honua_local_documents WHERE collection = $collection AND document_id = $id;
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$id", id);
        var json = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return json is null ? null : new OfflineDocument(id, json);
    }

    /// <summary>Replaces one document if its stored JSON matches the expected value. Null expects a missing document.</summary>
    public async Task<bool> CompareExchangeDocumentAsync(
        string collection, OfflineDocument document, string? expectedJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Id);
        ArgumentNullException.ThrowIfNull(document.Json);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = expectedJson is null
            ? """
                INSERT INTO honua_local_documents (collection, document_id, document_json)
                VALUES ($collection, $id, $json) ON CONFLICT(collection, document_id) DO NOTHING;
                """
            : """
                UPDATE honua_local_documents SET document_json = $json
                WHERE collection = $collection AND document_id = $id AND document_json = $expected;
                """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$id", document.Id);
        command.Parameters.AddWithValue("$json", document.Json);
        if (expectedJson is not null)
        {
            command.Parameters.AddWithValue("$expected", expectedJson);
        }

        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    /// <summary>Deletes an opaque document by ID.</summary>
    /// <exception cref="InvalidOperationException">The document has a pending operation.</exception>
    public async Task DeleteDocumentAsync(string collection, string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ThrowIfDocumentHasPendingOperation(
            !await ChangeDocumentAsync(collection, id, null, null, compare: false, ct).ConfigureAwait(false));
    }

    private static void ThrowIfDocumentHasPendingOperation(bool hasPendingOperation)
    {
        if (hasPendingOperation)
        {
            throw new InvalidOperationException("The document has a pending operation.");
        }
    }

    private static async Task<bool> HasPendingOperationAsync(
        SqliteConnection connection, SqliteTransaction transaction, string collection, string documentId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM honua_local_operations
            WHERE collection = $collection AND document_id = $document AND disposition = 'pending';
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$document", documentId);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    private static async Task SaveDocumentCoreAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string collection, OfflineDocument document, CancellationToken ct)
    {
        ValidateDocument(document);
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

    private static async Task<bool> DocumentMatchesAsync(
        SqliteConnection connection, SqliteTransaction transaction, string collection, string documentId,
        string? expectedJson, CancellationToken ct) =>
        await ReadDocumentJsonAsync(connection, transaction, collection, documentId, ct).ConfigureAwait(false) == expectedJson;

    private static async Task<string?> ReadDocumentJsonAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string collection, string documentId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT document_json FROM honua_local_documents WHERE collection = $collection AND document_id = $document;
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$document", documentId);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    private static void ValidateDocument(OfflineDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Id);
        ArgumentNullException.ThrowIfNull(document.Json);
    }
}
