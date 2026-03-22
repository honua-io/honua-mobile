using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Runs periodic sync cycles in the background, uploading pending offline edits and optionally
/// downloading delta changes when the device is online.
/// </summary>
public sealed class BackgroundSyncOrchestrator : IAsyncDisposable
{
    private readonly IOfflineSyncRunner _runner;
    private readonly IConnectivityStateProvider _connectivity;
    private readonly BackgroundSyncOrchestratorOptions _options;
    private readonly DeltaDownloadEngine? _downloadEngine;
    private readonly string? _downloadServiceId;
    private readonly ILogger<BackgroundSyncOrchestrator> _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>
    /// Raised when a sync cycle fails with an unhandled exception. The background loop continues running.
    /// </summary>
    public event Action<Exception>? SyncFailed;

    /// <summary>
    /// Initializes a new <see cref="BackgroundSyncOrchestrator"/>.
    /// </summary>
    /// <param name="runner">The sync runner that processes pending operations.</param>
    /// <param name="connectivity">Provider that reports current network state.</param>
    /// <param name="options">Orchestrator options; defaults are used when <see langword="null"/>.</param>
    /// <param name="downloadEngine">Optional delta download engine for bi-directional sync.</param>
    /// <param name="downloadServiceId">Service ID passed to the download engine.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="runner"/> or <paramref name="connectivity"/> is <see langword="null"/>.</exception>
    public BackgroundSyncOrchestrator(
        IOfflineSyncRunner runner,
        IConnectivityStateProvider connectivity,
        BackgroundSyncOrchestratorOptions? options = null,
        DeltaDownloadEngine? downloadEngine = null,
        string? downloadServiceId = null,
        ILogger<BackgroundSyncOrchestrator>? logger = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
        _options = options ?? new BackgroundSyncOrchestratorOptions();
        _downloadEngine = downloadEngine;
        _downloadServiceId = downloadServiceId;
        _logger = logger ?? NullLogger<BackgroundSyncOrchestrator>.Instance;
    }

    /// <summary>
    /// <see langword="true"/> when the background sync loop is actively running.
    /// </summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>
    /// Starts the periodic background sync loop. Does nothing if already running.
    /// </summary>
    /// <param name="ct">Cancellation token linked to the loop's lifetime.</param>
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

    /// <summary>
    /// Signals the background loop to stop and waits for it to complete.
    /// </summary>
    /// <param name="ct">Cancellation token to bound the wait time.</param>
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

    /// <summary>
    /// The result of the most recent delta download, or <see langword="null"/> if none has run.
    /// </summary>
    public DeltaDownloadResult? LastDownloadResult { get; private set; }

    /// <summary>
    /// Runs a single upload-then-download cycle if the device is online.
    /// Returns <see langword="null"/> without doing work when offline.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sync result, or <see langword="null"/> if offline.</returns>
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

    /// <inheritdoc />
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
        catch (Exception ex)
        {
            // Keep the background loop alive and retry on the next interval.
            _logger.LogError(ex, "Background sync cycle failed.");
            SyncFailed?.Invoke(ex);
        }
    }
}

/// <summary>
/// A connectivity provider that always reports online. Useful for testing or always-connected scenarios.
/// </summary>
public sealed class AlwaysOnlineConnectivityStateProvider : IConnectivityStateProvider
{
    /// <inheritdoc />
    public bool IsOnline => true;
}
