using Microsoft.Extensions.Logging;
using StorageChangeRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeRecord;

namespace Honua.Mobile.FieldCollection.Services.Sync;

internal sealed class QueuedFieldCollectionChangeUploader : IFieldCollectionChangeUploader
{
    private readonly ILogger<QueuedFieldCollectionChangeUploader>? _logger;

    public QueuedFieldCollectionChangeUploader(ILogger<QueuedFieldCollectionChangeUploader>? logger = null)
    {
        _logger = logger;
    }

    public Task<bool> UploadChangeAsync(StorageChangeRecord change, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger?.LogWarning(
            "Field collection change {ChangeId} for feature {FeatureId} in layer {LayerId} remains queued because remote field sync is not configured",
            change.Id,
            change.FeatureId,
            change.LayerId);

        return Task.FromResult(false);
    }
}

internal sealed class LocalOnlyFieldCollectionChangePuller : IFieldCollectionChangePuller
{
    private readonly ILogger<LocalOnlyFieldCollectionChangePuller>? _logger;

    public LocalOnlyFieldCollectionChangePuller(ILogger<LocalOnlyFieldCollectionChangePuller>? logger = null)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<ServerChange>> GetChangesAsync(
        long sinceGeneration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger?.LogDebug(
            "Field collection pull requested from generation {Generation}; remote field sync is local-only",
            sinceGeneration);
        return Task.FromResult<IReadOnlyList<ServerChange>>([]);
    }

    public Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }

    public Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }
}
