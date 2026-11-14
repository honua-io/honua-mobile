// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Text;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.Tests;

public sealed class EncryptedGeoPackageSyncStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"honua-encrypted-{Guid.NewGuid():N}.gpkg");

    [Fact]
    public async Task EncryptedQueue_SurvivesReopen_WithoutPlaintextPayload()
    {
        const string payload = "sensitive-field-observation-do-not-store-in-plaintext";
        var store = CreateStore("test-only-encryption-key");
        await store.InitializeAsync();
        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "encrypted-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = $"{{\"note\":\"{payload}\"}}",
        });

        var reopened = CreateStore("test-only-encryption-key");
        await reopened.InitializeAsync();
        var operation = Assert.Single(await reopened.GetPendingAsync(10));
        Assert.Equal("encrypted-op", operation.OperationId);
        Assert.Contains(payload, operation.PayloadJson, StringComparison.Ordinal);
        var bytes = await File.ReadAllBytesAsync(_path);
        Assert.False(bytes.AsSpan().StartsWith("SQLite format 3"u8));
        Assert.DoesNotContain(payload, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EncryptedQueue_SyncAfterReopen_DoesNotUploadAgain()
    {
        var store = CreateStore("persisted-key");
        await store.InitializeAsync();
        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "upload-once",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = "{}",
        });

        var uploader = new RecordingUploader();
        var firstRun = await new OfflineSyncEngine(CreateStore("persisted-key"), uploader).SyncAsync();
        var secondRun = await new OfflineSyncEngine(CreateStore("persisted-key"), uploader).SyncAsync();

        Assert.Equal(1, firstRun.Succeeded);
        Assert.Equal(0, firstRun.Failed);
        Assert.Equal(0, secondRun.Loaded);
        Assert.Equal("upload-once", Assert.Single(uploader.OperationIds));
    }

    [Fact]
    public async Task EncryptedStore_RetainsUploadAcknowledgementAcrossReopen()
    {
        var store = CreateStore("acknowledgement-key");
        await store.InitializeAsync();
        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "encrypted-ack",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = "{}",
        });

        await store.MarkUploadedAsync("encrypted-ack", ["server-7"]);

        var reopened = CreateStore("acknowledgement-key");
        await reopened.InitializeAsync();
        Assert.Equal(0, await reopened.CountPendingAsync());
        Assert.Equal(["server-7"], (await reopened.GetUploadAcknowledgementAsync("encrypted-ack"))!.ServerFeatureIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("incorrect-key")]
    public async Task EncryptedQueue_RejectsMissingOrWrongKey_WithoutDamagingData(string? incorrectKey)
    {
        var store = CreateStore("correct-key");
        await store.InitializeAsync();
        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "preserved-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = "{\"note\":\"preserve pending data\"}",
        });
        await Assert.ThrowsAsync<SqliteException>(() => CreateStore(incorrectKey).InitializeAsync());
        var recovered = CreateStore("correct-key");
        await recovered.InitializeAsync();
        Assert.Equal("preserved-op", Assert.Single(await recovered.GetPendingAsync(10)).OperationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyKey_IsRejectedBeforeCreatingDatabase(string key)
    {
        Assert.Throws<ArgumentException>(() => CreateStore(key));
        Assert.False(File.Exists(_path));
    }

    private GeoPackageSyncStore CreateStore(string? key) => new(new GeoPackageSyncStoreOptions
    {
        DatabasePath = _path,
        EncryptionKey = key,
    });

    private sealed class RecordingUploader : IOfflineOperationUploader
    {
        public List<string> OperationIds { get; } = [];

        Task<UploadResult> IOfflineOperationUploader.UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct)
        {
            OperationIds.Add(operation.OperationId);
            return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success });
        }
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
