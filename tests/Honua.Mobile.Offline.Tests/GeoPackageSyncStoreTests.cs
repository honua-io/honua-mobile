using System.Globalization;
using Honua.Mobile.Offline.GeoPackage;
using Microsoft.Data.Sqlite;

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
            InProgressLeaseTimeout = TimeSpan.FromMinutes(5),
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

        await SetClaimedAtUtcAsync("stale-op", DateTimeOffset.UtcNow.AddMinutes(-10));

        var reclaimed = await store.GetPendingAsync(1);
        Assert.Single(reclaimed);
        Assert.Equal("stale-op", reclaimed[0].OperationId);
    }

    [Fact]
    public async Task GetPendingByLayerKeyPrefixAsync_ClaimsOnlyMatchingLayerKeys()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "area-1-op",
            LayerKey = "sdk-package:area-1:parks",
            TargetCollection = "parks",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "area-2-op",
            LayerKey = "sdk-package:area-2:parks",
            TargetCollection = "parks",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        var area1Pending = await store.GetPendingByLayerKeyPrefixAsync("sdk-package:area-1:", 10);
        var area2Pending = await store.GetPendingByLayerKeyPrefixAsync("sdk-package:area-2:", 10);

        Assert.Collection(area1Pending, operation => Assert.Equal("area-1-op", operation.OperationId));
        Assert.Collection(area2Pending, operation => Assert.Equal("area-2-op", operation.OperationId));
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

    [Fact]
    public async Task InitializeAsync_HandlesDatabasePathsWithConnectionStringCharacters()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"honua-mobile-{Guid.NewGuid():N};Mode=Memory.gpkg");

        try
        {
            var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
            {
                DatabasePath = databasePath,
            });

            await store.InitializeAsync();
            await store.SetSyncCursorAsync("replica", "cursor-1");

            var cursor = await store.GetSyncCursorAsync("replica");

            Assert.Equal("cursor-1", cursor);
            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
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

    private async Task SetClaimedAtUtcAsync(string operationId, DateTimeOffset claimedAtUtc)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
        };

        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
UPDATE honua_sync_queue
SET claimed_at_utc = $claimed_at_utc
WHERE operation_id = $operation_id;
";
        command.Parameters.AddWithValue("$claimed_at_utc", claimedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$operation_id", operationId);

        await command.ExecuteNonQueryAsync();
    }
}
