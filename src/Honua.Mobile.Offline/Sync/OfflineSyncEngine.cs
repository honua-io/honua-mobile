using System.Diagnostics;
using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Processes pending offline edit operations by uploading them to the server,
/// handling conflicts according to the configured <see cref="SyncConflictStrategy"/>.
/// </summary>
public sealed class OfflineSyncEngine : IOfflineSyncRunner
{
    private readonly IGeoPackageSyncStore _store;
    private readonly IOfflineOperationUploader _uploader;
    private readonly OfflineSyncEngineOptions _options;

    /// <summary>
    /// Initializes a new <see cref="OfflineSyncEngine"/>.
    /// </summary>
    /// <param name="store">The local sync store for reading and updating operation status.</param>
    /// <param name="uploader">The uploader responsible for sending operations to the server.</param>
    /// <param name="options">Engine options; defaults are used when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> or <paramref name="uploader"/> is <see langword="null"/>.</exception>
    public OfflineSyncEngine(IGeoPackageSyncStore store, IOfflineOperationUploader uploader, OfflineSyncEngineOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _options = options ?? new OfflineSyncEngineOptions();
    }

    /// <inheritdoc />
    public async Task<SyncRunResult> SyncAsync(CancellationToken ct = default)
    {
        using var activity = MobileSyncTelemetry.ActivitySource.StartActivity("honua.mobile.sync.run", ActivityKind.Internal);
        try
        {
            await _store.InitializeAsync(ct).ConfigureAwait(false);
            await RecordPendingGaugeAsync(ct).ConfigureAwait(false);
            var pending = await _store.GetPendingAsync(_options.BatchSize, ct).ConfigureAwait(false);
            activity?.SetTag("honua.mobile.sync.loaded", pending.Count);

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

                    var uploadResult = await UploadSafelyAsync(operation, forceWrite: false, ct).ConfigureAwait(false);
                    if (uploadResult.Outcome == UploadOutcome.Success)
                    {
                        await _store.MarkSucceededAsync(operation.OperationId, ct).ConfigureAwait(false);
                        success++;
                        continue;
                    }

                    if (uploadResult.Outcome == UploadOutcome.Conflict)
                    {
                        var strategy = ResolveConflictStrategy(operation);
                        var conflictResolved = await HandleConflictAsync(operation, strategy, ct).ConfigureAwait(false);
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
                    await ReleaseClaimedOperationsBestEffortAsync(pending, index).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await ReleaseClaimedOperationsBestEffortAsync(pending, index).ConfigureAwait(false);
                    throw new MobileSyncException(MobileSyncProblemHelper.FromException(ex), ex);
                }
            }

            var result = new SyncRunResult
            {
                Loaded = pending.Count,
                Succeeded = success,
                Failed = failures.Count,
                Failures = failures,
            };

            activity?.SetTag("honua.mobile.sync.succeeded", result.Succeeded);
            activity?.SetTag("honua.mobile.sync.failed", result.Failed);
            var resultLabel = result.Failed == 0 ? "succeeded" : result.Succeeded == 0 ? "failed" : "partial_failure";
            MobileSyncTelemetry.RecordRun(resultLabel);
            activity?.SetStatus(result.Failed == 0 ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            await RecordPendingGaugeAsync(CancellationToken.None).ConfigureAwait(false);

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            MobileSyncTelemetry.RecordRun("cancelled");
            activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
            await RecordPendingGaugeAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (MobileSyncException ex)
        {
            MobileSyncTelemetry.RecordRun("exception");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Problem.Message);
            await RecordPendingGaugeAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            var problem = MobileSyncProblemHelper.FromException(ex);
            MobileSyncTelemetry.RecordRun("exception");
            activity?.SetStatus(ActivityStatusCode.Error, problem.Message);
            await RecordPendingGaugeAsync(CancellationToken.None).ConfigureAwait(false);
            throw new MobileSyncException(problem, ex);
        }
    }

    private SyncConflictStrategy ResolveConflictStrategy(OfflineEditOperation operation)
    {
        var selected = _options.ConflictStrategy;
        var selectedSpecificity = -1;

        foreach (var rule in _options.ConflictPolicyRules)
        {
            if (!rule.Matches(operation) || rule.Specificity <= selectedSpecificity)
            {
                continue;
            }

            selected = rule.Strategy;
            selectedSpecificity = rule.Specificity;
        }

        return selected;
    }

    private async Task<ConflictResolutionState> HandleConflictAsync(
        OfflineEditOperation operation,
        SyncConflictStrategy strategy,
        CancellationToken ct)
    {
        MobileSyncTelemetry.RecordConflict(strategy);

        switch (strategy)
        {
            case SyncConflictStrategy.ServerWins:
                await _store.MarkSucceededAsync(operation.OperationId, ct).ConfigureAwait(false);
                return ConflictResolutionState.ResolvedState;

            case SyncConflictStrategy.ClientWins:
                {
                    var forced = await UploadSafelyAsync(operation, forceWrite: true, ct).ConfigureAwait(false);
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

    private async Task<UploadResult> UploadSafelyAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct)
    {
        try
        {
            return await _uploader.UploadAsync(operation, forceWrite, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MobileSyncProblemHelper.ToUploadResult(ex);
        }
    }

    private readonly record struct ConflictResolutionState(bool Resolved, bool FailureHandled, string? FailureReason)
    {
        public static readonly ConflictResolutionState ResolvedState = new(true, false, null);
    }

    private async Task ReleaseClaimedOperationsBestEffortAsync(IReadOnlyList<OfflineEditOperation> pending, int startIndex)
    {
        try
        {
            for (var i = startIndex; i < pending.Count; i++)
            {
                await _store.MarkPendingAsync(pending[i].OperationId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // Preserve the original sync failure/cancellation surface.
        }
    }

    private async Task RecordPendingGaugeAsync(CancellationToken ct)
    {
        try
        {
            MobileSyncTelemetry.RecordPendingOperations(await _store.CountPendingAsync(ct).ConfigureAwait(false));
        }
        catch
        {
            // Telemetry must not change sync behavior.
        }
    }
}
