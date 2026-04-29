using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Offline.Abstractions;

namespace Honua.Mobile.Offline.Tests;

public sealed class GeoPackageSdkOfflineStoreAdapterTests : IDisposable
{
    private readonly string _databasePath;

    public GeoPackageSdkOfflineStoreAdapterTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-sdk-offline-{Guid.NewGuid():N}.gpkg");
    }

    [Fact]
    public async Task SaveFeaturesAsync_PersistsSdkFeatureRecordsInGeoPackageStore()
    {
        var store = CreateStore();
        var adapter = new GeoPackageSdkOfflineStoreAdapter(store);

        await adapter.SaveFeaturesAsync(new OfflineFeaturePage
        {
            PackageId = "area-1",
            SourceId = "parks",
            Source = CreateSourceDescriptor(),
            Result = new FeatureQueryResult
            {
                ProviderName = "fake",
                ObjectIdFieldName = "objectid",
                Features =
                [
                    new FeatureRecord
                    {
                        Id = "42",
                        Attributes = new Dictionary<string, JsonElement>
                        {
                            ["name"] = JsonSerializer.SerializeToElement("Ala Moana"),
                        },
                    },
                ],
                NumberReturned = 1,
            },
        });

        var features = await store.GetFeaturesAsync("sdk-package:area-1:parks");
        var unpartitionedFeatures = await store.GetFeaturesAsync("parks");

        var featureJson = Assert.Single(features);
        using var document = JsonDocument.Parse(featureJson);
        Assert.Equal(42, document.RootElement.GetProperty("attributes").GetProperty("objectid").GetInt64());
        Assert.Equal("Ala Moana", document.RootElement.GetProperty("attributes").GetProperty("name").GetString());
        Assert.Empty(unpartitionedFeatures);
    }

    [Fact]
    public async Task SaveFeaturesAsync_PartitionsSameSourceIdByPackage()
    {
        var store = CreateStore();
        var adapter = new GeoPackageSdkOfflineStoreAdapter(store);

        await adapter.SaveFeaturesAsync(CreateFeaturePage("area-1", "parks", "42", "Ala Moana"));
        await adapter.SaveFeaturesAsync(CreateFeaturePage("area-2", "parks", "42", "Kapiolani"));

        var area1Features = await store.GetFeaturesAsync("sdk-package:area-1:parks");
        var area2Features = await store.GetFeaturesAsync("sdk-package:area-2:parks");

        using var area1Document = JsonDocument.Parse(Assert.Single(area1Features));
        using var area2Document = JsonDocument.Parse(Assert.Single(area2Features));
        Assert.Equal("Ala Moana", area1Document.RootElement.GetProperty("attributes").GetProperty("name").GetString());
        Assert.Equal("Kapiolani", area2Document.RootElement.GetProperty("attributes").GetProperty("name").GetString());
    }

    [Fact]
    public async Task ChangeJournal_RoundTripsSdkEntriesThroughMobileQueue()
    {
        var store = CreateStore();
        var adapter = new GeoPackageSdkOfflineStoreAdapter(store);

        await adapter.EnqueueAsync(new OfflineChangeJournalEntry
        {
            OperationId = "op-1",
            PackageId = "area-1",
            SourceId = "parks",
            Source = new FeatureSource { CollectionId = "parks" },
            OperationKind = OfflineEditOperationKind.Update,
            Feature = new FeatureEditFeature
            {
                Id = "park-1",
                Attributes = new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonSerializer.SerializeToElement("Updated"),
                },
            },
            BaseSyncToken = "server-token",
            Metadata = new Dictionary<string, string> { ["formId"] = "inspection" },
        });

        var pending = await adapter.GetPendingAsync("area-1", 10);

        var entry = Assert.Single(pending);
        Assert.Equal("op-1", entry.OperationId);
        Assert.Equal("area-1", entry.PackageId);
        Assert.Equal("parks", entry.SourceId);
        Assert.Equal("parks", entry.Source.CollectionId);
        Assert.Equal(OfflineEditOperationKind.Update, entry.OperationKind);
        Assert.Equal("park-1", entry.Feature?.Id);
        Assert.Equal("server-token", entry.BaseSyncToken);
        Assert.Equal("inspection", entry.Metadata["formId"]);

        await adapter.MarkRetryAsync(new OfflineRetryCheckpoint
        {
            OperationId = "op-1",
            PackageId = "area-1",
            SourceId = "parks",
            AttemptCount = 1,
            Reason = "network unavailable",
        });

        Assert.Equal(1, await store.CountPendingAsync());
    }

    [Fact]
    public async Task ChangeJournal_OnlyClaimsEntriesForRequestedPackage()
    {
        var store = CreateStore();
        var adapter = new GeoPackageSdkOfflineStoreAdapter(store);

        await adapter.EnqueueAsync(CreateJournalEntry("area-1", "parks", "op-1"));
        await adapter.EnqueueAsync(CreateJournalEntry("area-2", "parks", "op-2"));

        var area1Pending = await adapter.GetPendingAsync("area-1", 10);
        var area2Pending = await adapter.GetPendingAsync("area-2", 10);

        Assert.Collection(
            area1Pending,
            entry => Assert.Equal("op-1", entry.OperationId));
        Assert.Collection(
            area2Pending,
            entry => Assert.Equal("op-2", entry.OperationId));
    }

    [Fact]
    public async Task CheckpointsAndState_RoundTripThroughSyncCursors()
    {
        var adapter = new GeoPackageSdkOfflineStoreAdapter(CreateStore());

        await adapter.SaveCheckpointAsync(new OfflineSyncCheckpoint
        {
            PackageId = "area-1",
            SourceId = "parks",
            SyncToken = "server-token",
            PulledFeatureCount = 12,
        });

        await adapter.SaveStateAsync(new OfflineSyncState
        {
            PackageId = "area-1",
            SourceId = "parks",
            Phase = OfflineSyncPhase.Completed,
            LastSyncToken = "server-token",
            PendingChangeCount = 3,
        });

        var checkpoint = await adapter.GetCheckpointAsync("area-1", "parks");
        var state = await adapter.GetStateAsync("area-1", "parks");

        Assert.NotNull(checkpoint);
        Assert.Equal("server-token", checkpoint!.SyncToken);
        Assert.Equal(12, checkpoint.PulledFeatureCount);
        Assert.NotNull(state);
        Assert.Equal(OfflineSyncPhase.Completed, state!.Phase);
        Assert.Equal(3, state.PendingChangeCount);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private GeoPackageSyncStore CreateStore()
        => new(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });

    private static OfflineFeaturePage CreateFeaturePage(string packageId, string sourceId, string featureId, string name)
        => new()
        {
            PackageId = packageId,
            SourceId = sourceId,
            Source = CreateSourceDescriptor(),
            Result = new FeatureQueryResult
            {
                ProviderName = "fake",
                ObjectIdFieldName = "objectid",
                Features =
                [
                    new FeatureRecord
                    {
                        Id = featureId,
                        Attributes = new Dictionary<string, JsonElement>
                        {
                            ["name"] = JsonSerializer.SerializeToElement(name),
                        },
                    },
                ],
                NumberReturned = 1,
            },
        };

    private static OfflineChangeJournalEntry CreateJournalEntry(string packageId, string sourceId, string operationId)
        => new()
        {
            OperationId = operationId,
            PackageId = packageId,
            SourceId = sourceId,
            Source = new FeatureSource { CollectionId = sourceId },
            OperationKind = OfflineEditOperationKind.Update,
            Feature = new FeatureEditFeature
            {
                Id = $"{operationId}-feature",
                Attributes = new Dictionary<string, JsonElement>
                {
                    ["name"] = JsonSerializer.SerializeToElement(operationId),
                },
            },
        };

    private static SourceDescriptor CreateSourceDescriptor()
        => new()
        {
            Id = "parks",
            Protocol = FeatureProtocolIds.OgcFeatures,
            Locator = new SourceLocator { CollectionId = "parks" },
        };
}
