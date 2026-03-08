using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.Tests;

public sealed class GeoPackageSyncStoreTests : IDisposable
{
    private readonly string _databasePath;

    public GeoPackageSyncStoreTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-mobile-{Guid.NewGuid():N}.gpkg");
    }

    [Fact]
    public async Task EnqueueAndGetPending_UsesPriorityOrdering()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "op-2",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 20,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "op-1",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Add,
            PayloadJson = "{}",
            Priority = 5,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        var pending = await store.GetPendingAsync(10);

        Assert.Equal(2, pending.Count);
        Assert.Equal("op-1", pending[0].OperationId);
        Assert.Equal("op-2", pending[1].OperationId);
    }

    [Fact]
    public async Task GetPendingAsync_ClaimsRowsToPreventDuplicateProcessing()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "op-1",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 5,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        var firstClaim = store.GetPendingAsync(1);
        var secondClaim = store.GetPendingAsync(1);
        await Task.WhenAll(firstClaim, secondClaim);
        var firstClaimResult = await firstClaim;
        var secondClaimResult = await secondClaim;

        Assert.Equal(1, firstClaimResult.Count + secondClaimResult.Count);
    }

    [Fact]
    public async Task GetPendingAsync_ReclaimsStaleInProgressOperations()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = _databasePath,
            InProgressLeaseTimeout = TimeSpan.FromMilliseconds(150),
        });
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "stale-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        var firstClaim = await store.GetPendingAsync(1);
        Assert.Single(firstClaim);
        Assert.Equal("stale-op", firstClaim[0].OperationId);

        var immediateClaim = await store.GetPendingAsync(1);
        Assert.Empty(immediateClaim);

        await Task.Delay(250);

        var reclaimed = await store.GetPendingAsync(1);
        Assert.Single(reclaimed);
        Assert.Equal("stale-op", reclaimed[0].OperationId);
    }

    [Fact]
    public async Task MapAreas_CanBeUpsertedAndListed()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await store.UpsertMapAreaAsync(new MapAreaPackage
        {
            AreaId = "district-1",
            Name = "District 1",
            BoundingBox = new BoundingBox(-158.0, 21.2, -157.7, 21.5),
            MinZoom = 10,
            MaxZoom = 16,
            GeoPackagePath = "/data/district-1.gpkg",
        });

        var mapAreas = await store.ListMapAreasAsync();

        Assert.Single(mapAreas);
        Assert.Equal("district-1", mapAreas[0].AreaId);
        Assert.Equal(10, mapAreas[0].MinZoom);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private GeoPackageSyncStore CreateStore()
    {
        return new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = _databasePath,
        });
    }
}
