using Honua.Mobile.Maui;
using Honua.Mobile.Maui.Annotations;
using Honua.Mobile.Maui.Location;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui.Tests;

public sealed class HonuaDeviceLocationTests
{
    [Fact]
    public async Task AcquireCurrentLocationAsync_RequestsForegroundPermissionBeforeProviderCall()
    {
        var permissions = new RecordingPermissionService
        {
            CheckStatus = HonuaLocationPermissionStatus.Denied,
            RequestStatus = HonuaLocationPermissionStatus.Foreground,
        };
        var provider = new RecordingLocationProvider
        {
            Location = new HonuaDeviceLocation
            {
                Coordinate = new HonuaMapCoordinate(21.3069, -157.8583),
                AccuracyMeters = 4,
                Provider = "test",
            },
        };
        var coordinator = new HonuaDeviceLocationCoordinator(permissions, provider);

        var location = await coordinator.AcquireCurrentLocationAsync(new HonuaDeviceLocationRequest
        {
            Accuracy = HonuaLocationAccuracy.High,
            RequiredAccess = HonuaLocationAccess.Foreground,
        });

        Assert.Equal(new HonuaMapCoordinate(21.3069, -157.8583), location.Coordinate);
        Assert.Equal([HonuaLocationAccess.Foreground], permissions.CheckedAccesses);
        Assert.Equal([HonuaLocationAccess.Foreground], permissions.RequestedAccesses);
        Assert.Equal(HonuaLocationAccuracy.High, provider.Requests.Single().Accuracy);
    }

    [Fact]
    public async Task StartBackgroundUpdatesAsync_RequiresBackgroundPermission()
    {
        var permissions = new RecordingPermissionService
        {
            CheckStatus = HonuaLocationPermissionStatus.Foreground,
            RequestStatus = HonuaLocationPermissionStatus.Background,
        };
        var backgroundProvider = new RecordingBackgroundLocationProvider();
        var coordinator = new HonuaDeviceLocationCoordinator(
            permissions,
            new RecordingLocationProvider(),
            backgroundProvider);

        var session = await coordinator.StartBackgroundUpdatesAsync(new HonuaBackgroundLocationOptions
        {
            MinimumInterval = TimeSpan.FromMinutes(10),
            MinimumDistanceMeters = 50,
            Purpose = "crew route replay",
        });

        Assert.Equal("session-1", session.SessionId);
        Assert.Equal([HonuaLocationAccess.Background], permissions.CheckedAccesses);
        Assert.Equal([HonuaLocationAccess.Background], permissions.RequestedAccesses);
        Assert.Equal(TimeSpan.FromMinutes(10), backgroundProvider.Options.Single().MinimumInterval);
    }

    [Fact]
    public async Task StartGeofencingAsync_DelegatesValidatedRegionsToMonitor()
    {
        var permissions = new RecordingPermissionService
        {
            CheckStatus = HonuaLocationPermissionStatus.Background,
        };
        var monitor = new RecordingGeofenceMonitor();
        var coordinator = new HonuaDeviceLocationCoordinator(
            permissions,
            new RecordingLocationProvider(),
            geofenceMonitor: monitor);
        var request = new HonuaGeofenceMonitoringRequest
        {
            Regions =
            [
                new HonuaGeofenceRegion
                {
                    Id = "job-site",
                    Center = new HonuaMapCoordinate(21.3069, -157.8583),
                    RadiusMeters = 100,
                    NotifyOnDwell = true,
                    DwellTime = TimeSpan.FromMinutes(2),
                },
            ],
        };

        await coordinator.StartGeofencingAsync(request);

        Assert.Same(request, monitor.Requests.Single());
        Assert.Equal([HonuaLocationAccess.Background], permissions.CheckedAccesses);
        Assert.Empty(permissions.RequestedAccesses);
    }

    [Fact]
    public void AddHonuaDeviceLocation_RegistersCoordinatorWithOptionalProviders()
    {
        using var provider = new ServiceCollection()
            .AddSingleton<IHonuaDeviceLocationPermissionService, RecordingPermissionService>()
            .AddSingleton<IHonuaDeviceLocationProvider, RecordingLocationProvider>()
            .AddSingleton<IHonuaBackgroundLocationProvider, RecordingBackgroundLocationProvider>()
            .AddSingleton<IHonuaGeofenceMonitor, RecordingGeofenceMonitor>()
            .AddHonuaDeviceLocation()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<HonuaDeviceLocationCoordinator>());
    }

    [Fact]
    public async Task AcquireCurrentLocationAsync_WhenPermissionDenied_Throws()
    {
        var coordinator = new HonuaDeviceLocationCoordinator(
            new RecordingPermissionService
            {
                CheckStatus = HonuaLocationPermissionStatus.Denied,
                RequestStatus = HonuaLocationPermissionStatus.Denied,
            },
            new RecordingLocationProvider());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await coordinator.AcquireCurrentLocationAsync());
    }

    private sealed class RecordingPermissionService : IHonuaDeviceLocationPermissionService
    {
        public HonuaLocationPermissionStatus CheckStatus { get; init; } = HonuaLocationPermissionStatus.Foreground;

        public HonuaLocationPermissionStatus RequestStatus { get; init; } = HonuaLocationPermissionStatus.Foreground;

        public List<HonuaLocationAccess> CheckedAccesses { get; } = [];

        public List<HonuaLocationAccess> RequestedAccesses { get; } = [];

        public ValueTask<HonuaLocationPermissionStatus> CheckPermissionAsync(
            HonuaLocationAccess access,
            CancellationToken ct = default)
        {
            CheckedAccesses.Add(access);
            return ValueTask.FromResult(CheckStatus);
        }

        public ValueTask<HonuaLocationPermissionStatus> RequestPermissionAsync(
            HonuaLocationAccess access,
            CancellationToken ct = default)
        {
            RequestedAccesses.Add(access);
            return ValueTask.FromResult(RequestStatus);
        }
    }

    private sealed class RecordingLocationProvider : IHonuaDeviceLocationProvider
    {
        public HonuaDeviceLocation? Location { get; init; } = new()
        {
            Coordinate = new HonuaMapCoordinate(21.3069, -157.8583),
        };

        public List<HonuaDeviceLocationRequest> Requests { get; } = [];

        public ValueTask<HonuaDeviceLocation?> GetCurrentLocationAsync(
            HonuaDeviceLocationRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(Location);
        }
    }

    private sealed class RecordingBackgroundLocationProvider : IHonuaBackgroundLocationProvider
    {
        public List<HonuaBackgroundLocationOptions> Options { get; } = [];

        public ValueTask<IHonuaBackgroundLocationSession> StartUpdatesAsync(
            HonuaBackgroundLocationOptions options,
            CancellationToken ct = default)
        {
            Options.Add(options);
            return ValueTask.FromResult<IHonuaBackgroundLocationSession>(new RecordingSession("session-1"));
        }
    }

    private sealed class RecordingSession : IHonuaBackgroundLocationSession
    {
        public RecordingSession(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingGeofenceMonitor : IHonuaGeofenceMonitor
    {
        public event EventHandler<HonuaGeofenceTransition>? Transitioned
        {
            add { }
            remove { }
        }

        public List<HonuaGeofenceMonitoringRequest> Requests { get; } = [];

        public ValueTask StartMonitoringAsync(
            HonuaGeofenceMonitoringRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return ValueTask.CompletedTask;
        }

        public ValueTask StopMonitoringAsync(
            IReadOnlyList<string> regionIds,
            CancellationToken ct = default)
            => ValueTask.CompletedTask;
    }
}
