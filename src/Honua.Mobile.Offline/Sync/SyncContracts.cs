using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.Sync;

public enum UploadOutcome
{
    Success,
    Conflict,
    RetryableFailure,
    FatalFailure,
}

public sealed class UploadResult
{
    public UploadOutcome Outcome { get; init; }

    public string? Message { get; init; }
}

public interface IOfflineOperationUploader
{
    Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default);
}

public enum SyncConflictStrategy
{
    ClientWins,
    ServerWins,
    ManualReview,
}

public sealed class OfflineSyncEngineOptions
{
    public int BatchSize { get; init; } = 50;

    public int MaxAttempts { get; init; } = 8;

    public SyncConflictStrategy ConflictStrategy { get; init; } = SyncConflictStrategy.ManualReview;
}

public sealed class SyncRunResult
{
    public int Loaded { get; init; }

    public int Succeeded { get; init; }

    public int Failed { get; init; }

    public IReadOnlyList<SyncFailure> Failures { get; init; } = [];
}

public sealed record SyncFailure(string OperationId, string Reason);
