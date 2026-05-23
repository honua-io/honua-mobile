using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class LocalRecordExportServiceTests
{
    public LocalRecordExportServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task ExportLayerAsync_WritesCsvGeoJsonAndRedactedAttachmentManifest()
    {
        var databasePath = CreateDatabasePath();
        var exportRoot = CreateExportRoot();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, exportRoot, attachmentRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();
        await storage.CreateLayerAsync(layer);
        await storage.ApplyRemoteFeatureAsync(CreateFeature("asset-synced", "synced", pendingSecret: "server-token"));
        await storage.StoreFeatureAsync(CreateFeature("asset-local", "pending", pendingSecret: "dont-export"));

        var localAttachmentPath = Path.Combine(attachmentRoot, "photos", "asset-local.jpg");
        await storage.StoreAttachmentMetadataAsync(new AttachmentInfo
        {
            Id = "attachment-1",
            LayerId = layer.Id,
            FeatureId = "asset-local",
            FileName = @"C:\device\camera\asset-local.jpg",
            ContentType = "image/jpeg",
            PayloadKind = AttachmentPayloadKind.Photo,
            SizeBytes = 42,
            LocalPath = localAttachmentPath,
            CreatedAt = DateTime.UtcNow.AddMinutes(-3),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-2),
            SyncStatus = AttachmentSyncStatus.UploadFailed,
            RetryCount = 1,
            LastError = "Bearer abc.def.ghi upload failed",
            ThumbnailUrl = "https://example.test/thumbs/asset-local.jpg?token=secret#fragment"
        });

        var service = new LocalRecordExportService(storage, exportRoot);

        var result = await service.ExportLayerAsync(layer);

        Assert.Equal(2, result.RecordCount);
        Assert.Equal(1, result.AttachmentCount);
        Assert.True(File.Exists(result.CsvPath));
        Assert.True(File.Exists(result.GeoJsonPath));
        Assert.True(File.Exists(result.AttachmentManifestPath));

        var csv = await File.ReadAllTextAsync(result.CsvPath);
        Assert.Contains("pending_sync", csv);
        Assert.Contains("created_at_utc", csv);
        Assert.Contains("updated_at_utc", csv);
        Assert.Contains("attribute_name", csv);
        Assert.Contains("asset-local", csv);
        Assert.Contains("Insert", csv);
        Assert.Contains("[redacted]", csv);
        Assert.DoesNotContain("dont-export", csv);

        using var geoJson = JsonDocument.Parse(await File.ReadAllTextAsync(result.GeoJsonPath));
        Assert.Equal("FeatureCollection", geoJson.RootElement.GetProperty("type").GetString());
        var features = geoJson.RootElement.GetProperty("features").EnumerateArray().ToList();
        Assert.Equal(2, features.Count);
        var pendingFeature = features.Single(feature => feature.GetProperty("id").GetString() == "asset-local");
        Assert.Equal("Point", pendingFeature.GetProperty("geometry").GetProperty("type").GetString());
        var properties = pendingFeature.GetProperty("properties");
        Assert.True(properties.GetProperty("pending_sync").GetBoolean());
        Assert.Equal("pending", properties.GetProperty("attributes").GetProperty("status").GetString());
        Assert.Equal("[redacted]", properties.GetProperty("attributes").GetProperty("accessToken").GetString());

        var manifest = await File.ReadAllTextAsync(result.AttachmentManifestPath);
        Assert.Contains("\"localPathsRedacted\": true", manifest);
        Assert.Contains("asset-local.jpg", manifest);
        Assert.Contains("Bearer [redacted]", manifest);
        Assert.Contains("https://example.test/thumbs/asset-local.jpg", manifest);
        Assert.DoesNotContain(attachmentRoot, manifest);
        Assert.DoesNotContain(localAttachmentPath, manifest);
        Assert.DoesNotContain("abc.def.ghi", manifest);
        Assert.DoesNotContain("token=secret", manifest);
    }

    [Fact]
    public async Task ExportLayerAsync_WhenLayerIsEmpty_WritesValidEmptyFiles()
    {
        var databasePath = CreateDatabasePath();
        var exportRoot = CreateExportRoot();
        await using var cleanup = new FileCleanup(databasePath, exportRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();
        var service = new LocalRecordExportService(storage, exportRoot);

        var result = await service.ExportLayerAsync(layer);

        Assert.True(result.IsEmpty);
        Assert.Equal(0, result.AttachmentCount);

        var csv = await File.ReadAllLinesAsync(result.CsvPath);
        Assert.Single(csv);
        Assert.Contains("feature_id", csv[0]);

        using var geoJson = JsonDocument.Parse(await File.ReadAllTextAsync(result.GeoJsonPath));
        Assert.Equal("FeatureCollection", geoJson.RootElement.GetProperty("type").GetString());
        Assert.Empty(geoJson.RootElement.GetProperty("features").EnumerateArray());

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.AttachmentManifestPath));
        Assert.Equal(0, manifest.RootElement.GetProperty("recordCount").GetInt32());
        Assert.Equal(0, manifest.RootElement.GetProperty("attachmentCount").GetInt32());
    }

    [Fact]
    public async Task ExportLayerAsync_WhenAttributeCasingDiffers_DoesNotBlankCsvCells()
    {
        var databasePath = CreateDatabasePath();
        var exportRoot = CreateExportRoot();
        await using var cleanup = new FileCleanup(databasePath, exportRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();
        await storage.ApplyRemoteFeatureAsync(CreateFeature("asset-001", "synced", statusAttributeName: "Status"));
        await storage.ApplyRemoteFeatureAsync(CreateFeature("asset-002", "pending", statusAttributeName: "status"));
        var service = new LocalRecordExportService(storage, exportRoot);

        var result = await service.ExportLayerAsync(layer);

        var rows = File.ReadAllLines(result.CsvPath);
        var headers = rows[0].Split(',');
        var statusIndex = Array.FindIndex(headers, header =>
            string.Equals(header, "attribute_Status", StringComparison.OrdinalIgnoreCase));

        Assert.True(statusIndex >= 0);
        Assert.Equal("synced", rows[1].Split(',')[statusIndex]);
        Assert.Equal("pending", rows[2].Split(',')[statusIndex]);
    }

    [Fact]
    public async Task ExportLayerAsync_WithManyRecords_CompletesWithoutChangingOutputShape()
    {
        var databasePath = CreateDatabasePath();
        var exportRoot = CreateExportRoot();
        await using var cleanup = new FileCleanup(databasePath, exportRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        var layer = CreateLayer();

        for (var index = 0; index < 300; index++)
        {
            await storage.ApplyRemoteFeatureAsync(CreateFeature($"asset-{index:000}", "synced"));
        }

        var service = new LocalRecordExportService(storage, exportRoot);

        var result = await service.ExportLayerAsync(layer);

        Assert.Equal(300, result.RecordCount);
        Assert.Equal(301, File.ReadLines(result.CsvPath).Count());
        Assert.True(new FileInfo(result.GeoJsonPath).Length > 0);
        Assert.True(new FileInfo(result.AttachmentManifestPath).Length > 0);
    }

    private static LayerInfo CreateLayer()
    {
        return new LayerInfo
        {
            Id = 1,
            ServiceId = "field-support",
            SourceId = "field-support/FeatureServer/1",
            Name = "Field Assets",
            GeometryType = GeometryType.Point,
            IsEditable = true
        };
    }

    private static Feature CreateFeature(
        string id,
        string status,
        string? pendingSecret = null,
        string statusAttributeName = "status")
    {
        return new Feature
        {
            Id = id,
            LayerId = 1,
            Version = 1,
            Geometry = new Point(21.3, -157.8),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ModifiedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Attributes = new Dictionary<string, object?>
            {
                ["name"] = id,
                [statusAttributeName] = status,
                ["accessToken"] = pendingSecret
            }
        };
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"honua-field-export-{Guid.NewGuid():N}.gpkg");
    }

    private static string CreateExportRoot()
    {
        return Path.Combine(Path.GetTempPath(), $"honua-field-export-{Guid.NewGuid():N}");
    }

    private static string CreateAttachmentRoot()
    {
        return Path.Combine(Path.GetTempPath(), $"honua-field-export-attachments-{Guid.NewGuid():N}");
    }

    private sealed class FileCleanup : IAsyncDisposable
    {
        private readonly string[] _paths;

        public FileCleanup(params string[] paths)
        {
            _paths = paths;
        }

        public ValueTask DisposeAsync()
        {
            foreach (var path in _paths)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                if (File.Exists($"{path}-wal"))
                {
                    File.Delete($"{path}-wal");
                }

                if (File.Exists($"{path}-shm"))
                {
                    File.Delete($"{path}-shm");
                }

                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
