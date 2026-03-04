using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.Sync;

public sealed class OfflineSyncEngine : IOfflineSyncRunner
{
    private readonly IGeoPackageSyncStore _store;
    private readonly IOfflineOperationUploader _uploader;
    private readonly OfflineSyncEngineOptions _options;

    public OfflineSyncEngine(IGeoPackageSyncStore store, IOfflineOperationUploader uploader, OfflineSyncEngineOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _options = options ?? new OfflineSyncEngineOptions();
    }

    public async Task<SyncRunResult> SyncAsync(CancellationToken ct = default)
    {
        await _store.InitializeAsync(ct).ConfigureAwait(false);
        var pending = await _store.GetPendingAsync(_options.BatchSize, ct).ConfigureAwait(false);

        var failures = new List<SyncFailure>();
        var success = 0;

        foreach (var operation in pending)
        {
            ct.ThrowIfCancellationRequested();

            if (operation.AttemptCount >= _options.MaxAttempts)
            {
                await _store.MarkFailedAsync(operation.OperationId, "max attempts reached", retryable: false, ct).ConfigureAwait(false);
                failures.Add(new SyncFailure(operation.OperationId, "max attempts reached"));
                continue;
            }

            var uploadResult = await _uploader.UploadAsync(operation, forceWrite: false, ct).ConfigureAwait(false);
            if (uploadResult.Outcome == UploadOutcome.Success)
            {
                await _store.MarkSucceededAsync(operation.OperationId, ct).ConfigureAwait(false);
                success++;
                continue;
            }

            if (uploadResult.Outcome == UploadOutcome.Conflict)
            {
                var conflictResolved = await HandleConflictAsync(operation, ct).ConfigureAwait(false);
                if (conflictResolved)
                {
                    success++;
                    continue;
                }
            }

            var retryable = uploadResult.Outcome == UploadOutcome.RetryableFailure;
            var reason = uploadResult.Message ?? uploadResult.Outcome.ToString();
            await _store.MarkFailedAsync(operation.OperationId, reason, retryable, ct).ConfigureAwait(false);
            failures.Add(new SyncFailure(operation.OperationId, reason));
        }

        return new SyncRunResult
        {
            Loaded = pending.Count,
            Succeeded = success,
            Failed = failures.Count,
            Failures = failures,
        };
    }

    private async Task<bool> HandleConflictAsync(OfflineEditOperation operation, CancellationToken ct)
    {
        switch (_options.ConflictStrategy)
        {
            case SyncConflictStrategy.ServerWins:
                await _store.MarkSucceededAsync(operation.OperationId, ct).ConfigureAwait(false);
                return true;

            case SyncConflictStrategy.ClientWins:
            {
                var forced = await _uploader.UploadAsync(operation, forceWrite: true, ct).ConfigureAwait(false);
                if (forced.Outcome == UploadOutcome.Success)
                {
                    await _store.MarkSucceededAsync(operation.OperationId, ct).ConfigureAwait(false);
                    return true;
                }

                var reason = forced.Message ?? "conflict retry failed";
                await _store.MarkFailedAsync(operation.OperationId, reason, retryable: forced.Outcome == UploadOutcome.RetryableFailure, ct).ConfigureAwait(false);
                return false;
            }

            case SyncConflictStrategy.ManualReview:
            default:
                await _store.MarkFailedAsync(operation.OperationId, "conflict requires manual review", retryable: false, ct).ConfigureAwait(false);
                return false;
        }
    }
}
