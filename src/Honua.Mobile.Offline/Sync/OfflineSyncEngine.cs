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

        for (var index = 0; index < pending.Count; index++)
        {
            var operation = pending[index];

            try
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
                    if (conflictResolved.Resolved)
                    {
                        success++;
                        continue;
                    }

                    if (conflictResolved.FailureHandled)
                    {
                        failures.Add(new SyncFailure(operation.OperationId, conflictResolved.FailureReason ?? "Conflict resolution failed."));
                        continue;
                    }
                }

                var retryable = uploadResult.Outcome == UploadOutcome.RetryableFailure;
                var reason = uploadResult.Message ?? uploadResult.Outcome.ToString();
                await _store.MarkFailedAsync(operation.OperationId, reason, retryable, ct).ConfigureAwait(false);
                failures.Add(new SyncFailure(operation.OperationId, reason));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await ReleaseClaimedOperationsAsync(pending, index).ConfigureAwait(false);
                throw;
            }
            catch
            {
                await ReleaseClaimedOperationsAsync(pending, index).ConfigureAwait(false);
                throw;
            }
        }

        return new SyncRunResult
        {
            Loaded = pending.Count,
            Succeeded = success,
            Failed = failures.Count,
            Failures = failures,
        };
    }

    private async Task<ConflictResolutionState> HandleConflictAsync(OfflineEditOperation operation, CancellationToken ct)
    {
        switch (_options.ConflictStrategy)
        {
            case SyncConflictStrategy.ServerWins:
                await _store.MarkSucceededAsync(operation.OperationId, ct).ConfigureAwait(false);
                return ConflictResolutionState.ResolvedState;

            case SyncConflictStrategy.ClientWins:
                {
                    var forced = await _uploader.UploadAsync(operation, forceWrite: true, ct).ConfigureAwait(false);
                    if (forced.Outcome == UploadOutcome.Success)
                    {
                        await _store.MarkSucceededAsync(operation.OperationId, ct).ConfigureAwait(false);
                        return ConflictResolutionState.ResolvedState;
                    }

                    var reason = forced.Message ?? "conflict retry failed";
                    await _store.MarkFailedAsync(operation.OperationId, reason, retryable: forced.Outcome == UploadOutcome.RetryableFailure, ct).ConfigureAwait(false);
                    return new ConflictResolutionState(false, true, reason);
                }

            case SyncConflictStrategy.ManualReview:
            default:
                const string manualReviewReason = "conflict requires manual review";
                await _store.MarkFailedAsync(operation.OperationId, manualReviewReason, retryable: false, ct).ConfigureAwait(false);
                return new ConflictResolutionState(false, true, manualReviewReason);
        }
    }

    private readonly record struct ConflictResolutionState(bool Resolved, bool FailureHandled, string? FailureReason)
    {
        public static readonly ConflictResolutionState ResolvedState = new(true, false, null);
    }

    private async Task ReleaseClaimedOperationsAsync(IReadOnlyList<OfflineEditOperation> pending, int startIndex)
    {
        for (var i = startIndex; i < pending.Count; i++)
        {
            await _store.MarkPendingAsync(pending[i].OperationId, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
