using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Telemetry sources emitted by the Honua mobile sync layer.
/// </summary>
public static class MobileSyncTelemetry
{
    /// <summary>
    /// Activity source name for mobile sync operations.
    /// </summary>
    public const string ActivitySourceName = "Honua.Mobile.Sync";

    /// <summary>
    /// Meter name for mobile sync metrics.
    /// </summary>
    public const string MeterName = "Honua.Mobile.Sync";

    /// <summary>
    /// Activity source for offline sync runs.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> SyncRuns = Meter.CreateCounter<long>(
        "mobile_sync_runs_total",
        description: "Number of mobile sync runs by result.");
    private static readonly Counter<long> SyncConflicts = Meter.CreateCounter<long>(
        "mobile_sync_conflicts_total",
        description: "Number of mobile sync conflicts by applied strategy.");
    private static long _pendingOperations;

    static MobileSyncTelemetry()
    {
        Meter.CreateObservableGauge(
            "mobile_pending_operations",
            () => Volatile.Read(ref _pendingOperations),
            description: "Current count of pending or retryable mobile sync operations.");
    }

    /// <summary>
    /// Records a completed sync run.
    /// </summary>
    /// <param name="result">Run result label.</param>
    public static void RecordRun(string result)
        => SyncRuns.Add(1, new KeyValuePair<string, object?>("result", result));

    /// <summary>
    /// Records a conflict handled by the specified strategy.
    /// </summary>
    /// <param name="strategy">Conflict strategy label.</param>
    public static void RecordConflict(SyncConflictStrategy strategy)
        => SyncConflicts.Add(1, new KeyValuePair<string, object?>("strategy", strategy.ToString()));

    /// <summary>
    /// Updates the pending operation gauge.
    /// </summary>
    /// <param name="pendingOperations">Current pending/retryable operation count.</param>
    public static void RecordPendingOperations(long pendingOperations)
        => Volatile.Write(ref _pendingOperations, Math.Max(0, pendingOperations));
}
