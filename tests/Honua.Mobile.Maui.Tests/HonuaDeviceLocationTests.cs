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
    public async Task StopGeofencingAsync_DelegatesRegionIdsToMonitor()
    {
        var monitor = new RecordingGeofenceMonitor();
        var coordinator = new HonuaDeviceLocationCoordinator(
            new RecordingPermissionService(),
            new RecordingLocationProvider(),
            geofenceMonitor: monitor);

        await coordinator.StopGeofencingAsync(["job-site", "yard"]);

        Assert.Equal(["job-site", "yard"], monitor.StoppedRegionIds.Single());
    }

    [Theory]
    [MemberData(nameof(NativeGeofenceTransitionFixture))]
    public void GeofenceTransitioned_ForwardsNativeEnterExitAndProximityEvents(HonuaGeofenceTransitionKind kind)
    {
        var monitor = new RecordingGeofenceMonitor();
        var coordinator = new HonuaDeviceLocationCoordinator(
            new RecordingPermissionService(),
            new RecordingLocationProvider(),
            geofenceMonitor: monitor);
        var forwarded = new List<HonuaGeofenceTransition>();

        coordinator.GeofenceTransitioned += (_, transition) => forwarded.Add(transition);

        monitor.Emit(new HonuaGeofenceTransition
        {
            RegionId = "job-site",
            Kind = kind,
            Location = new HonuaDeviceLocation
            {
                Coordinate = new HonuaMapCoordinate(21.3069, -157.8583),
                AccuracyMeters = 6,
                IsBackground = true,
            },
            OccurredAt = DateTimeOffset.Parse("2026-04-28T19:46:34Z"),
        });

        var transition = Assert.Single(forwarded);
        Assert.Equal(kind, transition.Kind);
        Assert.Equal("job-site", transition.RegionId);
        Assert.True(transition.Location?.IsBackground);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task StartGeofencingAsync_RejectsNonFiniteRadius(double radiusMeters)
    {
        var monitor = new RecordingGeofenceMonitor();
        var coordinator = new HonuaDeviceLocationCoordinator(
            new RecordingPermissionService(),
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
                    RadiusMeters = radiusMeters,
                },
            ],
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await coordinator.StartGeofencingAsync(request));
        Assert.Empty(monitor.Requests);
    }

    [Fact]
    public async Task BackgroundLocationLifecycleController_StartAsync_StartsUpdatesAndGeofences()
    {
        var permissions = new RecordingPermissionService
        {
            CheckStatus = HonuaLocationPermissionStatus.Background,
        };
        var backgroundProvider = new RecordingBackgroundLocationProvider();
        var monitor = new RecordingGeofenceMonitor();
        var coordinator = new HonuaDeviceLocationCoordinator(
            permissions,
            new RecordingLocationProvider(),
            backgroundProvider,
            monitor);
        var controller = new HonuaBackgroundLocationLifecycleController(coordinator);
        var geofenceRequest = CreateGeofenceRequest();

        await controller.StartAsync(new HonuaBackgroundLocationLifecycleRequest
        {
            BackgroundUpdates = new HonuaBackgroundLocationOptions
            {
                MinimumInterval = TimeSpan.FromMinutes(15),
                MinimumDistanceMeters = 75,
                Purpose = "field crew route continuity",
            },
            Geofences = geofenceRequest,
        });

        Assert.Equal(HonuaBackgroundLocationRuntimeState.Running, controller.State);
        Assert.Equal(TimeSpan.FromMinutes(15), backgroundProvider.Options.Single().MinimumInterval);
        Assert.Same(geofenceRequest, monitor.Requests.Single());
    }

    [Fact]
    public async Task BackgroundLocationLifecycleController_BatterySaverEnabled_DefersAndRestarts()
    {
        var backgroundProvider = new RecordingBackgroundLocationProvider();
        var monitor = new RecordingGeofenceMonitor();
        var coordinator = new HonuaDeviceLocationCoordinator(
            new RecordingPermissionService
            {
                CheckStatus = HonuaLocationPermissionStatus.Background,
            },
            new RecordingLocationProvider(),
            backgroundProvider,
            monitor);
        var controller = new HonuaBackgroundLocationLifecycleController(coordinator);

        await controller.StartAsync(new HonuaBackgroundLocationLifecycleRequest
        {
            BackgroundUpdates = new HonuaBackgroundLocationOptions
            {
                AllowBatterySaverDeferral = true,
            },
            Geofences = CreateGeofenceRequest(),
        });
        var firstSession = backgroundProvider.Sessions.Single();

        await controller.HandleLifecycleEventAsync(HonuaLocationLifecycleEvent.BatterySaverEnabled);

        Assert.Equal(HonuaBackgroundLocationRuntimeState.DeferredForBatterySaver, controller.State);
        Assert.True(firstSession.IsDisposed);
        Assert.Equal(["job-site"], monitor.StoppedRegionIds.Single());

        await controller.HandleLifecycleEventAsync(HonuaLocationLifecycleEvent.BatterySaverDisabled);

        Assert.Equal(HonuaBackgroundLocationRuntimeState.Running, controller.State);
        Assert.Equal(2, backgroundProvider.Options.Count);
        Assert.Equal(2, monitor.Requests.Count);
        Assert.False(backgroundProvider.Sessions.Last().IsDisposed);
    }

    [Fact]
    public async Task BackgroundLocationLifecycleController_Suspended_StopsAndClearsRequestedRuntime()
    {
        var backgroundProvider = new RecordingBackgroundLocationProvider();
        var monitor = new RecordingGeofenceMonitor();
        var coordinator = new HonuaDeviceLocationCoordinator(
            new RecordingPermissionService
            {
                CheckStatus = HonuaLocationPermissionStatus.Background,
            },
            new RecordingLocationProvider(),
            backgroundProvider,
            monitor);
        var controller = new HonuaBackgroundLocationLifecycleController(coordinator);

        await controller.StartAsync(new HonuaBackgroundLocationLifecycleRequest
        {
            Geofences = CreateGeofenceRequest(),
        });

        await controller.HandleLifecycleEventAsync(HonuaLocationLifecycleEvent.Suspended);
        await controller.HandleLifecycleEventAsync(HonuaLocationLifecycleEvent.EnteredForeground);

        Assert.Equal(HonuaBackgroundLocationRuntimeState.Stopped, controller.State);
        Assert.True(backgroundProvider.Sessions.Single().IsDisposed);
        Assert.Equal(["job-site"], monitor.StoppedRegionIds.Single());
        Assert.Single(backgroundProvider.Options);
        Assert.Single(monitor.Requests);
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
        Assert.NotNull(provider.GetRequiredService<HonuaBackgroundLocationLifecycleController>());
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

    public static TheoryData<HonuaGeofenceTransitionKind> NativeGeofenceTransitionFixture()
        => new()
        {
            HonuaGeofenceTransitionKind.Enter,
            HonuaGeofenceTransitionKind.Exit,
            HonuaGeofenceTransitionKind.Proximity,
        };

    private static HonuaGeofenceMonitoringRequest CreateGeofenceRequest()
        => new()
        {
            Regions =
            [
                new HonuaGeofenceRegion
                {
                    Id = "job-site",
                    Center = new HonuaMapCoordinate(21.3069, -157.8583),
                    RadiusMeters = 100,
                    NotifyOnEntry = true,
                    NotifyOnExit = true,
                },
            ],
        };

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

        public List<RecordingSession> Sessions { get; } = [];

        public ValueTask<IHonuaBackgroundLocationSession> StartUpdatesAsync(
            HonuaBackgroundLocationOptions options,
            CancellationToken ct = default)
        {
            Options.Add(options);
            var session = new RecordingSession($"session-{Sessions.Count + 1}");
            Sessions.Add(session);
            return ValueTask.FromResult<IHonuaBackgroundLocationSession>(session);
        }
    }

    private sealed class RecordingSession : IHonuaBackgroundLocationSession
    {
        public RecordingSession(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingGeofenceMonitor : IHonuaGeofenceMonitor
    {
        public event EventHandler<HonuaGeofenceTransition>? Transitioned;

        public List<HonuaGeofenceMonitoringRequest> Requests { get; } = [];

        public List<IReadOnlyList<string>> StoppedRegionIds { get; } = [];

        public void Emit(HonuaGeofenceTransition transition)
            => Transitioned?.Invoke(this, transition);

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
        {
            StoppedRegionIds.Add(regionIds);
            return ValueTask.CompletedTask;
        }
    }
}
