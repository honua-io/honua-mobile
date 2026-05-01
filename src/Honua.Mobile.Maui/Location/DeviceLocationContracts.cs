using Honua.Mobile.Maui.Annotations;

namespace Honua.Mobile.Maui.Location;

/// <summary>
/// Permission scope needed for a mobile location workflow.
/// </summary>
public enum HonuaLocationAccess
{
    Foreground,
    Background,
}

/// <summary>
/// Current location permission state reported by the platform.
/// </summary>
public enum HonuaLocationPermissionStatus
{
    Unknown,
    Denied,
    Foreground,
    Background,
}

/// <summary>
/// Requested device location precision.
/// </summary>
public enum HonuaLocationAccuracy
{
    Reduced,
    Balanced,
    High,
    Best,
}

/// <summary>
/// Device location fix acquired from a native platform provider.
/// </summary>
public sealed record HonuaDeviceLocation
{
    public required HonuaMapCoordinate Coordinate { get; init; }

    public double? AccuracyMeters { get; init; }

    public double? AltitudeMeters { get; init; }

    public double? SpeedMetersPerSecond { get; init; }

    public double? HeadingDegrees { get; init; }

    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool IsBackground { get; init; }

    public string? Provider { get; init; }

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    public void Validate()
    {
        if (AccuracyMeters is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AccuracyMeters), "Location accuracy must be non-negative.");
        }

        if (SpeedMetersPerSecond is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedMetersPerSecond), "Location speed must be non-negative.");
        }

        if (HeadingDegrees is < 0 or >= 360)
        {
            throw new ArgumentOutOfRangeException(nameof(HeadingDegrees), "Location heading must be in [0, 360).");
        }
    }
}

/// <summary>
/// Options for a one-shot current-location acquisition.
/// </summary>
public sealed record HonuaDeviceLocationRequest
{
    public HonuaLocationAccess RequiredAccess { get; init; } = HonuaLocationAccess.Foreground;

    public HonuaLocationAccuracy Accuracy { get; init; } = HonuaLocationAccuracy.Balanced;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan? MaxCacheAge { get; init; } = TimeSpan.FromMinutes(2);

    public bool AllowReducedAccuracy { get; init; } = true;

    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Location timeout must be positive.");
        }

        if (MaxCacheAge.HasValue && MaxCacheAge.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCacheAge), "Location cache age must be positive.");
        }
    }
}

/// <summary>
/// Options for continuous background location acquisition.
/// </summary>
public sealed record HonuaBackgroundLocationOptions
{
    public HonuaLocationAccuracy Accuracy { get; init; } = HonuaLocationAccuracy.Balanced;

    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromMinutes(5);

    public double MinimumDistanceMeters { get; init; } = 25;

    public bool AllowBatterySaverDeferral { get; init; } = true;

    public string Purpose { get; init; } = "Honua background location";

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    public void Validate()
    {
        if (MinimumInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumInterval), "Background location interval must be positive.");
        }

        if (MinimumDistanceMeters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumDistanceMeters), "Background location distance must be non-negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(Purpose);
    }
}

/// <summary>
/// Region delegated to native OS geofencing facilities.
/// </summary>
public sealed record HonuaGeofenceRegion
{
    public required string Id { get; init; }

    public required HonuaMapCoordinate Center { get; init; }

    public required double RadiusMeters { get; init; }

    public bool NotifyOnEntry { get; init; } = true;

    public bool NotifyOnExit { get; init; } = true;

    public bool NotifyOnDwell { get; init; }

    public TimeSpan? DwellTime { get; init; }

    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);

        if (RadiusMeters <= 0 || double.IsNaN(RadiusMeters) || double.IsInfinity(RadiusMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(RadiusMeters), "Geofence radius must be finite and positive.");
        }

        if (!NotifyOnEntry && !NotifyOnExit && !NotifyOnDwell)
        {
            throw new ArgumentException("A geofence region must notify on entry, exit, or dwell.", nameof(HonuaGeofenceRegion));
        }

        if (DwellTime.HasValue && DwellTime.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DwellTime), "Geofence dwell time must be positive.");
        }
    }
}

/// <summary>
/// Options for starting native OS geofence monitoring.
/// </summary>
public sealed record HonuaGeofenceMonitoringRequest
{
    public IReadOnlyList<HonuaGeofenceRegion> Regions { get; init; } = [];

    public HonuaLocationAccess RequiredAccess { get; init; } = HonuaLocationAccess.Background;

    public bool ReplaceExisting { get; init; } = true;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Regions);

        if (Regions.Count == 0)
        {
            throw new ArgumentException("At least one geofence region is required.", nameof(Regions));
        }

        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in Regions)
        {
            ArgumentNullException.ThrowIfNull(region);
            region.Validate();

            if (!regionIds.Add(region.Id))
            {
                throw new InvalidOperationException($"Geofence region '{region.Id}' is defined more than once.");
            }
        }
    }
}

/// <summary>
/// Native geofence transition kind.
/// </summary>
public enum HonuaGeofenceTransitionKind
{
    Enter,
    Exit,
    Dwell,
}

/// <summary>
/// Native geofence transition emitted by a platform monitor.
/// </summary>
public sealed record HonuaGeofenceTransition
{
    public required string RegionId { get; init; }

    public required HonuaGeofenceTransitionKind Kind { get; init; }

    public HonuaDeviceLocation? Location { get; init; }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Platform permission adapter for foreground and background location access.
/// </summary>
public interface IHonuaDeviceLocationPermissionService
{
    ValueTask<HonuaLocationPermissionStatus> CheckPermissionAsync(
        HonuaLocationAccess access,
        CancellationToken ct = default);

    ValueTask<HonuaLocationPermissionStatus> RequestPermissionAsync(
        HonuaLocationAccess access,
        CancellationToken ct = default);
}

/// <summary>
/// Platform adapter for one-shot device location acquisition.
/// </summary>
public interface IHonuaDeviceLocationProvider
{
    ValueTask<HonuaDeviceLocation?> GetCurrentLocationAsync(
        HonuaDeviceLocationRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Active native background location session.
/// </summary>
public interface IHonuaBackgroundLocationSession : IAsyncDisposable
{
    string SessionId { get; }
}

/// <summary>
/// Platform adapter for continuous background location acquisition.
/// </summary>
public interface IHonuaBackgroundLocationProvider
{
    ValueTask<IHonuaBackgroundLocationSession> StartUpdatesAsync(
        HonuaBackgroundLocationOptions options,
        CancellationToken ct = default);
}

/// <summary>
/// Platform adapter for OS-backed geofence monitoring.
/// </summary>
public interface IHonuaGeofenceMonitor
{
    event EventHandler<HonuaGeofenceTransition>? Transitioned;

    ValueTask StartMonitoringAsync(
        HonuaGeofenceMonitoringRequest request,
        CancellationToken ct = default);

    ValueTask StopMonitoringAsync(
        IReadOnlyList<string> regionIds,
        CancellationToken ct = default);
}
