using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class GeoPackageBatchStorageTests
{
    public GeoPackageBatchStorageTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task StoreFeaturesAsync_PersistsAllFeaturesAndChangeRecordsInOneBatch()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new FileCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);

        var features = Enumerable.Range(0, 25)
            .Select(i => CreateFeature($"asset-{i}", layerId: 1))
            .ToList();

        var ids = await storage.StoreFeaturesAsync(features);

        Assert.Equal(25, ids.Count);
        Assert.Equal(features.Select(f => f.Id).OrderBy(x => x), ids.OrderBy(x => x));

        var stored = await storage.QueryFeaturesAsync(layerId: 1);
        Assert.Equal(25, stored.Count);

        // Every insert must record a pending change so it syncs (matches single-insert path).
        var pending = await storage.GetPendingChangesAsync(layerId: 1);
        Assert.Equal(25, pending.Count);
    }

    [Fact]
    public async Task StoreFeaturesAsync_EmptyBatch_IsNoOp()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new FileCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);

        var ids = await storage.StoreFeaturesAsync(new List<Feature>());

        Assert.Empty(ids);
        Assert.Empty(await storage.QueryFeaturesAsync(layerId: 1));
    }

    [Fact]
    public async Task GetAttachmentsForFeaturesAsync_GroupsByFeatureInSingleQuery()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new FileCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);

        await storage.StoreAttachmentMetadataAsync(CreateAttachment("att-1", "asset-1", layerId: 1));
        await storage.StoreAttachmentMetadataAsync(CreateAttachment("att-2", "asset-1", layerId: 1));
        await storage.StoreAttachmentMetadataAsync(CreateAttachment("att-3", "asset-2", layerId: 1));
        await storage.StoreAttachmentMetadataAsync(CreateAttachment("att-4", "asset-3", layerId: 1, isDeleted: true));

        var result = await storage.GetAttachmentsForFeaturesAsync(
            new[] { "asset-1", "asset-2", "asset-3", "asset-missing" });

        Assert.Equal(2, result["asset-1"].Count);
        Assert.Single(result["asset-2"]);
        // Deleted attachments are excluded by default, so asset-3 has no entry.
        Assert.False(result.ContainsKey("asset-3"));
        Assert.False(result.ContainsKey("asset-missing"));

        // Same result as calling the single-feature method per feature.
        var single1 = await storage.GetAttachmentsForFeatureAsync("asset-1", layerId: 1);
        Assert.Equal(
            single1.Select(a => a.Id).OrderBy(x => x),
            result["asset-1"].Select(a => a.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task GetAttachmentsForFeaturesAsync_RespectsLayerScope()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new FileCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);

        await storage.StoreAttachmentMetadataAsync(CreateAttachment("att-1", "asset-1", layerId: 1));
        await storage.StoreAttachmentMetadataAsync(CreateAttachment("att-2", "asset-1", layerId: 2));

        var layer1 = await storage.GetAttachmentsForFeaturesAsync(new[] { "asset-1" }, layerId: 1);

        Assert.Single(layer1["asset-1"]);
        Assert.Equal("att-1", layer1["asset-1"][0].Id);
    }

    [Fact]
    public async Task GetAttachmentsForFeaturesAsync_EmptyInput_ReturnsEmpty()
    {
        var databasePath = CreateDatabasePath();
        await using var cleanup = new FileCleanup(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);

        var result = await storage.GetAttachmentsForFeaturesAsync(Array.Empty<string>());

        Assert.Empty(result);
    }

    private static Feature CreateFeature(string id, int layerId)
    {
        return new Feature
        {
            Id = id,
            LayerId = layerId,
            Version = 1,
            Geometry = new Point(21.3, -157.8),
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Attributes = new Dictionary<string, object?> { ["name"] = id },
        };
    }

    private static AttachmentInfo CreateAttachment(string id, string featureId, int layerId, bool isDeleted = false)
    {
        return new AttachmentInfo
        {
            Id = id,
            FeatureId = featureId,
            LayerId = layerId,
            FileName = $"{id}.jpg",
            ContentType = "image/jpeg",
            PayloadKind = AttachmentPayloadKind.Photo,
            CreatedAt = DateTime.UtcNow,
            SyncStatus = AttachmentSyncStatus.PendingUpload,
            IsDeleted = isDeleted,
        };
    }

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), $"honua-batch-{Guid.NewGuid():N}.gpkg");

    private sealed class FileCleanup : IAsyncDisposable
    {
        private readonly string[] _paths;

        public FileCleanup(params string[] paths) => _paths = paths;

        public ValueTask DisposeAsync()
        {
            foreach (var path in _paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
