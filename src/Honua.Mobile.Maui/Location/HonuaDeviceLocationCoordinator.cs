namespace Honua.Mobile.Maui.Location;

/// <summary>
/// Coordinates permission checks with foreground, background, and geofence location adapters.
/// </summary>
public sealed class HonuaDeviceLocationCoordinator
{
    private readonly IHonuaDeviceLocationPermissionService _permissions;
    private readonly IHonuaDeviceLocationProvider _locationProvider;
    private readonly IHonuaBackgroundLocationProvider? _backgroundProvider;
    private readonly IHonuaGeofenceMonitor? _geofenceMonitor;

    public HonuaDeviceLocationCoordinator(
        IHonuaDeviceLocationPermissionService permissions,
        IHonuaDeviceLocationProvider locationProvider,
        IHonuaBackgroundLocationProvider? backgroundProvider = null,
        IHonuaGeofenceMonitor? geofenceMonitor = null)
    {
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _locationProvider = locationProvider ?? throw new ArgumentNullException(nameof(locationProvider));
        _backgroundProvider = backgroundProvider;
        _geofenceMonitor = geofenceMonitor;
    }

    /// <summary>
    /// Acquires a single device location after ensuring the requested permission scope is granted.
    /// </summary>
    /// <param name="request">Acquisition options; defaults are used when <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current location fix.</returns>
    public async ValueTask<HonuaDeviceLocation> AcquireCurrentLocationAsync(
        HonuaDeviceLocationRequest? request = null,
        CancellationToken ct = default)
    {
        request ??= new HonuaDeviceLocationRequest();
        request.Validate();

        await EnsurePermissionAsync(request.RequiredAccess, ct).ConfigureAwait(false);

        var location = await _locationProvider.GetCurrentLocationAsync(request, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The device location provider did not return a location fix.");

        location.Validate();
        return location;
    }

    /// <summary>
    /// Starts a native background location session after ensuring background permission is granted.
    /// </summary>
    /// <param name="options">Background acquisition options; defaults are used when <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active background location session.</returns>
    public async ValueTask<IHonuaBackgroundLocationSession> StartBackgroundUpdatesAsync(
        HonuaBackgroundLocationOptions? options = null,
        CancellationToken ct = default)
    {
        if (_backgroundProvider is null)
        {
            throw new InvalidOperationException("No background location provider is registered.");
        }

        options ??= new HonuaBackgroundLocationOptions();
        options.Validate();

        await EnsurePermissionAsync(HonuaLocationAccess.Background, ct).ConfigureAwait(false);
        return await _backgroundProvider.StartUpdatesAsync(options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts native OS geofence monitoring after ensuring the request's permission scope is granted.
    /// </summary>
    /// <param name="request">Geofence monitoring request.</param>
    /// <param name="ct">Cancellation token.</param>
    public async ValueTask StartGeofencingAsync(
        HonuaGeofenceMonitoringRequest request,
        CancellationToken ct = default)
    {
        if (_geofenceMonitor is null)
        {
            throw new InvalidOperationException("No geofence monitor is registered.");
        }

        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        await EnsurePermissionAsync(request.RequiredAccess, ct).ConfigureAwait(false);
        await _geofenceMonitor.StartMonitoringAsync(request, ct).ConfigureAwait(false);
    }

    public static bool PermissionAllows(
        HonuaLocationPermissionStatus status,
        HonuaLocationAccess access)
    {
        return access switch
        {
            HonuaLocationAccess.Foreground => status is HonuaLocationPermissionStatus.Foreground
                or HonuaLocationPermissionStatus.Background,
            HonuaLocationAccess.Background => status is HonuaLocationPermissionStatus.Background,
            _ => false,
        };
    }

    private async ValueTask EnsurePermissionAsync(HonuaLocationAccess access, CancellationToken ct)
    {
        var status = await _permissions.CheckPermissionAsync(access, ct).ConfigureAwait(false);
        if (!PermissionAllows(status, access))
        {
            status = await _permissions.RequestPermissionAsync(access, ct).ConfigureAwait(false);
        }

        if (!PermissionAllows(status, access))
        {
            throw new UnauthorizedAccessException($"Location permission '{access}' was not granted.");
        }
    }
}
