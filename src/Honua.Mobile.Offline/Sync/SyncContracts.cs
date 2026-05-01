using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Outcome of an individual offline operation upload attempt.
/// </summary>
public enum UploadOutcome
{
    /// <summary>The operation was applied successfully on the server.</summary>
    Success,
    /// <summary>The server reported a version conflict.</summary>
    Conflict,
    /// <summary>A transient failure occurred; the operation can be retried.</summary>
    RetryableFailure,
    /// <summary>A permanent failure occurred; the operation should not be retried.</summary>
    FatalFailure,
}

/// <summary>
/// Result of uploading a single <see cref="OfflineEditOperation"/> to the server.
/// </summary>
public sealed class UploadResult
{
    /// <summary>
    /// The outcome of the upload attempt.
    /// </summary>
    public UploadOutcome Outcome { get; init; }

    /// <summary>
    /// Optional message from the server describing the outcome or error.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Uploads a single offline edit operation to the server.
/// </summary>
public interface IOfflineOperationUploader
{
    /// <summary>
    /// Uploads <paramref name="operation"/> to the server.
    /// </summary>
    /// <param name="operation">The offline edit operation to upload.</param>
    /// <param name="forceWrite">When <see langword="true"/>, bypasses version checks (client-wins conflict resolution).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The upload result indicating success, conflict, or failure.</returns>
    Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default);
}

/// <summary>
/// Runs a single sync cycle: claims pending offline edits and uploads them to the server.
/// </summary>
public interface IOfflineSyncRunner
{
    /// <summary>
    /// Executes a full sync cycle, processing all pending operations up to the configured batch size.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A summary of the sync run including success/failure counts.</returns>
    Task<SyncRunResult> SyncAsync(CancellationToken ct = default);
}

/// <summary>
/// Provides the current network connectivity state.
/// </summary>
public interface IConnectivityStateProvider
{
    /// <summary>
    /// <see langword="true"/> when the device has network connectivity.
    /// </summary>
    bool IsOnline { get; }
}

/// <summary>
/// Strategy for resolving conflicts during offline sync.
/// </summary>
public enum SyncConflictStrategy
{
    /// <summary>Re-upload with force-write to overwrite the server version.</summary>
    ClientWins,
    /// <summary>Discard the local edit and accept the server version.</summary>
    ServerWins,
    /// <summary>Mark the operation as failed for manual resolution.</summary>
    ManualReview,
}

/// <summary>
/// Configuration options for <see cref="OfflineSyncEngine"/>.
/// </summary>
public sealed class OfflineSyncEngineOptions
{
    /// <summary>
    /// Maximum number of operations to process per sync cycle. Defaults to 50.
    /// </summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Maximum upload attempts before an operation is permanently failed. Defaults to 8.
    /// </summary>
    public int MaxAttempts { get; init; } = 8;

    /// <summary>
    /// Strategy for resolving version conflicts. Defaults to <see cref="SyncConflictStrategy.ManualReview"/>.
    /// </summary>
    public SyncConflictStrategy ConflictStrategy { get; init; } = SyncConflictStrategy.ManualReview;
}

/// <summary>
/// Configuration options for <see cref="BackgroundSyncOrchestrator"/>.
/// </summary>
public sealed class BackgroundSyncOrchestratorOptions
{
    /// <summary>
    /// Interval between sync cycles. Defaults to 1 minute.
    /// </summary>
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// When <see langword="true"/>, the first sync cycle runs immediately on start. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RunImmediately { get; init; } = true;
}

/// <summary>
/// Lifecycle event that should cancel optional background prefetch work.
/// </summary>
public enum PrefetchLifecycleEvent
{
    /// <summary>
    /// The app is entering the background or suspended state.
    /// </summary>
    Suspend,

    /// <summary>
    /// The platform reported memory pressure.
    /// </summary>
    LowMemory,

    /// <summary>
    /// The scheduler is shutting down.
    /// </summary>
    Shutdown,
}

/// <summary>
/// Configuration options for <see cref="BackgroundPrefetchScheduler"/>.
/// </summary>
public sealed class BackgroundPrefetchSchedulerOptions
{
    /// <summary>
    /// Maximum number of prefetch items allowed to run at the same time.
    /// </summary>
    public int MaxConcurrency { get; init; } = 2;
}

/// <summary>
/// Represents a cancellable background prefetch task.
/// </summary>
public interface IBackgroundPrefetchWorkItem
{
    /// <summary>
    /// Stable identifier used for diagnostics.
    /// </summary>
    string ItemId { get; }

    /// <summary>
    /// Executes the prefetch work.
    /// </summary>
    /// <param name="ct">Cancellation token that is triggered for app suspend, low memory, or shutdown.</param>
    Task ExecuteAsync(CancellationToken ct = default);
}

/// <summary>
/// Summary of a single offline sync cycle.
/// </summary>
public sealed class SyncRunResult
{
    /// <summary>
    /// Total number of operations loaded from the queue.
    /// </summary>
    public int Loaded { get; init; }

    /// <summary>
    /// Number of operations successfully uploaded.
    /// </summary>
    public int Succeeded { get; init; }

    /// <summary>
    /// Number of operations that failed.
    /// </summary>
    public int Failed { get; init; }

    /// <summary>
    /// Details of each failed operation.
    /// </summary>
    public IReadOnlyList<SyncFailure> Failures { get; init; } = [];
}

/// <summary>
/// Describes a single operation that failed during sync.
/// </summary>
/// <param name="OperationId">The ID of the failed operation.</param>
/// <param name="Reason">A human-readable description of the failure.</param>
public sealed record SyncFailure(string OperationId, string Reason);

/// <summary>
/// Configuration options for <see cref="DeltaDownloadEngine"/>.
/// </summary>
public sealed class DeltaDownloadOptions
{
    /// <summary>
    /// Custom replica name. When <see langword="null"/>, a name is auto-generated from the service ID.
    /// </summary>
    public string? ReplicaName { get; init; }

    /// <summary>
    /// Optional layer IDs to include in the replica. When <see langword="null"/>, all layers are included.
    /// </summary>
    public int[]? LayerIds { get; init; }
}

/// <summary>
/// Summary of a delta download cycle.
/// </summary>
public sealed class DeltaDownloadResult
{
    /// <summary>
    /// Number of features added to the local cache.
    /// </summary>
    public int Adds { get; init; }

    /// <summary>
    /// Number of features updated in the local cache.
    /// </summary>
    public int Updates { get; init; }

    /// <summary>
    /// Number of features deleted from the local cache.
    /// </summary>
    public int Deletes { get; init; }

    /// <summary>
    /// Server generation number after synchronization.
    /// </summary>
    public long ServerGen { get; init; }
}
