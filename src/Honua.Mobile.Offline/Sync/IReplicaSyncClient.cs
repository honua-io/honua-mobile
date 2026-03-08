namespace Honua.Mobile.Offline.Sync;

public interface IReplicaSyncClient
{
    Task<CreateReplicaResult> CreateReplicaAsync(string serviceId, string replicaName, int[]? layerIds = null, CancellationToken ct = default);

    Task<ExtractChangesResult> ExtractChangesAsync(string serviceId, string replicaId, CancellationToken ct = default);

    Task<SynchronizeResult> SynchronizeReplicaAsync(string serviceId, string replicaId, string syncDirection = "download", CancellationToken ct = default);

    Task UnRegisterReplicaAsync(string serviceId, string replicaId, CancellationToken ct = default);
}

public sealed record CreateReplicaResult(string ReplicaId, long ServerGen);

public sealed record SynchronizeResult(string ReplicaId, long ServerGen);

public sealed class ExtractChangesResult
{
    public required LayerChangeSet[] LayerChanges { get; init; }

    public long ServerGen { get; init; }
}

public sealed class LayerChangeSet
{
    public int LayerId { get; init; }

    public string[]? AddFeaturesJson { get; init; }

    public string[]? UpdateFeaturesJson { get; init; }

    public long[]? DeleteIds { get; init; }
}
