namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>
/// Abstraction for the local GeoPackage-based sync store that manages offline edit queues,
/// sync cursors, map area metadata, and replicated feature caches.
/// </summary>
public interface IGeoPackageSyncStore
{
    /// <summary>
    /// Creates the required database tables if they do not already exist.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Enqueues an offline edit operation for later upload. Upserts on <see cref="OfflineEditOperation.OperationId"/>.
    /// </summary>
    /// <param name="operation">The edit operation to enqueue.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EnqueueAsync(OfflineEditOperation operation, CancellationToken ct = default);

    /// <summary>
    /// Claims up to <paramref name="maxCount"/> pending operations, marking them as in-progress.
    /// Stale claims older than the configured lease timeout are reclaimed.
    /// </summary>
    /// <param name="maxCount">Maximum number of operations to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The claimed operations ordered by priority then creation time.</returns>
    Task<IReadOnlyList<OfflineEditOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default);

    /// <summary>
    /// Returns the number of operations in pending or retry status.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Count of pending operations.</returns>
    Task<int> CountPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// Removes a successfully uploaded operation from the queue.
    /// </summary>
    /// <param name="operationId">The operation ID to mark as succeeded.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkSucceededAsync(string operationId, CancellationToken ct = default);

    /// <summary>
    /// Resets an operation back to pending status, clearing its claim and error state.
    /// </summary>
    /// <param name="operationId">The operation ID to reset.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkPendingAsync(string operationId, CancellationToken ct = default);

    /// <summary>
    /// Records a failure for the operation, incrementing its attempt count.
    /// </summary>
    /// <param name="operationId">The operation ID that failed.</param>
    /// <param name="failureReason">Human-readable reason for the failure.</param>
    /// <param name="retryable">When <see langword="true"/>, the operation is set to retry status; otherwise it is permanently failed.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkFailedAsync(string operationId, string failureReason, bool retryable, CancellationToken ct = default);

    /// <summary>
    /// Persists a sync cursor value (e.g., replica ID or server generation number).
    /// </summary>
    /// <param name="cursorKey">Unique key identifying the cursor.</param>
    /// <param name="cursorValue">The cursor value to store.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSyncCursorAsync(string cursorKey, string cursorValue, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a previously stored sync cursor value.
    /// </summary>
    /// <param name="cursorKey">Unique key identifying the cursor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cursor value, or <see langword="null"/> if not found.</returns>
    Task<string?> GetSyncCursorAsync(string cursorKey, CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a downloaded map area package record.
    /// </summary>
    /// <param name="mapArea">The map area metadata to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertMapAreaAsync(MapAreaPackage mapArea, CancellationToken ct = default);

    /// <summary>
    /// Lists all downloaded map area packages ordered by name.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All stored map area packages.</returns>
    Task<IReadOnlyList<MapAreaPackage>> ListMapAreasAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts or updates a replicated feature in the local cache.
    /// The object ID is extracted from the JSON payload.
    /// </summary>
    /// <param name="layerKey">The layer identifier for the feature.</param>
    /// <param name="featureJson">The full feature JSON including attributes and geometry.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpsertFeatureAsync(string layerKey, string featureJson, CancellationToken ct = default);

    /// <summary>
    /// Deletes a replicated feature from the local cache.
    /// </summary>
    /// <param name="layerKey">The layer identifier for the feature.</param>
    /// <param name="objectId">The object ID of the feature to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteFeatureAsync(string layerKey, long objectId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all cached feature JSON payloads for a given layer, ordered by object ID.
    /// </summary>
    /// <param name="layerKey">The layer identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Feature JSON strings for the specified layer.</returns>
    Task<IReadOnlyList<string>> GetFeaturesAsync(string layerKey, CancellationToken ct = default);
}
