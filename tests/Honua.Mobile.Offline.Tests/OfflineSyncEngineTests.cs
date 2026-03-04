using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;

namespace Honua.Mobile.Offline.Tests;

public sealed class OfflineSyncEngineTests : IDisposable
{
    private readonly string _databasePath;

    public OfflineSyncEngineTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-sync-{Guid.NewGuid():N}.gpkg");
    }

    [Fact]
    public async Task SyncAsync_ClientWins_RetriesConflictWithForceWrite()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "conflict-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{\"id\":1}",
            Priority = 1,
        });

        var uploader = new ConflictThenSuccessUploader();
        var engine = new OfflineSyncEngine(
            store,
            uploader,
            new OfflineSyncEngineOptions { ConflictStrategy = SyncConflictStrategy.ClientWins });

        var result = await engine.SyncAsync();
        var remaining = await store.CountPendingAsync();

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, remaining);
        Assert.Equal(2, uploader.CallCount);
    }

    [Fact]
    public async Task SyncAsync_ManualReview_LeavesConflictAsFailed()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "manual-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 1,
        });

        var uploader = new AlwaysConflictUploader();
        var engine = new OfflineSyncEngine(
            store,
            uploader,
            new OfflineSyncEngineOptions { ConflictStrategy = SyncConflictStrategy.ManualReview });

        var result = await engine.SyncAsync();
        var pending = await store.GetPendingAsync(10);

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Empty(pending);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private sealed class ConflictThenSuccessUploader : IOfflineOperationUploader
    {
        public int CallCount { get; private set; }

        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
        {
            CallCount++;

            if (!forceWrite)
            {
                return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Conflict, Message = "version conflict" });
            }

            return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success });
        }
    }

    private sealed class AlwaysConflictUploader : IOfflineOperationUploader
    {
        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
            => Task.FromResult(new UploadResult { Outcome = UploadOutcome.Conflict, Message = "conflict" });
    }
}
