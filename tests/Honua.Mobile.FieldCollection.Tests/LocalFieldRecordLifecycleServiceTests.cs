using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.Services.Workflow;
using Honua.Sdk.Field.Projects;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class LocalFieldRecordLifecycleServiceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly string _artifactRoot;

    public LocalFieldRecordLifecycleServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-lifecycle-{Guid.NewGuid():N}.gpkg");
        _artifactRoot = Path.Combine(Path.GetTempPath(), $"honua-lifecycle-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_artifactRoot);
    }

    [Fact]
    public async Task TransitionAsync_EnforcesPolicyAndPersistsLifecycleMetadata()
    {
        using (var storage = new GeoPackageStorageService(_databasePath))
        {
            await storage.CreateLayerAsync(CreateLayer());
            await storage.StoreFeatureAsync(CreateFeature("asset-1"));
            var service = new LocalFieldRecordLifecycleService(storage);
            var submittedAt = new DateTimeOffset(2026, 5, 24, 8, 0, 0, TimeSpan.Zero);

            var ready = await service.TransitionAsync(
                1,
                "asset-1",
                RecordStatus.ReadyToSubmit,
                transitionTimeUtc: submittedAt.AddMinutes(-5));
            var submitted = await service.TransitionAsync(
                1,
                "asset-1",
                RecordStatus.Submitted,
                actorId: "field-user-1",
                actorRole: "inspector",
                note: "ready for review",
                transitionTimeUtc: submittedAt);
            var invalid = await service.TransitionAsync(1, "asset-1", RecordStatus.Draft);

            Assert.True(ready.Succeeded);
            Assert.True(submitted.Succeeded);
            Assert.False(invalid.Succeeded);
            Assert.Equal("invalid-transition", invalid.ReasonCode);

            var feature = await storage.GetFeatureAsync("asset-1", 1);
            Assert.NotNull(feature);
            Assert.Equal(RecordStatus.Submitted, LocalFieldRecordLifecycleService.GetStatus(feature));
            Assert.False(LocalFieldRecordLifecycleService.CanEdit(feature));
            Assert.Equal("field-user-1", feature.Attributes[LocalFieldRecordLifecycleService.LifecycleActorIdAttribute]?.ToString());
            Assert.Contains("2026-05-24T08:00:00", feature.Attributes[LocalFieldRecordLifecycleService.SubmittedAtAttribute]?.ToString());
        }

        using (var restartedStorage = new GeoPackageStorageService(_databasePath))
        {
            var feature = await restartedStorage.GetFeatureAsync("asset-1", 1);
            Assert.NotNull(feature);
            Assert.Equal(RecordStatus.Submitted, LocalFieldRecordLifecycleService.GetStatus(feature));
        }
    }

    [Fact]
    public async Task LifecyclePolicy_ProtectsSubmittedRecordsAndAllowsRejectedOrReopenedEditing()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        await storage.CreateLayerAsync(CreateLayer());
        await storage.StoreFeatureAsync(CreateFeature("asset-2"));
        var service = new LocalFieldRecordLifecycleService(storage);

        Assert.True(LocalFieldRecordLifecycleService.CanEdit(RecordStatus.Draft));
        Assert.True(LocalFieldRecordLifecycleService.CanEdit(RecordStatus.Rejected));
        Assert.False(LocalFieldRecordLifecycleService.CanEdit(RecordStatus.Submitted));
        Assert.False(LocalFieldRecordLifecycleService.CanEdit(RecordStatus.Approved));

        Assert.True(await TransitionSucceeded(service, "asset-2", RecordStatus.Submitted));
        Assert.True(await TransitionSucceeded(service, "asset-2", RecordStatus.Rejected));
        var rejected = await storage.GetFeatureAsync("asset-2", 1);
        Assert.NotNull(rejected);
        Assert.True(LocalFieldRecordLifecycleService.CanEdit(rejected));

        Assert.True(await TransitionSucceeded(service, "asset-2", RecordStatus.Reopened));
        var reopened = await storage.GetFeatureAsync("asset-2", 1);
        Assert.NotNull(reopened);
        Assert.True(LocalFieldRecordLifecycleService.CanEdit(reopened));

        var protectedPolicy = FieldRecordLifecyclePolicy.Default with { AllowRejectedEdit = false };
        Assert.False(LocalFieldRecordLifecycleService.CanEdit(RecordStatus.Rejected, protectedPolicy));
    }

    [Fact]
    public async Task LocalExport_IncludesLifecycleMetadata()
    {
        using var storage = new GeoPackageStorageService(_databasePath);
        var layer = CreateLayer();
        await storage.CreateLayerAsync(layer);
        await storage.UpsertProjectCatalogEntryAsync(new FieldProjectCatalogEntry
        {
            ProjectId = "lifecycle-demo",
            ServiceId = "lifecycle-demo",
            PackageId = "pkg-lifecycle",
            Name = "Lifecycle Demo",
            State = FieldProjectCatalogState.Installed,
            ValidationStatus = FieldProjectValidationStatus.Valid,
            LayerCount = 1
        });
        await storage.StoreFeatureAsync(CreateFeature("asset-3"));
        var service = new LocalFieldRecordLifecycleService(storage);
        Assert.True((await service.TransitionAsync(1, "asset-3", RecordStatus.Submitted)).Succeeded);

        var result = await new LocalRecordExportService(storage, _artifactRoot).ExportLayerAsync(layer);
        var csv = await File.ReadAllTextAsync(result.CsvPath);
        var geoJson = await File.ReadAllTextAsync(result.GeoJsonPath);

        Assert.Contains("attribute_honua_record_status", csv);
        Assert.Contains("Submitted", csv);
        Assert.Contains(LocalFieldRecordLifecycleService.StatusAttribute, geoJson);
        Assert.Contains("Submitted", geoJson);
    }

    public void Dispose()
    {
        DeleteIfExists(_databasePath);
        DeleteIfExists($"{_databasePath}-wal");
        DeleteIfExists($"{_databasePath}-shm");

        if (Directory.Exists(_artifactRoot))
        {
            Directory.Delete(_artifactRoot, recursive: true);
        }
    }

    private static async Task<bool> TransitionSucceeded(
        LocalFieldRecordLifecycleService service,
        string featureId,
        RecordStatus status)
        => (await service.TransitionAsync(1, featureId, status)).Succeeded;

    private static LayerInfo CreateLayer()
        => new()
        {
            Id = 1,
            ServiceId = "lifecycle-demo",
            SourceId = "lifecycle-demo/assets",
            Name = "Lifecycle Assets",
            GeometryType = GeometryType.Point,
            IsEditable = true
        };

    private static Feature CreateFeature(string id)
        => new()
        {
            Id = id,
            LayerId = 1,
            Geometry = new Point(21.3, -157.8),
            CreatedAt = new DateTime(2026, 5, 24, 7, 30, 0, DateTimeKind.Utc),
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["asset_id"] = id
            }
        };

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
