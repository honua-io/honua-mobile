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
