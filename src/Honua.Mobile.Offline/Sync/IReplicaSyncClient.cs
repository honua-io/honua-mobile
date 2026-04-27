namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Client for the server-side replica sync API used to create replicas,
/// extract changes, synchronize, and unregister replicas.
/// </summary>
public interface IReplicaSyncClient
{
    /// <summary>
    /// Creates a new server-side replica for the specified service.
    /// </summary>
    /// <param name="serviceId">The feature service ID.</param>
    /// <param name="replicaName">A name for the new replica.</param>
    /// <param name="layerIds">Optional layer IDs to include. When <see langword="null"/>, all layers are included.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created replica ID and initial server generation number.</returns>
    Task<CreateReplicaResult> CreateReplicaAsync(string serviceId, string replicaName, int[]? layerIds = null, CancellationToken ct = default);

    /// <summary>
    /// Extracts feature changes (adds, updates, deletes) from the server since the last synchronization.
    /// </summary>
    /// <param name="serviceId">The feature service ID.</param>
    /// <param name="replicaId">The replica ID obtained from <see cref="CreateReplicaAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Layer-level change sets and the current server generation number.</returns>
    Task<ExtractChangesResult> ExtractChangesAsync(string serviceId, string replicaId, CancellationToken ct = default);

    /// <summary>
    /// Acknowledges received changes and advances the replica's server generation number.
    /// </summary>
    /// <param name="serviceId">The feature service ID.</param>
    /// <param name="replicaId">The replica ID.</param>
    /// <param name="syncDirection">Sync direction (e.g., <c>"download"</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated replica ID and server generation number.</returns>
    Task<SynchronizeResult> SynchronizeReplicaAsync(string serviceId, string replicaId, string syncDirection = "download", CancellationToken ct = default);

    /// <summary>
    /// Unregisters a replica from the server, freeing server-side resources.
    /// </summary>
    /// <param name="serviceId">The feature service ID.</param>
    /// <param name="replicaId">The replica ID to unregister.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnRegisterReplicaAsync(string serviceId, string replicaId, CancellationToken ct = default);
}

/// <summary>
/// Result of creating a new server-side replica.
/// </summary>
/// <param name="ReplicaId">The unique identifier assigned to the replica by the server.</param>
/// <param name="ServerGen">The initial server generation number.</param>
public sealed record CreateReplicaResult(string ReplicaId, long ServerGen);

/// <summary>
/// Result of synchronizing a replica.
/// </summary>
/// <param name="ReplicaId">The replica identifier.</param>
/// <param name="ServerGen">The updated server generation number after synchronization.</param>
public sealed record SynchronizeResult(string ReplicaId, long ServerGen);

/// <summary>
/// Contains layer-level change sets extracted from the server.
/// </summary>
public sealed class ExtractChangesResult
{
    /// <summary>
    /// Per-layer change sets containing added, updated, and deleted features.
    /// </summary>
    public required LayerChangeSet[] LayerChanges { get; init; }

    /// <summary>
    /// The server generation number at the time changes were extracted.
    /// </summary>
    public long ServerGen { get; init; }
}

/// <summary>
/// Feature changes for a single layer within an <see cref="ExtractChangesResult"/>.
/// </summary>
public sealed class LayerChangeSet
{
    /// <summary>
    /// The numeric layer ID.
    /// </summary>
    public int LayerId { get; init; }

    /// <summary>
    /// JSON representations of newly added features, or <see langword="null"/> if none.
    /// </summary>
    public string[]? AddFeaturesJson { get; init; }

    /// <summary>
    /// JSON representations of updated features, or <see langword="null"/> if none.
    /// </summary>
    public string[]? UpdateFeaturesJson { get; init; }

    /// <summary>
    /// Object IDs of deleted features, or <see langword="null"/> if none.
    /// </summary>
    public long[]? DeleteIds { get; init; }
}
