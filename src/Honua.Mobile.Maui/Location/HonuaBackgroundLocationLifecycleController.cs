using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Honua.Mobile.Maui.Location;

/// <summary>
/// Owns mobile lifecycle behavior for native background location sessions and OS geofence monitoring.
/// </summary>
public sealed class HonuaBackgroundLocationLifecycleController : IDisposable, IAsyncDisposable
{
    private readonly HonuaDeviceLocationCoordinator _locations;
    private readonly ILogger<HonuaBackgroundLocationLifecycleController> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IHonuaBackgroundLocationSession? _session;
    private HonuaBackgroundLocationLifecycleRequest? _requested;
    private IReadOnlyList<string> _activeRegionIds = [];
    private bool _batterySaverDeferralActive;
    private bool _disposed;
    private HonuaBackgroundLocationRuntimeState _state = HonuaBackgroundLocationRuntimeState.Stopped;

    public HonuaBackgroundLocationLifecycleController(
        HonuaDeviceLocationCoordinator locations,
        ILogger<HonuaBackgroundLocationLifecycleController>? logger = null)
    {
        _locations = locations ?? throw new ArgumentNullException(nameof(locations));
        _logger = logger ?? NullLogger<HonuaBackgroundLocationLifecycleController>.Instance;
    }

    /// <summary>
    /// Current runtime state for the native background location workflow.
    /// </summary>
    public HonuaBackgroundLocationRuntimeState State => _state;

    /// <summary>
    /// Starts background location updates, geofence monitoring, or both.
    /// </summary>
    public async ValueTask StartAsync(
        HonuaBackgroundLocationLifecycleRequest? request = null,
        CancellationToken ct = default)
    {
        request ??= new HonuaBackgroundLocationLifecycleRequest();
        request.Validate();

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _requested = request;

            if (_batterySaverDeferralActive && AllowsBatterySaverDeferral(request))
            {
                await StopActiveAsync(HonuaBackgroundLocationStopReason.BatterySaver, ct).ConfigureAwait(false);
                _state = HonuaBackgroundLocationRuntimeState.DeferredForBatterySaver;
                _logger.LogInformation("Deferring background location startup while battery saver is enabled.");
                return;
            }

            await StartActiveAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Applies a mobile lifecycle or power event to the active background location runtime.
    /// </summary>
    public async ValueTask HandleLifecycleEventAsync(
        HonuaLocationLifecycleEvent lifecycleEvent,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            switch (lifecycleEvent)
            {
                case HonuaLocationLifecycleEvent.EnteredForeground:
                case HonuaLocationLifecycleEvent.EnteredBackground:
                    _logger.LogDebug("Background location lifecycle event {LifecycleEvent} does not change the active session.", lifecycleEvent);
                    break;

                case HonuaLocationLifecycleEvent.BatterySaverEnabled:
                    await DeferForBatterySaverAsync(ct).ConfigureAwait(false);
                    break;

                case HonuaLocationLifecycleEvent.BatterySaverDisabled:
                    await ResumeAfterBatterySaverAsync(ct).ConfigureAwait(false);
                    break;

                case HonuaLocationLifecycleEvent.Suspended:
                    await StopActiveAsync(HonuaBackgroundLocationStopReason.LifecycleSuspended, ct).ConfigureAwait(false);
                    _requested = null;
                    _state = HonuaBackgroundLocationRuntimeState.Stopped;
                    break;

                case HonuaLocationLifecycleEvent.Shutdown:
                    await StopActiveAsync(HonuaBackgroundLocationStopReason.Shutdown, ct).ConfigureAwait(false);
                    _requested = null;
                    _batterySaverDeferralActive = false;
                    _state = HonuaBackgroundLocationRuntimeState.Stopped;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(lifecycleEvent), lifecycleEvent, null);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops any active background update session and geofence monitoring.
    /// </summary>
    public async ValueTask StopAsync(
        HonuaBackgroundLocationStopReason reason = HonuaBackgroundLocationStopReason.UserStopped,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StopActiveAsync(reason, ct).ConfigureAwait(false);
            _requested = null;
            _state = HonuaBackgroundLocationRuntimeState.Stopped;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopAsync(HonuaBackgroundLocationStopReason.Shutdown).AsTask().GetAwaiter().GetResult();
        _gate.Dispose();
        _disposed = true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(HonuaBackgroundLocationStopReason.Shutdown).ConfigureAwait(false);
        _gate.Dispose();
        _disposed = true;
    }

    private async ValueTask StartActiveAsync(
        HonuaBackgroundLocationLifecycleRequest request,
        CancellationToken ct)
    {
        await StopActiveAsync(HonuaBackgroundLocationStopReason.Restarting, ct).ConfigureAwait(false);

        IHonuaBackgroundLocationSession? startedSession = null;
        IReadOnlyList<string> startedRegionIds = [];

        try
        {
            if (request.BackgroundUpdates is not null)
            {
                startedSession = await _locations.StartBackgroundUpdatesAsync(request.BackgroundUpdates, ct).ConfigureAwait(false);
            }

            if (request.Geofences is not null)
            {
                await _locations.StartGeofencingAsync(request.Geofences, ct).ConfigureAwait(false);
                startedRegionIds = request.Geofences.Regions.Select(static region => region.Id).ToArray();
            }

            _session = startedSession;
            _activeRegionIds = startedRegionIds;
            _state = HonuaBackgroundLocationRuntimeState.Running;
        }
        catch
        {
            if (startedRegionIds.Count > 0)
            {
                await _locations.StopGeofencingAsync(startedRegionIds, ct).ConfigureAwait(false);
            }

            if (startedSession is not null)
            {
                await startedSession.DisposeAsync().ConfigureAwait(false);
            }

            _state = HonuaBackgroundLocationRuntimeState.Stopped;
            throw;
        }
    }

    private async ValueTask StopActiveAsync(
        HonuaBackgroundLocationStopReason reason,
        CancellationToken ct)
    {
        var session = _session;
        var activeRegionIds = _activeRegionIds;
        _session = null;
        _activeRegionIds = [];

        try
        {
            if (activeRegionIds.Count > 0)
            {
                await _locations.StopGeofencingAsync(activeRegionIds, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (reason != HonuaBackgroundLocationStopReason.Restarting)
        {
            _logger.LogInformation("Stopped background location runtime for reason {StopReason}.", reason);
        }
    }

    private async ValueTask DeferForBatterySaverAsync(CancellationToken ct)
    {
        _batterySaverDeferralActive = true;

        if (_requested is null || !AllowsBatterySaverDeferral(_requested))
        {
            return;
        }

        await StopActiveAsync(HonuaBackgroundLocationStopReason.BatterySaver, ct).ConfigureAwait(false);
        _state = HonuaBackgroundLocationRuntimeState.DeferredForBatterySaver;
    }

    private async ValueTask ResumeAfterBatterySaverAsync(CancellationToken ct)
    {
        _batterySaverDeferralActive = false;

        if (_state == HonuaBackgroundLocationRuntimeState.DeferredForBatterySaver && _requested is not null)
        {
            await StartActiveAsync(_requested, ct).ConfigureAwait(false);
        }
        else if (_state == HonuaBackgroundLocationRuntimeState.DeferredForBatterySaver)
        {
            _state = HonuaBackgroundLocationRuntimeState.Stopped;
        }
    }

    private static bool AllowsBatterySaverDeferral(HonuaBackgroundLocationLifecycleRequest request)
        => request.AllowBatterySaverDeferral
            && request.BackgroundUpdates?.AllowBatterySaverDeferral != false;
}
