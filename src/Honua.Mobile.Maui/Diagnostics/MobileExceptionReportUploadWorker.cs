// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Microsoft.Extensions.Logging;

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Uploads pending sanitized exception reports and deletes queue files only after successful delivery.
/// </summary>
public sealed class MobileExceptionReportUploadWorker : IMobileExceptionReportUploadWorker
{
    private readonly IMobileExceptionReportQueue _queue;
    private readonly IMobileExceptionReportUploader _uploader;
    private readonly MobileExceptionReportingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MobileExceptionReportUploadWorker>? _logger;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly object _retryGate = new();
    private readonly Dictionary<string, RetryState> _retryStates = new(StringComparer.Ordinal);

    public MobileExceptionReportUploadWorker(
        IMobileExceptionReportQueue queue,
        IMobileExceptionReportUploader uploader,
        MobileExceptionReportingOptions options,
        TimeProvider? timeProvider = null,
        ILogger<MobileExceptionReportUploadWorker>? logger = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _uploader = uploader ?? throw new ArgumentNullException(nameof(uploader));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    public async Task FlushPendingAsync(CancellationToken cancellationToken = default)
    {
        await _flushGate.WaitAsync(cancellationToken);
        try
        {
            await FlushPendingCoreAsync(cancellationToken);
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task FlushPendingCoreAsync(CancellationToken cancellationToken)
    {
        if (_options.Mode != MobileExceptionReportingMode.ServerUpload)
        {
            return;
        }

        var attempted = 0;
        var seenQueueIds = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var queued in _queue.ReadPendingAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Track every still-pending queue id so stale retry state can be pruned
            // below. We keep enumerating past the upload batch cap (only skipping the
            // upload) so the seen-set reflects the full pending queue, not just the
            // batch we attempted this pass.
            seenQueueIds.Add(queued.QueueId);

            if (attempted >= _options.MaxUploadBatchSize)
            {
                continue;
            }

            var now = _timeProvider.GetUtcNow();
            if (!IsDue(queued.QueueId, now))
            {
                continue;
            }

            bool sent;
            try
            {
                attempted++;
                sent = await _uploader.UploadAsync(queued.Report, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Mobile exception uploader failed unexpectedly");
                sent = false;
            }

            if (sent)
            {
                await _queue.DeleteAsync(queued, cancellationToken);
                ClearRetry(queued.QueueId);
                continue;
            }

            ScheduleRetry(queued.QueueId, now);
        }

        // Drop retry state for reports that are no longer on disk. Queue files can be
        // removed out-of-band (e.g. FileMobileExceptionReportQueue.TrimQueue deleting
        // excess reports when MaxQueuedReports is exceeded) without routing through the
        // worker's success/delete path, which would otherwise leak one stale entry per
        // trimmed-while-failing report and grow _retryStates without bound.
        PruneRetryStates(seenQueueIds);
    }

    private bool IsDue(string queueId, DateTimeOffset now)
    {
        lock (_retryGate)
        {
            return !_retryStates.TryGetValue(queueId, out var state) || state.NextAttemptAtUtc <= now;
        }
    }

    private void ScheduleRetry(string queueId, DateTimeOffset now)
    {
        lock (_retryGate)
        {
            _retryStates.TryGetValue(queueId, out var previous);
            var attempt = previous.Attempt + 1;
            var backoff = CalculateBackoff(attempt);
            _retryStates[queueId] = new RetryState(attempt, now + backoff);
        }
    }

    private void ClearRetry(string queueId)
    {
        lock (_retryGate)
        {
            _retryStates.Remove(queueId);
        }
    }

    private void PruneRetryStates(HashSet<string> pendingQueueIds)
    {
        lock (_retryGate)
        {
            if (_retryStates.Count == 0)
            {
                return;
            }

            List<string>? stale = null;
            foreach (var queueId in _retryStates.Keys)
            {
                if (!pendingQueueIds.Contains(queueId))
                {
                    (stale ??= new List<string>()).Add(queueId);
                }
            }

            if (stale is null)
            {
                return;
            }

            foreach (var queueId in stale)
            {
                _retryStates.Remove(queueId);
            }
        }
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        if (_options.UploadInitialBackoff == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        var ticks = _options.UploadInitialBackoff.Ticks * multiplier;
        if (ticks >= _options.UploadMaxBackoff.Ticks)
        {
            return _options.UploadMaxBackoff;
        }

        return TimeSpan.FromTicks((long)ticks);
    }

    private readonly record struct RetryState(int Attempt, DateTimeOffset NextAttemptAtUtc);
}
