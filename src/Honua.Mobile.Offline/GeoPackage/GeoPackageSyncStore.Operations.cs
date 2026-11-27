// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>An immutable operation and its current exclusive lease token.</summary>
public sealed record OfflineOperationClaim(string OperationId, string DocumentId, string PayloadJson, string LeaseToken);

/// <summary>The next durable state of a claimed operation.</summary>
public enum OfflineOperationDisposition
{
    /// <summary>Retain a terminal success marker for idempotency.</summary>
    Succeeded,

    /// <summary>Release the claim so the same immutable operation can be retried.</summary>
    Retry,

    /// <summary>Retain a terminal rejection marker and the updated document.</summary>
    Rejected,
}

public sealed partial class GeoPackageSyncStore
{
    private const string OperationSchema = """
        CREATE TABLE IF NOT EXISTS honua_local_operations (
            collection TEXT NOT NULL,
            operation_id TEXT NOT NULL,
            document_id TEXT NOT NULL,
            payload_json TEXT,
            payload_sha256 BLOB NOT NULL,
            disposition TEXT NOT NULL DEFAULT 'pending',
            lease_token TEXT,
            lease_until_utc TEXT,
            PRIMARY KEY (collection, operation_id)
        );
        CREATE UNIQUE INDEX IF NOT EXISTS honua_local_operations_active_document
            ON honua_local_operations (collection, document_id) WHERE disposition = 'pending';
        """;

    /// <summary>
    /// Atomically updates a document and queues its immutable operation.
    /// A null expected value requires a missing document. Returns false if the document changed
    /// or another operation is pending for it. Identical operation replays leave the document untouched.
    /// </summary>
    public Task<bool> EnqueueOperationAsync(
        string collection, string operationId, OfflineDocument document, string payloadJson,
        string? expectedDocumentJson, CancellationToken ct = default) =>
        EnqueueOperationCoreAsync(collection, operationId, document, payloadJson, expectedDocumentJson, ct);

    /// <summary>Queues an operation for a document whose existing JSON still matches the given snapshot.</summary>
    public Task<bool> AdoptOperationAsync(
        string collection, string operationId, OfflineDocument existingDocument, string payloadJson,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(existingDocument);
        ArgumentNullException.ThrowIfNull(existingDocument.Json);
        return EnqueueOperationCoreAsync(collection, operationId, existingDocument, payloadJson, existingDocument.Json, ct);
    }

    private async Task<bool> EnqueueOperationCoreAsync(
        string collection, string operationId, OfflineDocument document, string payloadJson,
        string? expectedDocumentJson, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ValidateDocument(document);
        ArgumentNullException.ThrowIfNull(payloadJson);
        var payloadDigest = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        await using (var existing = connection.CreateCommand())
        {
            existing.Transaction = transaction;
            existing.CommandText = """
                SELECT document_id, payload_sha256 FROM honua_local_operations
                WHERE collection = $collection AND operation_id = $operation;
                """;
            existing.Parameters.AddWithValue("$collection", collection);
            existing.Parameters.AddWithValue("$operation", operationId);
            await using var reader = await existing.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (reader.GetString(0) != document.Id || !reader.GetFieldValue<byte[]>(1).AsSpan().SequenceEqual(payloadDigest))
                {
                    throw new InvalidOperationException("The operation ID already identifies another immutable operation in this collection.");
                }

                return true;
            }
        }

        if (!await DocumentMatchesAsync(connection, transaction, collection, document.Id, expectedDocumentJson, ct)
            .ConfigureAwait(false))
        {
            return false;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO honua_local_operations (collection, operation_id, document_id, payload_json, payload_sha256)
            VALUES ($collection, $operation, $document, $payload, $digest)
            ON CONFLICT DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$collection", collection);
        insert.Parameters.AddWithValue("$operation", operationId);
        insert.Parameters.AddWithValue("$document", document.Id);
        insert.Parameters.AddWithValue("$payload", payloadJson);
        insert.Parameters.AddWithValue("$digest", payloadDigest);
        if (await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            return false;
        }

        if (document.Json != expectedDocumentJson)
        {
            await SaveDocumentCoreAsync(connection, transaction, collection, document, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Claims a pending operation for a document, including recovery after a prior lease expires.</summary>
    public Task<OfflineOperationClaim?> ClaimOperationAsync(
        string collection, string documentId, TimeSpan leaseDuration, CancellationToken ct = default) =>
        ClaimOperationCoreAsync(collection, documentId, null, leaseDuration, ct);

    /// <summary>Claims a pending operation only when it matches the expected operation ID.</summary>
    public Task<OfflineOperationClaim?> ClaimOperationAsync(
        string collection, string documentId, string expectedOperationId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOperationId);
        return ClaimOperationCoreAsync(collection, documentId, expectedOperationId, leaseDuration, ct);
    }

    private async Task<OfflineOperationClaim?> ClaimOperationCoreAsync(
        string collection, string documentId, string? expectedOperationId, TimeSpan leaseDuration, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        var now = _options.TimeProvider.GetUtcNow();
        var token = Guid.NewGuid().ToString("N");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE honua_local_operations SET lease_token = $token, lease_until_utc = $until
            WHERE collection = $collection AND document_id = $document AND disposition = 'pending'
              AND ($operation IS NULL OR operation_id = $operation)
              AND (lease_until_utc IS NULL OR lease_until_utc <= $now)
            RETURNING operation_id, payload_json;
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$document", documentId);
        command.Parameters.AddWithValue("$operation", (object?)expectedOperationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$token", token);
        command.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$until", (now + leaseDuration).ToString("O", CultureInfo.InvariantCulture));

        OfflineOperationClaim? claim = null;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                claim = new OfflineOperationClaim(reader.GetString(0), documentId, reader.GetString(1), token);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return claim;
    }

    /// <summary>
    /// Changes operation state and its document atomically while the lease and expected document are current.
    /// Returns false if either condition fails.
    /// </summary>
    public async Task<bool> CompleteOperationAsync(
        string collection, OfflineOperationClaim claim, OfflineDocument document, string expectedDocumentJson,
        OfflineOperationDisposition disposition, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(claim);
        ValidateDocument(document);
        ArgumentNullException.ThrowIfNull(expectedDocumentJson);
        if (claim.DocumentId != document.Id)
        {
            throw new ArgumentException("The document must match the claim.", nameof(document));
        }

        var state = disposition switch
        {
            OfflineOperationDisposition.Succeeded => "succeeded",
            OfflineOperationDisposition.Retry => "pending",
            OfflineOperationDisposition.Rejected => "rejected",
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };
        await using var connection = OpenConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();
        if (!await DocumentMatchesAsync(connection, transaction, collection, document.Id, expectedDocumentJson, ct)
            .ConfigureAwait(false))
        {
            return false;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE honua_local_operations
            SET disposition = $state, lease_token = NULL, lease_until_utc = NULL,
                payload_json = CASE WHEN $state = 'pending' THEN payload_json ELSE NULL END
            WHERE collection = $collection AND operation_id = $operation AND document_id = $document
              AND disposition = 'pending' AND lease_token = $token AND lease_until_utc > $now;
            """;
        command.Parameters.AddWithValue("$collection", collection);
        command.Parameters.AddWithValue("$operation", claim.OperationId);
        command.Parameters.AddWithValue("$document", document.Id);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$token", claim.LeaseToken);
        command.Parameters.AddWithValue("$now", _options.TimeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
        {
            return false;
        }

        await SaveDocumentCoreAsync(connection, transaction, collection, document, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }
}
