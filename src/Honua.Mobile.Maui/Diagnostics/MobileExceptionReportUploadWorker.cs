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
        await foreach (var queued in _queue.ReadPendingAsync(cancellationToken))
        {
            if (attempted >= _options.MaxUploadBatchSize)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

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
