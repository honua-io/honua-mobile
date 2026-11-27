// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Text;
using Honua.Mobile.Offline.GeoPackage;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.Tests;

public sealed class OpaqueOperationQueueTests : IDisposable
{
    private const string EncryptionKey = "test-only-encryption-key";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"honua-operations-{Guid.NewGuid():N}.gpkg");

    [Fact]
    public async Task EncryptedRestart_RetainsRetryRejectionAndIdempotency()
    {
        const string collection = "worker-a";
        const string payload = "{\"snapshot\":\"sensitive-field-answers\"}";
        var queued = new OfflineDocument("record", "{\"state\":\"queued\"}");
        var retry = queued with { Json = "{\"state\":\"retry\"}" };
        var rejected = queued with { Json = "{\"state\":\"rejected\"}" };
        var first = CreateStore();
        await first.InitializeAsync();
        Assert.True(await first.EnqueueOperationAsync(collection, "receipt-1", queued, payload, null));
        Assert.False(await first.EnqueueOperationAsync(collection, "receipt-2", rejected, payload, queued.Json));
        Assert.False(await first.CompareExchangeDocumentAsync(collection, rejected, queued.Json));
        Assert.False(await first.CompareDeleteDocumentAsync(collection, queued.Id, queued.Json));

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.SaveDocumentAsync(collection, rejected));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.DeleteDocumentAsync(collection, queued.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.ImportDocumentCollectionAsync(collection, [rejected]));
        Assert.False(await first.IsDocumentCollectionImportedAsync(collection));

        var reopened = CreateStore();
        await reopened.InitializeAsync();
        var claim = Assert.IsType<OfflineOperationClaim>(
            await reopened.ClaimOperationAsync(collection, queued.Id, TimeSpan.FromMinutes(2)));
        Assert.Equal(payload, claim.PayloadJson);
        Assert.False(await reopened.CompleteOperationAsync(collection, claim, retry, "wrong snapshot", OfflineOperationDisposition.Retry));
        Assert.Equal(queued, await reopened.LoadDocumentAsync(collection, queued.Id));
        Assert.True(await reopened.CompleteOperationAsync(collection, claim, retry, queued.Json, OfflineOperationDisposition.Retry));

        var retried = Assert.IsType<OfflineOperationClaim>(
            await first.ClaimOperationAsync(collection, queued.Id, TimeSpan.FromMinutes(2)));
        Assert.Equal(claim.OperationId, retried.OperationId);
        Assert.Equal(claim.PayloadJson, retried.PayloadJson);
        Assert.True(await first.CompleteOperationAsync(collection, retried, rejected, retry.Json, OfflineOperationDisposition.Rejected));

        Assert.True(await reopened.EnqueueOperationAsync(collection, "receipt-1", queued, payload, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reopened.EnqueueOperationAsync(collection, "receipt-1", queued, "{}", null));
        Assert.Equal(rejected, await reopened.LoadDocumentAsync(collection, queued.Id));
        Assert.Null(await reopened.ClaimOperationAsync(collection, queued.Id, TimeSpan.FromMinutes(2)));
        Assert.False(await reopened.CompareDeleteDocumentAsync(collection, rejected.Id, queued.Json));
        Assert.True(await reopened.CompareDeleteDocumentAsync(collection, rejected.Id, rejected.Json));
        Assert.Null(await reopened.LoadDocumentAsync(collection, rejected.Id));
        Assert.True(await reopened.EnqueueOperationAsync(collection, "receipt-1", queued, payload, null));
        Assert.Null(await reopened.LoadDocumentAsync(collection, rejected.Id));
        await using (var connection = await OpenTestConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload_json FROM honua_local_operations WHERE operation_id = 'receipt-1';";
            Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
        }

        var bytes = await File.ReadAllBytesAsync(_path);
        Assert.False(bytes.AsSpan().StartsWith("SQLite format 3"u8));
        Assert.DoesNotContain("sensitive-field-answers", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredLease_CannotCompleteAReclaimedOperation_OrCrossCollection()
    {
        var clock = new TestClock();
        var store = CreateStore(clock);
        var otherStore = CreateStore(clock);
        await store.InitializeAsync();
        var queued = new OfflineDocument("record", "{\"state\":\"queued\"}");
        var sent = queued with { Json = "{\"state\":\"sent\"}" };
        Assert.True(await store.EnqueueOperationAsync("worker-a", "same-receipt", queued, "{}", null));
        Assert.True(await store.EnqueueOperationAsync("worker-b", "same-receipt", queued, "{}", null));
        var expired = Assert.IsType<OfflineOperationClaim>(
            await store.ClaimOperationAsync("worker-a", queued.Id, TimeSpan.FromMinutes(2)));
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.False(await store.CompleteOperationAsync("worker-a", expired, sent, queued.Json, OfflineOperationDisposition.Succeeded));

        var racers = await Task.WhenAll(
            Task.Run(() => store.ClaimOperationAsync("worker-a", queued.Id, TimeSpan.FromMinutes(2))),
            Task.Run(() => otherStore.ClaimOperationAsync("worker-a", queued.Id, TimeSpan.FromMinutes(2))));
        var winner = Assert.IsType<OfflineOperationClaim>(Assert.Single(racers, candidate => candidate is not null));
        Assert.NotEqual(expired.LeaseToken, winner.LeaseToken);
        Assert.False(await store.CompleteOperationAsync("worker-a", expired, sent, queued.Json, OfflineOperationDisposition.Retry));
        Assert.False(await store.CompleteOperationAsync("worker-b", winner, sent, queued.Json, OfflineOperationDisposition.Succeeded));
        Assert.True(await otherStore.CompleteOperationAsync("worker-a", winner, sent, queued.Json, OfflineOperationDisposition.Succeeded));
        Assert.Equal(sent, await CreateStore().LoadDocumentAsync("worker-a", queued.Id));
        Assert.Equal(queued, await store.LoadDocumentAsync("worker-b", queued.Id));
        Assert.Null(await store.ClaimOperationAsync("worker-a", queued.Id, TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public async Task LegacyDatabaseMigration_AdoptsOnlyTheMatchingDocument()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        const string collection = "legacy";
        var before = new OfflineDocument("record", "{\"state\":\"draft\"}");
        var current = before with { Json = "{\"state\":\"queued\"}" };
        await store.SaveDocumentAsync(collection, before);

        // Recreate the pre-queue schema while retaining the legacy encrypted document.
        await using (var connection = await OpenTestConnectionAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE honua_local_operations;";
            await command.ExecuteNonQueryAsync();
        }

        store = CreateStore();
        await store.InitializeAsync();
        Assert.Equal(before, await store.LoadDocumentAsync(collection, before.Id));
        Assert.True(await store.CompareExchangeDocumentAsync(collection, current, before.Json));
        Assert.False(await store.AdoptOperationAsync(collection, "receipt", before, "{}"));
        Assert.Null(await store.ClaimOperationAsync(collection, before.Id, TimeSpan.FromMinutes(2)));
        Assert.True(await store.AdoptOperationAsync(collection, "receipt", current, "{}"));
        Assert.Equal(current, await store.LoadDocumentAsync(collection, current.Id));
        Assert.Null(await store.ClaimOperationAsync(collection, current.Id, "stale-receipt", TimeSpan.FromMinutes(2)));
        Assert.Equal("receipt", (await store.ClaimOperationAsync(collection, current.Id, "receipt", TimeSpan.FromMinutes(2)))?.OperationId);
    }

    private async Task<SqliteConnection> OpenTestConnectionAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            Password = EncryptionKey,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private GeoPackageSyncStore CreateStore(TimeProvider? clock = null) => new(new GeoPackageSyncStoreOptions
    {
        DatabasePath = _path,
        EncryptionKey = EncryptionKey,
        TimeProvider = clock ?? TimeProvider.System,
    });

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            File.Delete(_path + suffix);
        }
    }
}
