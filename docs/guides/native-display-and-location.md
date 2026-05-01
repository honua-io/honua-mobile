# Native Display and Location Integration

This guide covers the mobile-owned surfaces for native .NET map display and
device location behavior.

## Native Display Adapter

`Honua.Mobile.Maui.Display` provides a dependency-free adapter boundary for a
native map renderer:

- `HonuaNativeMapLayer` wraps an SDK `SourceDescriptor` with mobile rendering
  state such as visibility, z-index, filter, output fields, and projection.
- `HonuaNativeMapProjection` declares source and display CRS values. The mobile
  renderer performs projection; this package does not add geometry transforms.
- `HonuaNativeMapDisplayController` turns visible feature layers into SDK
  `FeatureQueryRequest` instances using the current `FeatureBoundingBox`.
- `IHonuaNativeMapAdapter` is the platform renderer boundary for Mapsui,
  platform maps, or a custom native view.

```csharp
using Honua.Mobile.Maui;
using Honua.Mobile.Maui.Display;
using Honua.Sdk.Abstractions.Features;

builder.Services
    .AddHonuaMobileSdk(clientOptions)
    .AddHonuaSdkGeoPackageOfflineSync(storeOptions, offlineManifest)
    .AddHonuaNativeDisplay();

builder.Services.AddSingleton<IHonuaNativeMapAdapter, MapsuiHonuaMapAdapter>();
```

```csharp
var scene = new HonuaNativeMapScene
{
    Layers =
    [
        new HonuaNativeMapLayer
        {
            Id = "parks",
            Source = new SourceDescriptor
            {
                Id = "parks",
                Protocol = FeatureProtocolIds.OgcFeatures,
                Locator = new SourceLocator { CollectionId = "parks" },
            },
            Projection = new HonuaNativeMapProjection
            {
                SourceCrs = HonuaNativeMapProjection.Wgs84,
                DisplayCrs = HonuaNativeMapProjection.WebMercator,
            },
            OutFields = ["objectid", "name", "status"],
        },
    ],
};

await display.RefreshAsync(scene, currentView, ct);
```

### Mapsui Evaluation

Mapsui is a reasonable candidate for issue #57 because its model lines up with
the boundary above: data providers feed layers, layers are ordered and styled
independently, and projection can stay in the renderer adapter instead of the
SDK packages.

The repo does not add Mapsui directly yet. Pulling it into
`Honua.Mobile.Maui` would commit every consumer to the display dependency,
platform handler lifecycle, renderer asset packaging, and projection stack
before the SDK geometry contracts have graduated. The safer shape is:

- keep `Honua.Mobile.Maui` dependency-free and source-descriptor based;
- implement `MapsuiHonuaMapAdapter` in an app or future renderer package;
- translate SDK `FeatureQueryResult` records into Mapsui provider features at
  the adapter edge;
- use Mapsui projection support only inside that adapter;
- benchmark pan/zoom refresh, offline GeoPackage layer loading, and annotation
  redraw before making Mapsui the default renderer.

## Device Location and Geofencing

`Honua.Mobile.Maui.Location` owns mobile runtime acquisition behavior while
leaving platform APIs behind app-provided adapters:

- `IHonuaDeviceLocationPermissionService` checks and requests foreground or
  background location permission.
- `IHonuaDeviceLocationProvider` acquires a one-shot foreground or background
  location fix.
- `IHonuaBackgroundLocationProvider` starts native background updates and
  returns an async-disposable session.
- `IHonuaGeofenceMonitor` delegates geofence registration and transitions to
  OS geofencing facilities.
- `HonuaDeviceLocationCoordinator` enforces permission order and validates
  request options before invoking the platform adapters.

```csharp
builder.Services
    .AddSingleton<IHonuaDeviceLocationPermissionService, MauiLocationPermissions>()
    .AddSingleton<IHonuaDeviceLocationProvider, MauiDeviceLocationProvider>()
    .AddSingleton<IHonuaBackgroundLocationProvider, MauiBackgroundLocationProvider>()
    .AddSingleton<IHonuaGeofenceMonitor, MauiGeofenceMonitor>()
    .AddHonuaDeviceLocation();
```

Foreground capture:

```csharp
var location = await locations.AcquireCurrentLocationAsync(
    new HonuaDeviceLocationRequest
    {
        RequiredAccess = HonuaLocationAccess.Foreground,
        Accuracy = HonuaLocationAccuracy.High,
        Timeout = TimeSpan.FromSeconds(20),
    },
    ct);
```

Background acquisition:

```csharp
await using var session = await locations.StartBackgroundUpdatesAsync(
    new HonuaBackgroundLocationOptions
    {
        Accuracy = HonuaLocationAccuracy.Balanced,
        MinimumInterval = TimeSpan.FromMinutes(5),
        MinimumDistanceMeters = 25,
        AllowBatterySaverDeferral = true,
        Purpose = "offline field workflow updates",
    },
    ct);
```

Geofence registration:

```csharp
await locations.StartGeofencingAsync(
    new HonuaGeofenceMonitoringRequest
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
                NotifyOnDwell = true,
                DwellTime = TimeSpan.FromMinutes(2),
            },
        ],
    },
    ct);
```

### Lifecycle Rules

- Request foreground permission before one-shot foreground capture.
- Request background permission separately before background updates or
  geofencing. Foreground permission is not treated as sufficient background
  access.
- Keep platform-specific permission copy, manifest entries, foreground service
  notifications, and background mode declarations in the app layer.
- Use OS geofencing APIs for enter, exit, and dwell detection. This package does
  not implement geometry predicates or distance checks.
- Dispose the background session when the workflow no longer needs updates.
- Map `HonuaDeviceLocation` into SDK field, routing, or future geometry
  contracts at the adapter edge when those SDK contracts are available.
