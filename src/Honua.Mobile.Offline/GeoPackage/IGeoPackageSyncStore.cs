namespace Honua.Mobile.Offline.GeoPackage;

public interface IGeoPackageSyncStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task EnqueueAsync(OfflineEditOperation operation, CancellationToken ct = default);

    Task<IReadOnlyList<OfflineEditOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default);

    Task<int> CountPendingAsync(CancellationToken ct = default);

    Task MarkSucceededAsync(string operationId, CancellationToken ct = default);

    Task MarkPendingAsync(string operationId, CancellationToken ct = default);

    Task MarkFailedAsync(string operationId, string failureReason, bool retryable, CancellationToken ct = default);

    Task SetSyncCursorAsync(string cursorKey, string cursorValue, CancellationToken ct = default);

    Task<string?> GetSyncCursorAsync(string cursorKey, CancellationToken ct = default);

    Task UpsertMapAreaAsync(MapAreaPackage mapArea, CancellationToken ct = default);

    Task<IReadOnlyList<MapAreaPackage>> ListMapAreasAsync(CancellationToken ct = default);

    Task UpsertFeatureAsync(string layerKey, string featureJson, CancellationToken ct = default);

    Task DeleteFeatureAsync(string layerKey, long objectId, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetFeaturesAsync(string layerKey, CancellationToken ct = default);
}
