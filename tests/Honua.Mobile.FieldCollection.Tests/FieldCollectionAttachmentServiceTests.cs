using System.Text;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
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
            AttachmentPayloadKind.Photo);

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
