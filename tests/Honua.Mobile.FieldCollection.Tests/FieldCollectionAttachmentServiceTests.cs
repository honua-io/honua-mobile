using System.Text;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Ai;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.Services.Sync;
using Honua.Sdk.Abstractions.Features;
using StorageChangeRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeRecord;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class FieldCollectionAttachmentServiceTests
{
    public FieldCollectionAttachmentServiceTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task SaveAttachmentAsync_PersistsContentMetadataAndQueueAcrossRestart()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);

        using (var storage = new GeoPackageStorageService(databasePath))
        {
            var service = new AttachmentService(storage, attachmentRoot);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("photo"));

            var attachment = await service.SaveAttachmentAsync(
                layerId: 1,
                featureId: "asset-1",
                content,
                "asset.jpg",
                "image/jpeg",
                AttachmentPayloadKind.Photo);

            Assert.Equal(AttachmentSyncStatus.PendingUpload, attachment.SyncStatus);
            Assert.Equal(AttachmentPayloadKind.Photo, attachment.PayloadKind);
            Assert.True(File.Exists(attachment.LocalPath));
            Assert.Single(await storage.GetPendingAttachmentChangesAsync());
        }

        using (var restartedStorage = new GeoPackageStorageService(databasePath))
        {
            var restartedService = new AttachmentService(restartedStorage, attachmentRoot);
            var attachments = (await restartedService.GetAttachmentsAsync("asset-1")).ToList();

            var attachment = Assert.Single(attachments);
            Assert.Equal("asset.jpg", attachment.FileName);
            await using var savedContent = await restartedService.GetAttachmentAsync(attachment.Id);
            using var reader = new StreamReader(savedContent, Encoding.UTF8);
            Assert.Equal("photo", await reader.ReadToEndAsync());
        }
    }

    [Theory]
    [InlineData("walkthrough.mp4", "video/mp4", AttachmentPayloadKind.Video)]
    [InlineData("voice-note.m4a", "audio/mp4", AttachmentPayloadKind.Audio)]
    [InlineData("damage-sketch.json", "application/vnd.honua.sketch+json", AttachmentPayloadKind.Sketch)]
    [InlineData("asset-barcode.json", "application/vnd.honua.barcode+json", AttachmentPayloadKind.Barcode)]
    [InlineData("signature.svg", "image/svg+xml", AttachmentPayloadKind.Signature)]
    public async Task SaveAttachmentAsync_InfersAndPersistsLocalMediaParityKinds(
        string fileName,
        string contentType,
        AttachmentPayloadKind expectedKind)
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);

        using (var storage = new GeoPackageStorageService(databasePath))
        {
            var service = new AttachmentService(storage, attachmentRoot);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes(fileName));

            await service.SaveAttachmentAsync(
                layerId: 1,
                featureId: "asset-1",
                content,
                fileName,
                contentType);
        }

        using (var restartedStorage = new GeoPackageStorageService(databasePath))
        {
            var restartedService = new AttachmentService(restartedStorage, attachmentRoot);
            var attachment = Assert.Single(await restartedService.GetAttachmentsAsync("asset-1"));

            Assert.Equal(expectedKind, attachment.PayloadKind);
            Assert.Equal(contentType, attachment.ContentType);
            Assert.True(File.Exists(attachment.LocalPath));
        }
    }

    [Fact]
    public async Task SaveAttachmentAsync_PersistsPhotoCaptureLocationMetadata()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);
        var captureLocation = CreateExternalGnssEvidence();

        using (var storage = new GeoPackageStorageService(databasePath))
        {
            var service = new AttachmentService(storage, attachmentRoot);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("photo"));

            var attachment = await service.SaveAttachmentAsync(
                1,
                "asset-1",
                content,
                "asset.jpg",
                "image/jpeg",
                AttachmentPayloadKind.Photo,
                description: "photos",
                captureLocation: captureLocation);

            Assert.Equal(FieldLocationSourceKind.ExternalGnss, attachment.CaptureLocation?.SourceKind);
            Assert.Equal(0.8, attachment.CaptureLocation?.HorizontalAccuracyMeters);
        }

        using (var restartedStorage = new GeoPackageStorageService(databasePath))
        {
            var restartedService = new AttachmentService(restartedStorage, attachmentRoot);
            var attachment = Assert.Single(await restartedService.GetAttachmentsAsync("asset-1"));

            Assert.Equal(FieldLocationSourceKind.ExternalGnss, attachment.CaptureLocation?.SourceKind);
            Assert.Equal("Trimble R12", attachment.CaptureLocation?.Receiver?.Name);
            Assert.Equal(21.3069, attachment.CaptureLocation?.Latitude);
        }
    }

    [Fact]
    public async Task UpdateAttachmentAiStateAsync_PersistsMediaRedactionState()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);

        string attachmentId;
        using (var storage = new GeoPackageStorageService(databasePath))
        {
            var service = new AttachmentService(storage, attachmentRoot);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("photo"));
            var attachment = await service.SaveAttachmentAsync(
                1,
                "asset-1",
                content,
                "asset.jpg",
                "image/jpeg",
                AttachmentPayloadKind.Photo);
            attachmentId = attachment.Id;

            await service.UpdateAttachmentAiStateAsync(attachment.Id, new MobileAiMediaState
            {
                RedactionStatus = MobileAiMediaProcessingStatus.Queued,
                EnrichmentStatus = MobileAiMediaProcessingStatus.Completed,
                RequiresFaceBlur = true,
                ProviderId = "mock"
            });
        }

        using (var restartedStorage = new GeoPackageStorageService(databasePath))
        {
            var restartedService = new AttachmentService(restartedStorage, attachmentRoot);
            var attachment = Assert.Single(await restartedService.GetAttachmentsAsync("asset-1"));

            Assert.Equal(attachmentId, attachment.Id);
            Assert.True(attachment.AiMediaState?.RequiresFaceBlur);
            Assert.Equal(MobileAiMediaProcessingStatus.Queued, attachment.AiMediaState?.RedactionStatus);
            Assert.Contains("AI redaction Queued", attachment.StatusSummary);
        }
    }

    [Fact]
    public async Task DeleteAttachmentAsync_WhenUnsynced_RemovesContentAndClearsPendingQueue()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        var service = new AttachmentService(storage, attachmentRoot);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("draft"));
        var attachment = await service.SaveAttachmentAsync(1, "asset-1", content, "draft.txt", "text/plain");

        await service.DeleteAttachmentAsync(attachment.Id);

        Assert.Empty(await service.GetAttachmentsAsync("asset-1"));
        Assert.Empty(await storage.GetPendingAttachmentChangesAsync());
        Assert.False(File.Exists(attachment.LocalPath));
        var metadata = await storage.GetAttachmentMetadataAsync(attachment.Id);
        Assert.True(metadata?.IsDeleted);
        Assert.Equal(AttachmentSyncStatus.Synced, metadata?.SyncStatus);
    }

    [Fact]
    public async Task SaveAttachmentAsync_WhenQuotaExceeded_DoesNotPersistMetadataOrFile()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        var service = new AttachmentService(storage, attachmentRoot, quotaBytes: 4);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("too-large"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAttachmentAsync(1, "asset-1", content, "large.bin", "application/octet-stream"));

        Assert.Empty(await service.GetAttachmentsAsync("asset-1"));
        Assert.Empty(Directory.Exists(attachmentRoot)
            ? Directory.EnumerateFiles(attachmentRoot, "*", SearchOption.AllDirectories)
            : []);
    }

    [Fact]
    public async Task GetOfflineCacheDiagnosticsAsync_ReportsAttachmentPendingAndFailures()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        var service = new AttachmentService(storage, attachmentRoot);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("photo"));
        var attachment = await service.SaveAttachmentAsync(1, "asset-1", content, "asset.jpg", "image/jpeg");

        await storage.MarkAttachmentSyncFailedAsync(
            attachment.Id,
            AttachmentSyncStatus.UploadFailed,
            "upload failed");

        var diagnostics = await storage.GetOfflineCacheDiagnosticsAsync();

        Assert.Equal(1, diagnostics.Operations.AttachmentPendingCount);
        Assert.Equal(1, diagnostics.Operations.AttachmentFailedCount);
        Assert.Equal(1, diagnostics.Operations.AttachmentUploadFailedCount);
        Assert.Equal(0, diagnostics.Operations.AttachmentDownloadFailedCount);
    }

    [Fact]
    public async Task PushChangesAsync_UploadsPendingAttachmentsAndRetriesFailures()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", objectId: 42));
        await storage.MarkChangesAsSynced((await storage.GetPendingChangesAsync()).Select(change => change.Id).ToList());

        var service = new AttachmentService(storage, attachmentRoot);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("photo"));
        var attachment = await service.SaveAttachmentAsync(
            1,
            "asset-1",
            content,
            "asset.jpg",
            "image/jpeg",
            AttachmentPayloadKind.Photo,
            captureLocation: CreateExternalGnssEvidence());

        var attachmentClient = new RecordingAttachmentSyncClient
        {
            AddResult = new FeatureAttachmentResult
            {
                Succeeded = false,
                Error = new FeatureEditError { Code = 503, Message = "temporary outage" }
            }
        };
        using var sync = CreateSyncService(storage, service, attachmentClient);

        var failed = await sync.PushChangesAsync();

        Assert.False(failed.IsSuccess);
        var failedMetadata = await storage.GetAttachmentMetadataAsync(attachment.Id);
        Assert.Equal(AttachmentSyncStatus.UploadFailed, failedMetadata?.SyncStatus);
        Assert.Equal(1, failedMetadata?.RetryCount);
        Assert.Contains("temporary outage", failedMetadata?.LastError);

        attachmentClient.AddResult = new FeatureAttachmentResult { Succeeded = true, AttachmentId = 7 };
        var retried = await sync.PushChangesAsync();

        Assert.True(retried.IsSuccess);
        Assert.Equal(1, retried.AttachmentsPushed);
        Assert.Equal(2, attachmentClient.AddRequests.Count);
        var request = attachmentClient.AddRequests.Last();
        Assert.Equal(42, request.ObjectId);
        Assert.Equal("asset.jpg", request.Name);
        var keywords = Assert.IsType<string>(request.Keywords);
        Assert.Contains("honua.location.source=ExternalGnss", keywords, StringComparison.Ordinal);
        Assert.Contains("honua.location.accuracy_m=0.8", keywords, StringComparison.Ordinal);

        var syncedMetadata = await storage.GetAttachmentMetadataAsync(attachment.Id);
        Assert.Equal(AttachmentSyncStatus.Synced, syncedMetadata?.SyncStatus);
        Assert.Equal(7, syncedMetadata?.RemoteAttachmentId);
        Assert.Equal(0, syncedMetadata?.RetryCount);
    }

    [Fact]
    public async Task PullChangesAsync_DownloadsRemoteAttachmentsForLocalFeatures()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.ApplyRemoteFeatureAsync(CreateFeature("asset-1", objectId: 42));

        var service = new AttachmentService(storage, attachmentRoot);
        var attachmentClient = new RecordingAttachmentSyncClient
        {
            ListResponse =
            [
                new FeatureAttachmentInfo
                {
                    Source = new FeatureSource { ServiceId = "assets", LayerId = 1 },
                    ParentObjectId = 42,
                    AttachmentId = 7,
                    Name = "server.txt",
                    ContentType = "text/plain",
                    Size = 6
                }
            ],
            DownloadFactory = _ => new FeatureAttachmentContent
            {
                Info = new FeatureAttachmentInfo
                {
                    Source = new FeatureSource { ServiceId = "assets", LayerId = 1 },
                    ParentObjectId = 42,
                    AttachmentId = 7,
                    Name = "server.txt",
                    ContentType = "text/plain",
                    Size = 6
                },
                Content = new MemoryStream(Encoding.UTF8.GetBytes("server"))
            }
        };
        using var sync = CreateSyncService(storage, service, attachmentClient);

        var result = await sync.PullChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.AttachmentsPulled);
        var attachment = Assert.Single(await service.GetAttachmentsAsync("asset-1"));
        Assert.Equal(AttachmentSyncStatus.Synced, attachment.SyncStatus);
        Assert.Equal(7, attachment.RemoteAttachmentId);
        await using var downloaded = await service.GetAttachmentAsync(attachment.Id);
        using var reader = new StreamReader(downloaded, Encoding.UTF8);
        Assert.Equal("server", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task PushChangesAsync_DeletesRemoteAttachmentsAfterOfflineRemoval()
    {
        var databasePath = CreateDatabasePath();
        var attachmentRoot = CreateAttachmentRoot();
        await using var cleanup = new FileCleanup(databasePath, attachmentRoot);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.StoreFeatureAsync(CreateFeature("asset-1", objectId: 42));
        await storage.MarkChangesAsSynced((await storage.GetPendingChangesAsync()).Select(change => change.Id).ToList());

        var service = new AttachmentService(storage, attachmentRoot);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("remote"));
        var attachment = await service.SaveDownloadedAttachmentAsync(
            1,
            "asset-1",
            new FeatureAttachmentInfo
            {
                Source = new FeatureSource { ServiceId = "assets", LayerId = 1 },
                ParentObjectId = 42,
                AttachmentId = 7,
                Name = "remote.txt",
                ContentType = "text/plain",
                Size = 6
            },
            content);
        await service.DeleteAttachmentAsync(attachment.Id);

        var attachmentClient = new RecordingAttachmentSyncClient();
        using var sync = CreateSyncService(storage, service, attachmentClient);
        var result = await sync.PushChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.AttachmentsPushed);
        var deleteRequest = Assert.Single(attachmentClient.DeleteRequests);
        Assert.Equal(42, deleteRequest.ObjectId);
        Assert.Equal(7, deleteRequest.AttachmentId);

        var metadata = await storage.GetAttachmentMetadataAsync(attachment.Id);
        Assert.True(metadata?.IsDeleted);
        Assert.Equal(AttachmentSyncStatus.Synced, metadata?.SyncStatus);
        Assert.Empty(await storage.GetPendingAttachmentChangesAsync());
    }

    private static GeoPackageSyncService CreateSyncService(
        GeoPackageStorageService storage,
        IAttachmentService attachmentService,
        RecordingAttachmentSyncClient attachmentClient)
    {
        var metadata = new FixedMetadataService([CreateLayer()]);
        var synchronizer = new HonuaFieldCollectionAttachmentSynchronizer(
            storage,
            attachmentService,
            metadata,
            attachmentClient);

        return new GeoPackageSyncService(
            storage,
            new TestAuthenticationService(),
            new TestConnectivityService(),
            new FixedResultUploader(true),
            new FixedPuller([]),
            synchronizer);
    }

    private static LayerInfo CreateLayer()
    {
        return new LayerInfo
        {
            Id = 1,
            ServiceId = "assets",
            SourceId = "assets/FeatureServer/1",
            Name = "Assets",
            GeometryType = GeometryType.Point,
            IsEditable = true
        };
    }

    private static Feature CreateFeature(string id, long objectId)
    {
        return new Feature
        {
            Id = id,
            LayerId = 1,
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            Attributes = new Dictionary<string, object?>
            {
                ["objectid"] = objectId,
                ["name"] = id
            },
            Geometry = new Point(21.3, -157.8)
        };
    }

    private static string CreateDatabasePath()
    {
        return Path.Combine(Path.GetTempPath(), $"honua-field-attachments-{Guid.NewGuid():N}.gpkg");
    }

    private static string CreateAttachmentRoot()
    {
        return Path.Combine(Path.GetTempPath(), $"honua-field-attachments-{Guid.NewGuid():N}");
    }

    private static FieldLocationCaptureEvidence CreateExternalGnssEvidence()
    {
        return new FieldLocationCaptureEvidence
        {
            Latitude = 21.3069,
            Longitude = -157.8583,
            HorizontalAccuracyMeters = 0.8,
            VerticalAccuracyMeters = 1.6,
            CapturedAtUtc = new DateTimeOffset(2026, 5, 23, 9, 15, 0, TimeSpan.Zero),
            SourceKind = FieldLocationSourceKind.ExternalGnss,
            Provider = "bluetooth-nmea",
            Receiver = new FieldLocationReceiverMetadata
            {
                Name = "Trimble R12",
                Model = "R12",
                IsExternal = true
            }
        };
    }

    private sealed class RecordingAttachmentSyncClient : IFieldCollectionAttachmentSyncClient
    {
        public bool IsConfigured { get; set; } = true;
        public FeatureAttachmentResult AddResult { get; set; } = new() { Succeeded = true, AttachmentId = 7 };
        public FeatureAttachmentResult DeleteResult { get; set; } = new() { Succeeded = true, AttachmentId = 7 };
        public IReadOnlyList<FeatureAttachmentInfo> ListResponse { get; set; } = [];
        public Func<FeatureAttachmentDownloadRequest, FeatureAttachmentContent>? DownloadFactory { get; set; }
        public List<FeatureAttachmentAddRequest> AddRequests { get; } = [];
        public List<FeatureAttachmentDeleteRequest> DeleteRequests { get; } = [];
        public List<FeatureAttachmentListRequest> ListRequests { get; } = [];
        public List<FeatureAttachmentDownloadRequest> DownloadRequests { get; } = [];

        public Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsAsync(
            FeatureAttachmentListRequest request,
            CancellationToken cancellationToken = default)
        {
            ListRequests.Add(request);
            return Task.FromResult(ListResponse);
        }

        public Task<FeatureAttachmentContent> DownloadAttachmentAsync(
            FeatureAttachmentDownloadRequest request,
            CancellationToken cancellationToken = default)
        {
            DownloadRequests.Add(request);
            return Task.FromResult(DownloadFactory?.Invoke(request) ?? new FeatureAttachmentContent
            {
                Info = new FeatureAttachmentInfo
                {
                    Source = request.Source,
                    ParentObjectId = request.ObjectId,
                    AttachmentId = request.AttachmentId,
                    Name = "attachment.bin",
                    ContentType = "application/octet-stream"
                },
                Content = new MemoryStream()
            });
        }

        public Task<FeatureAttachmentResult> AddAttachmentAsync(
            FeatureAttachmentAddRequest request,
            CancellationToken cancellationToken = default)
        {
            AddRequests.Add(request);
            return Task.FromResult(AddResult);
        }

        public Task<FeatureAttachmentResult> DeleteAttachmentAsync(
            FeatureAttachmentDeleteRequest request,
            CancellationToken cancellationToken = default)
        {
            DeleteRequests.Add(request);
            return Task.FromResult(DeleteResult);
        }
    }

    private sealed class FixedResultUploader : IFieldCollectionChangeUploader
    {
        private readonly bool _result;

        public FixedResultUploader(bool result)
        {
            _result = result;
        }

        public Task<bool> UploadChangeAsync(
            StorageChangeRecord change,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class FixedPuller : IFieldCollectionChangePuller
    {
        private readonly IReadOnlyList<ServerChange> _changes;

        public FixedPuller(IReadOnlyList<ServerChange> changes)
        {
            _changes = changes;
        }

        public Task<IReadOnlyList<ServerChange>> GetChangesAsync(
            long sinceGeneration,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_changes);
        }

        public Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1L);
        }

        public Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0L);
        }
    }

    private sealed class FixedMetadataService : IFieldCollectionMetadataService
    {
        private readonly IReadOnlyList<LayerInfo> _layers;

        public FixedMetadataService(IReadOnlyList<LayerInfo> layers)
        {
            _layers = layers;
        }

        public Task<IReadOnlyList<FieldProjectInfo>> GetProjectsAsync(
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FieldProjectInfo>>([]);
        }

        public Task<FieldProjectInfo?> GetSelectedProjectAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<FieldProjectInfo?>(new FieldProjectInfo
            {
                ServiceId = _layers.FirstOrDefault()?.ServiceId ?? "assets",
                Layers = _layers.ToList()
            });
        }

        public Task SelectProjectAsync(string serviceId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LayerInfo>> GetLayersAsync(
            bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_layers);
        }
    }

    private sealed class FileCleanup : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly string _attachmentRoot;

        public FileCleanup(string databasePath, string attachmentRoot)
        {
            _databasePath = databasePath;
            _attachmentRoot = attachmentRoot;
        }

        public ValueTask DisposeAsync()
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }

            if (Directory.Exists(_attachmentRoot))
            {
                Directory.Delete(_attachmentRoot, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}
