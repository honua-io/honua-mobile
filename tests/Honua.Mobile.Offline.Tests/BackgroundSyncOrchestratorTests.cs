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
        await Task.Delay(120);
        await orchestrator.StopAsync();

        Assert.True(runner.CallCount >= 1);
    }

    private sealed class FakeSyncRunner : IOfflineSyncRunner
    {
        public int CallCount { get; private set; }

        public Task<SyncRunResult> SyncAsync(CancellationToken ct = default)
        {
            CallCount++;
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
}
