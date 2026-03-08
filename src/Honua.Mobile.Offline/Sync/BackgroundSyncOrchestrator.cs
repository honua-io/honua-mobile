namespace Honua.Mobile.Offline.Sync;

public sealed class BackgroundSyncOrchestrator : IAsyncDisposable
{
    private readonly IOfflineSyncRunner _runner;
    private readonly IConnectivityStateProvider _connectivity;
    private readonly BackgroundSyncOrchestratorOptions _options;
    private readonly DeltaDownloadEngine? _downloadEngine;
    private readonly string? _downloadServiceId;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public BackgroundSyncOrchestrator(
        IOfflineSyncRunner runner,
        IConnectivityStateProvider connectivity,
        BackgroundSyncOrchestratorOptions? options = null,
        DeltaDownloadEngine? downloadEngine = null,
        string? downloadServiceId = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
        _options = options ?? new BackgroundSyncOrchestratorOptions();
        _downloadEngine = downloadEngine;
        _downloadServiceId = downloadServiceId;
    }

    public bool IsRunning => _loopTask is { IsCompleted: false };

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts is null || _loopTask is null)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            await _loopTask.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    public DeltaDownloadResult? LastDownloadResult { get; private set; }

    public async Task<SyncRunResult?> RunOnceIfOnlineAsync(CancellationToken ct = default)
    {
        if (!_connectivity.IsOnline)
        {
            return null;
        }

        await _runLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var uploadResult = await _runner.SyncAsync(ct).ConfigureAwait(false);

            if (_downloadEngine is not null && !string.IsNullOrWhiteSpace(_downloadServiceId))
            {
                LastDownloadResult = await _downloadEngine.DownloadAsync(_downloadServiceId, ct).ConfigureAwait(false);
            }

            return uploadResult;
        }
        finally
        {
            _runLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _runLock.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        if (_options.RunImmediately)
        {
            await ExecuteCycleAsync(ct).ConfigureAwait(false);
        }

        using var timer = new PeriodicTimer(_options.SyncInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            await ExecuteCycleAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task ExecuteCycleAsync(CancellationToken ct)
    {
        try
        {
            await RunOnceIfOnlineAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Keep the background loop alive and retry on the next interval.
        }
    }
}

public sealed class AlwaysOnlineConnectivityStateProvider : IConnectivityStateProvider
{
    public bool IsOnline => true;
}
