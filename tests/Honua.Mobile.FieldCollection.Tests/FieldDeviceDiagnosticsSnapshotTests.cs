using Honua.Mobile.FieldCollection.Services.Diagnostics;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class FieldDeviceDiagnosticsSnapshotTests
{
    [Fact]
    public void Create_WhenFailuresExist_CategorizesStateAndActions()
    {
        var snapshot = FieldDeviceDiagnosticsSnapshot.Create(new FieldDeviceDiagnosticsInput
        {
            GeneratedAtUtc = new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc),
            AppVersion = "1.2.3",
            BuildNumber = "42",
            BuildEnvironment = "staging",
            SourceDisplay = "honua-io/honua-mobile@abcdef on main",
            WorkflowRunDisplay = "GitHub Actions run 100",
            ServiceEndpointState = "Staging: https://api.staging.example.test",
            Platform = "Android",
            OperatingSystem = "Android 16",
            DeviceModel = "Pixel",
            DeviceType = "Physical",
            Architecture = "Arm64",
            Manufacturer = "Google",
            IsConnected = true,
            ServerReachable = false,
            IsRemoteSyncConfigured = true,
            SyncStatus = "Error",
            LastSyncTime = new DateTime(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc),
            PendingChangeCount = 3,
            PendingAttachmentCount = 2,
            FailedOperationCount = 1,
            RetryOperationCount = 4,
            FailedAttachmentCount = 2,
            AttachmentUploadFailedCount = 1,
            AttachmentDownloadFailedCount = 1,
            ConflictCount = 1,
            DatabaseSizeDisplay = "2.00 MB",
            TotalFeatureCount = 15,
            LayerCount = 2,
            PackageId = "field-cache",
            PackageFileName = "/private/device/cache/honua_field_collection.gpkg",
            PackageSizeDisplay = "4.00 MB",
            MetadataCacheStatus = "Available",
            MetadataSourceCount = 2,
            FeatureCacheStatus = "Available",
            FeatureSourceCount = 2,
            CachedFeatureCount = 15,
            LocalGeneration = 7,
            ServerGeneration = 9
        });

        Assert.Equal("Critical", snapshot.HealthStatus);
        Assert.Contains(snapshot.FailureCategories, category => category.Category == "SyncFailures");
        Assert.Contains(snapshot.FailureCategories, category => category.Category == "AttachmentFailures");
        Assert.Contains(snapshot.FailureCategories, category => category.Category == "Conflicts");
        Assert.Contains("Resolve attachment", snapshot.SupportActionsText);
        Assert.Contains("Pending changes 3", snapshot.SummaryText);
        Assert.Contains("Last sync 2026-05-22 12:00:00Z (1 day ago)", snapshot.SummaryText);
        Assert.Equal("honua_field_collection.gpkg", snapshot.PackageFileName);
        Assert.DoesNotContain("/private/device/cache", snapshot.SummaryText);

        var properties = snapshot.ToReportProperties();
        Assert.Equal("Critical", properties["healthStatus"]);
        Assert.Equal(3, properties["pendingChanges"]);
        Assert.Equal(2, properties["failedAttachments"]);
    }

    [Fact]
    public void Create_WhenHealthy_ProducesInventoryShapeForFutureRemoteUse()
    {
        var snapshot = FieldDeviceDiagnosticsSnapshot.Create(new FieldDeviceDiagnosticsInput
        {
            GeneratedAtUtc = new DateTime(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc),
            AppVersion = "1.2.3",
            BuildNumber = "42",
            BuildEnvironment = "production",
            SourceDisplay = "Source not stamped",
            WorkflowRunDisplay = "Local build",
            ServiceEndpointState = "Production: https://api.example.test",
            Platform = "iOS",
            OperatingSystem = "iOS 19",
            DeviceModel = "iPhone",
            DeviceType = "Physical",
            Architecture = "Arm64",
            Manufacturer = "Apple",
            IsConnected = true,
            ServerReachable = true,
            IsRemoteSyncConfigured = true,
            SyncStatus = "Idle",
            LastSyncTime = new DateTime(2026, 5, 23, 11, 55, 0, DateTimeKind.Utc),
            DatabaseSizeDisplay = "1.00 MB",
            PackageId = "field-cache",
            PackageFileName = "field-cache.gpkg",
            PackageSizeDisplay = "2.00 MB",
            MetadataCacheStatus = "Available",
            FeatureCacheStatus = "Available"
        });

        Assert.Equal("Healthy", snapshot.HealthStatus);
        Assert.Equal("No local action required.", Assert.Single(snapshot.SupportActions));
        Assert.Equal("honua.mobile.device-inventory.v1", snapshot.DeviceInventory.SchemaVersion);
        Assert.Contains("sync-health", snapshot.DeviceInventory.Capabilities);
        Assert.Equal("field-cache", snapshot.DeviceInventory.PackageId);
        Assert.Equal("Available", snapshot.PackageState.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
    }
}
