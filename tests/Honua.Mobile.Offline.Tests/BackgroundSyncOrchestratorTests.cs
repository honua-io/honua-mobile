using Honua.Mobile.Offline.Sync;

namespace Honua.Mobile.Offline.Tests;

public sealed class BackgroundSyncOrchestratorTests
{
    [Fact]
    public async Task RunOnceIfOnlineAsync_WhenOffline_DoesNotSync()
    {
        var runner = new FakeSyncRunner();
        var connectivity = new ToggleConnectivityProvider { IsOnline = false };
        await using var orchestrator = new BackgroundSyncOrchestrator(runner, connectivity);

        var result = await orchestrator.RunOnceIfOnlineAsync();

        Assert.Null(result);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task StartAsync_WhenOnline_RunsBackgroundSync()
    {
        var runner = new FakeSyncRunner();
        var connectivity = new ToggleConnectivityProvider { IsOnline = true };
        await using var orchestrator = new BackgroundSyncOrchestrator(
            runner,
            connectivity,
            new BackgroundSyncOrchestratorOptions
            {
                SyncInterval = TimeSpan.FromMilliseconds(30),
                RunImmediately = true,
            });

        await orchestrator.StartAsync();
        await runner.FirstRun.WaitAsync(TimeSpan.FromSeconds(5));
        await orchestrator.StopAsync();

        Assert.True(runner.CallCount >= 1);
    }

    [Fact]
    public async Task StartAsync_WhenSyncThrows_KeepsLoopRunning()
    {
        var runner = new FlakySyncRunner(initialFailures: 1);
        var connectivity = new ToggleConnectivityProvider { IsOnline = true };
        await using var orchestrator = new BackgroundSyncOrchestrator(
            runner,
            connectivity,
            new BackgroundSyncOrchestratorOptions
            {
                SyncInterval = TimeSpan.FromMilliseconds(25),
                RunImmediately = true,
            });

        await orchestrator.StartAsync();
        await runner.FirstSuccess.WaitAsync(TimeSpan.FromSeconds(5));
        await orchestrator.StopAsync();

        Assert.True(runner.CallCount >= 2);
        Assert.True(runner.SuccessCount >= 1);
    }

    private sealed class FakeSyncRunner : IOfflineSyncRunner
    {
        private int _callCount;

        public Task FirstRun => _firstRun.Task;

        public int CallCount => Volatile.Read(ref _callCount);

        private readonly TaskCompletionSource _firstRun = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SyncRunResult> SyncAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);
            _firstRun.TrySetResult();

            return Task.FromResult(new SyncRunResult
            {
                Loaded = 0,
                Succeeded = 0,
                Failed = 0,
            });
        }
    }

    private sealed class ToggleConnectivityProvider : IConnectivityStateProvider
    {
        public bool IsOnline { get; set; }
    }

    private sealed class FlakySyncRunner : IOfflineSyncRunner
    {
        private int _remainingFailures;
        private int _callCount;
        private int _successCount;

        public FlakySyncRunner(int initialFailures)
        {
            _remainingFailures = initialFailures;
        }

        public Task FirstSuccess => _firstSuccess.Task;

        public int CallCount => Volatile.Read(ref _callCount);

        public int SuccessCount => Volatile.Read(ref _successCount);

        private readonly TaskCompletionSource _firstSuccess = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<SyncRunResult> SyncAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _callCount);

            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                throw new InvalidOperationException("transient sync error");
            }

            Interlocked.Increment(ref _successCount);
            _firstSuccess.TrySetResult();

            return Task.FromResult(new SyncRunResult
            {
                Loaded = 0,
                Succeeded = 0,
                Failed = 0,
            });
        }
    }
}
