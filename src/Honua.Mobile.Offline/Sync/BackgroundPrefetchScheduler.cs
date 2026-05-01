using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Runs bounded background prefetch work and cooperatively cancels it on mobile lifecycle events.
/// </summary>
public sealed class BackgroundPrefetchScheduler : IAsyncDisposable
{
    private readonly BackgroundPrefetchSchedulerOptions _options;
    private readonly ILogger<BackgroundPrefetchScheduler> _logger;
    private readonly SemaphoreSlim _concurrency;
    private readonly object _sync = new();
    private readonly List<Task> _runningTasks = [];
    private CancellationTokenSource _lifetimeCts = new();

    /// <summary>
    /// Initializes a new <see cref="BackgroundPrefetchScheduler"/>.
    /// </summary>
    /// <param name="options">Scheduler options. Defaults are used when omitted.</param>
    /// <param name="logger">Optional logger.</param>
    public BackgroundPrefetchScheduler(
        BackgroundPrefetchSchedulerOptions? options = null,
        ILogger<BackgroundPrefetchScheduler>? logger = null)
    {
        _options = options ?? new BackgroundPrefetchSchedulerOptions();
        _logger = logger ?? NullLogger<BackgroundPrefetchScheduler>.Instance;
        _concurrency = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
    }

    /// <summary>
    /// Number of prefetch tasks currently tracked by the scheduler.
    /// </summary>
    public int RunningCount
    {
        get
        {
            lock (_sync)
            {
                PruneCompletedTasks();
                return _runningTasks.Count;
            }
        }
    }

    /// <summary>
    /// Starts a background prefetch item.
    /// </summary>
    /// <param name="workItem">Work item to run.</param>
    /// <param name="ct">Cancellation token linked to this specific item.</param>
    /// <returns>The scheduled task.</returns>
    public Task ScheduleAsync(IBackgroundPrefetchWorkItem workItem, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        CancellationTokenSource linkedCts;
        lock (_sync)
        {
            PruneCompletedTasks();
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, ct);
        }

        var task = Task.Run(async () =>
        {
            using (linkedCts)
            {
                await RunWorkItemAsync(workItem, linkedCts.Token).ConfigureAwait(false);
            }
        }, CancellationToken.None);
        lock (_sync)
        {
            _runningTasks.Add(task);
        }

        return task;
    }

    /// <summary>
    /// Cancels in-flight prefetch work because the host app received a lifecycle pressure event.
    /// </summary>
    /// <param name="reason">Lifecycle reason for cancellation.</param>
    /// <param name="ct">Cancellation token used while waiting for work to observe cancellation.</param>
    public async Task CancelForLifecycleEventAsync(PrefetchLifecycleEvent reason, CancellationToken ct = default)
    {
        Task[] tasks;
        lock (_sync)
        {
            _logger.LogInformation("Cancelling mobile prefetch work because of lifecycle event {LifecycleEvent}.", reason);
            _lifetimeCts.Cancel();
            tasks = _runningTasks.ToArray();
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Individual task errors are logged in RunWorkItemAsync; cancellation should not fail lifecycle handling.
        }
        finally
        {
            lock (_sync)
            {
                _lifetimeCts.Dispose();
                _lifetimeCts = new CancellationTokenSource();
                PruneCompletedTasks();
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CancelForLifecycleEventAsync(PrefetchLifecycleEvent.Shutdown).ConfigureAwait(false);
        _lifetimeCts.Dispose();
        _concurrency.Dispose();
    }

    private async Task RunWorkItemAsync(IBackgroundPrefetchWorkItem workItem, CancellationToken ct)
    {
        try
        {
            await _concurrency.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await workItem.ExecuteAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _concurrency.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cooperative lifecycle cancellation.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background prefetch item {PrefetchItemId} failed.", workItem.ItemId);
        }
    }

    private void PruneCompletedTasks()
        => _runningTasks.RemoveAll(static task => task.IsCompleted);
}
