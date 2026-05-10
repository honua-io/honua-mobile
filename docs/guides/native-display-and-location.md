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

The focused decision spike is
[`docs/spikes/mapsui-native-display-adapter.md`](../spikes/mapsui-native-display-adapter.md).
It records the native adapter shape, Mapsui dependency boundary, shared
SDK-contract flow with the MapLibre/deck.gl web adapter, raster CRS limits, and
related SDK/server dependencies.

The repo does not add Mapsui directly yet. Pulling it into
`Honua.Mobile.Maui` would commit every consumer to the display dependency,
platform handler lifecycle, renderer asset packaging, and projection stack
before the SDK geometry contracts have graduated. The safer shape is:

- keep `Honua.Mobile.Maui` dependency-free and source-descriptor based;
- implement `MapsuiHonuaMapAdapter` in an app or future renderer package;
- translate SDK `FeatureQueryResult` pages and SDK-owned NTS geometry into
  Mapsui provider features at the adapter edge;
- use Mapsui projection support only inside that adapter;
- reject raster tile reprojection unless the server or offline package emits
  tiles in the viewer target CRS;
- benchmark pan/zoom refresh, offline GeoPackage layer loading, and annotation
  redraw before making Mapsui the default renderer.

## Native Scene Anchoring Adapter

`Honua.Mobile.Maui.SceneAnchoring` provides the mobile-owned boundary for #38
and #23 native AR anchoring work. It does not define scene metadata, package
manifests, geometry primitives, or server clients. Apps resolve scenes and
packages through `Honua.Sdk.*`, then pass scene/package IDs and mobile runtime
state into the platform adapter.

- `IHonuaNativeArSceneAnchorAdapter` is the ARKit/ARCore boundary implemented by
  app or platform packages.
- `HonuaNativeArSceneAnchoringController` checks support, starts the native
  session, handles app background/resume/stop transitions, and evaluates
  readiness for mobile UX gates.
- `HonuaNativeArReadiness` gates coarse rendering, evidence capture, and
  precision tools from runtime status, horizontal/yaw accuracy, package state,
  control-point count, and calibration residual.
- `HonuaNativeArSceneAnchorRequest` carries scene id, scene revision, optional
  offline package id, selected anchoring mode, and control-point IDs. The SDK or
  app remains responsible for resolving the authoritative scene/package data.
- `HonuaNativeArEvidenceContext` snapshots the mobile runtime metadata that AR
  field photos, annotations, and reports should carry: scene/package ids,
  online/offline state, runtime, device model, active anchor mode, package
  state, accuracy samples, control points, readiness, blockers, and warnings.

```csharp
using Honua.Mobile.Maui;
using Honua.Mobile.Maui.SceneAnchoring;

builder.Services
    .AddSingleton<IHonuaNativeArSceneAnchorAdapter, AndroidArCoreSceneAnchorAdapter>()
    .AddHonuaNativeSceneAnchoring(new HonuaNativeArSessionOptions
    {
        CoarsePreviewHorizontalAccuracyMeters = 10,
        SiteReviewHorizontalAccuracyMeters = 2,
        PrecisionCalibrationResidualMeters = 0.5,
    });
```

```csharp
var readiness = await sceneAnchoring.StartAsync(
    new HonuaNativeArSceneAnchorRequest
    {
        SceneId = sceneId,
        SceneRevision = sceneRevision,
        PackageId = packageId,
        IsOffline = packageId is not null,
        PreferredAnchoringMode = HonuaNativeArAnchoringMode.PlatformGeospatial,
        ControlPointIds = selectedControlPointIds,
    },
    ct);

if (readiness.CanRenderOverlay)
{
    // Render through the app's native AR adapter with the readiness state visible in the UI.
}
```

Field evidence capture:

```csharp
var evidenceContext = await sceneAnchoring.CreateEvidenceContextAsync(ct);

if (evidenceContext.CanAttachToFieldEvidence)
{
    // Attach evidenceContext to the captured photo, annotation, or report.
}
```

Lifecycle integration:

```csharp
await sceneAnchoring.HandleAppLifecycleAsync(
    HonuaNativeArAppLifecycleEvent.EnteringBackground,
    ct);

await sceneAnchoring.HandleAppLifecycleAsync(
    HonuaNativeArAppLifecycleEvent.ResumingForeground,
    ct);
```

GPS-only readiness never enables precision tools. Offline rendering requires a
valid package state, and stale, expired, partial, or revoked packages block AR
overlays until the package is refreshed.

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
- `HonuaBackgroundLocationLifecycleController` owns the active background
  session/geofence lifecycle, including suspend shutdown and battery-saver
  deferral.

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

Lifecycle-managed background runtime:

```csharp
await backgroundLocation.StartAsync(
    new HonuaBackgroundLocationLifecycleRequest
    {
        BackgroundUpdates = new HonuaBackgroundLocationOptions
        {
            Accuracy = HonuaLocationAccuracy.Balanced,
            MinimumInterval = TimeSpan.FromMinutes(5),
            MinimumDistanceMeters = 25,
            AllowBatterySaverDeferral = true,
            Purpose = "offline field workflow updates",
        },
        Geofences = jobSiteGeofences,
    },
    ct);

await backgroundLocation.HandleLifecycleEventAsync(
    HonuaLocationLifecycleEvent.BatterySaverEnabled,
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

Native geofence transitions are forwarded through
`HonuaDeviceLocationCoordinator.GeofenceTransitioned`. Enter, exit, dwell, and
proximity signals remain mobile acquisition events; map them into SDK geofence
evaluation/event contracts at the adapter edge once those contracts are present
in the consumed `Honua.Sdk.*` packages.

### Lifecycle Rules

- Request foreground permission before one-shot foreground capture.
- Request background permission separately before background updates or
  geofencing. Foreground permission is not treated as sufficient background
  access.
- Keep platform-specific permission copy, manifest entries, foreground service
  notifications, and background mode declarations in the app layer.
- Route app suspend, foreground/background, battery-saver, and shutdown signals
  into `HonuaBackgroundLocationLifecycleController` when the workflow needs a
  managed background runtime.
- Let `AllowBatterySaverDeferral` pause active background updates/geofences and
  restart them when battery saver is disabled. Set it to `false` only for
  workflows with an explicit product requirement to keep native monitoring
  active through power-saver mode.
- Use OS geofencing APIs for enter, exit, and dwell detection. This package does
  not implement geometry predicates or distance checks.
- Dispose the background session when the workflow no longer needs updates.
- Map `HonuaDeviceLocation` into SDK field, routing, or future geometry
  contracts at the adapter edge when those SDK contracts are available.

### SDK Versus Native Ownership

| Behavior | Owner |
|----------|-------|
| Geofence rule models, geometry predicates, buffers, CRS transforms, and portable event evaluation | `honua-sdk-dotnet` packages |
| iOS/Android permission prompts, manifest/background mode declarations, foreground services, and native sensor acquisition | Mobile app/platform adapters |
| Background session lifecycle, suspend/shutdown behavior, battery-saver deferral, and OS geofence start/stop | `Honua.Mobile.Maui.Location` |
| Mapping native enter/exit/dwell/proximity events into SDK event contracts | Mobile adapter edge after the SDK contracts are available |
