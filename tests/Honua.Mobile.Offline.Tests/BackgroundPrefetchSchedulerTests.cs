using Honua.Mobile.Offline.Sync;

namespace Honua.Mobile.Offline.Tests;

public sealed class BackgroundPrefetchSchedulerTests
{
    [Fact]
    public async Task CancelForLifecycleEventAsync_CancelsRunningPrefetchWork()
    {
        await using var scheduler = new BackgroundPrefetchScheduler(new BackgroundPrefetchSchedulerOptions
        {
            MaxConcurrency = 1,
        });
        var workItem = new CancellableWorkItem();

        var task = scheduler.ScheduleAsync(workItem);
        await workItem.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await scheduler.CancelForLifecycleEventAsync(PrefetchLifecycleEvent.LowMemory);
        await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(workItem.WasCancelled);
        Assert.Equal(0, scheduler.RunningCount);
    }

    private sealed class CancellableWorkItem : IBackgroundPrefetchWorkItem
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCancelled { get; private set; }

        public string ItemId => "test-prefetch";

        public async Task ExecuteAsync(CancellationToken ct = default)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                WasCancelled = true;
                throw;
            }
        }
    }
}
