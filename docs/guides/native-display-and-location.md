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

## Annotation Styling

`Honua.Mobile.Maui.Annotations` provides native field-collection annotation
primitives (`HonuaAnnotation`, `HonuaAnnotationLayer`) styled by
`HonuaAnnotationStyle`. The style record carries only local visual properties:

- `FillColor`, `StrokeColor`, `StrokeWidth`, `Opacity`
- `TextColor`, `TextSize`

These values are applied by the native renderer/adapter when drawing
annotations on the device. They describe how a field user's annotation looks
locally; they are not a data-driven styling pipeline.

### Orthogonal to server (MapLibre) styles

`HonuaAnnotationStyle` is **intentionally orthogonal** to the Honua Server
data-driven style system and is **not** part of the cross-repo style strategy:

- It is **not** a server style. There is no MapLibre, SLD, or Esri style
  document, no `styleId`, no `honua://styles/{styleId}` reference, and no
  consumption of the server's `/ogc/styles` (OGC API – Styles) endpoints.
- It styles a single user-authored annotation, not a feature collection by
  attribute/zoom rules. There are no data-driven expressions, filters, or
  layer-paint specifications.
- Rendering happens locally/natively on the device. Server-side or
  server-published style resolution is never involved.

This boundary is deliberate per
[`honua-server` ADR-0048 — OGC API – Styles and cross-repo style coherence](https://github.com/honua-io/honua-server/blob/trunk/docs/contributor/adr/0048-ogc-api-styles-and-cross-repo-style-coherence.md).
Server style consumption (e.g. reading `/ogc/styles` or applying a
`styleId`-keyed MapLibre/SLD/Esri style for offline data-driven rendering) is
out of scope for annotation styling and would only be added if and when
offline data-driven rendering is separately scoped.

| Concern | `HonuaAnnotationStyle` (this package) | Server data-driven styles (ADR-0048) |
|---------|---------------------------------------|--------------------------------------|
| Purpose | Native styling of a user-authored annotation | Data-driven rendering of feature collections |
| Format | Flat fill/stroke/opacity/text properties | MapLibre / SLD / Esri style documents |
| Identity | None (instance carried with the annotation) | `styleId`, `honua://styles/{styleId}` |
| Source | Authored on device | Published via `/ogc/styles` (OGC API – Styles) |
| Rendering | Local/native renderer | Map renderer applying the data-driven style |

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
- `HonuaSdkGeofenceWorkflowController` consumes SDK-owned
  `Honua.Sdk.Geometry.HonuaGeofenceDefinition` instances, maps them to native
  monitoring regions, and emits workflow/sync events through
  `IHonuaGeofenceWorkflowEventSink`.

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

### SDK Geofence and Proximity Workflows

Portable geofence rules and transition state stay in `honua-sdk-dotnet`. The
mobile adapter only performs native runtime work:

- Convert SDK geofence definitions into OS geofence regions broad enough to
  wake the app for enter, exit, and proximity workflows.
- Delegate foreground/background permission checks to the platform permission
  adapter.
- Keep background polling off by default. If a workflow enables background
  updates, the controller clamps the interval to at least five minutes unless
  the app sets a stricter `MinimumBackgroundInterval`.
- Defer active background sessions when battery saver is enabled and restart
  when the lifecycle controller receives `BatterySaverDisabled`.
- Publish native transitions as `HonuaGeofenceWorkflowEvent` values so field UI,
  local persistence, and sync queues can register sinks without owning platform
  geofence APIs.

```csharp
using Honua.Mobile.Maui.Location;
using Honua.Sdk.Geometry;

builder.Services.AddSingleton<IHonuaGeofenceWorkflowEventSink, FieldSyncQueueSink>();

var workflow = serviceProvider.GetRequiredService<HonuaSdkGeofenceWorkflowController>();
await workflow.StartAsync(
    new HonuaSdkGeofenceWorkflowRequest
    {
        Definitions = sdkGeofenceDefinitions,
        BackgroundUpdates = null, // OS geofence wakeups only; no GPS polling.
        MinimumNativeRadiusMeters = 25,
        Metadata = new Dictionary<string, object?>
        {
            ["workflow"] = "inspection-arrival",
        },
    },
    ct);
```

Android platform notes:

- Request foreground location before background location, and surface the
  Android 10+ background permission handoff clearly in the app UX.
- Use native geofencing APIs or a fused-location monitor inside the app adapter;
  keep exact geofence evaluation in the SDK evaluator when the app has a fresh
  position sample.
- Prefer OS geofence wakeups for enter/exit/proximity starts. Enable background
  updates only for workflows that need continuous progress and keep the minimum
  interval/distance bounded.

iOS platform notes:

- Include `NSLocationWhenInUseUsageDescription` and
  `NSLocationAlwaysAndWhenInUseUsageDescription`; request Always access only
  after the user has accepted foreground location.
- Use Core Location region monitoring for wakeups. Significant-change or
  standard background location updates should be reserved for workflows that
  need them and should respect battery-saver lifecycle events.
- Persist emitted `HonuaGeofenceWorkflowEvent` values before starting network
  sync so transitions are not lost when iOS suspends the app shortly after a
  region callback.

### External GNSS and High-Accuracy Capture

The Field Collection app keeps high-accuracy location details in mobile-owned
capture evidence and maps only the stable geometry/media parts into SDK-owned
field contracts:

- `FieldLocationFix` wraps the MAUI `Location` reading plus mobile source
  metadata.
- `FieldLocationCaptureEvidence` snapshots latitude, longitude, altitude,
  horizontal/vertical accuracy, speed, heading, timestamp, source kind,
  provider, reduced/mock flags, and optional receiver metadata.
- `IHighAccuracyLocationMetadataProvider` is an optional app/platform hook.
  Apps can register it to enrich MAUI readings with Android, iOS, Bluetooth,
  USB, NMEA, RTK, or vendor SDK receiver details.
- Record geometry captures write companion attributes such as
  `gps_source`, `gps_accuracy_m`, `gps_provider`, `gps_receiver_name`, and
  `gps_captured_at_utc`.
- Location form fields preserve companion metadata under the field-specific
  prefix generated by the mobile form runtime.
- Photo attachments store capture evidence in local SQLite metadata, map the
  coordinates to the SDK `FieldMediaAttachment.CaptureLocation`, and include a
  compact `honua.location.*` keyword payload during SDK-backed attachment sync.

The default Field Collection service requests `GeolocationAccuracy.Best` and
falls back to built-in GPS metadata when MAUI does not expose a richer source.
External GNSS distinction is available when the platform hook supplies source
metadata.

Android platform path:

- Use MAUI `Geolocation` for the baseline fix and permission flow.
- In an Android adapter, read native `Android.Locations.Location.Provider`,
  mock-provider state, extras, NMEA messages, `GnssStatus`, or receiver/vendor
  SDK state.
- For Bluetooth or USB receivers, keep transport pairing, connection state,
  NTRIP/RTK correction status, firmware, and receiver identity in the app or
  platform adapter. Pass only support-safe fields into
  `FieldLocationCaptureMetadata`.

iOS platform path:

- Use MAUI `Geolocation` for the baseline fix and permission flow.
- In an iOS adapter, read Core Location accuracy, speed, course, altitude, and
  `CLLocationSourceInformation` where available, including accessory-produced
  and software-simulated indicators.
- For MFi, Bluetooth, or vendor SDK receivers, keep pairing/authentication in
  the platform adapter and pass receiver name/model/firmware plus correction
  state into `FieldLocationCaptureMetadata`.

Physical-device validation checklist:

- Built-in GPS on Android and iOS: verify source label, horizontal/vertical
  accuracy, altitude, heading, speed, timestamp, and reduced/mock flags.
- External GNSS on Android: connect a Bluetooth/NMEA or vendor receiver, verify
  `External GNSS` source, provider, receiver name/model, sub-meter accuracy,
  photo attachment capture metadata, and pushed attachment keywords.
- External GNSS on iOS: connect an accessory-supported receiver, verify
  accessory/simulation flags where exposed, receiver metadata, and fallback
  behavior when the receiver disconnects.
- Offline workflow: capture a GPS point and photo while offline, restart the
  app, confirm local GeoPackage/SQLite metadata persists, sync later, and
  inspect the pushed feature attributes and attachment keyword payload.

Automated coverage should mock the metadata provider rather than native
hardware. The existing Field Collection tests cover source mapping, record
navigation metadata, attachment persistence, and sync keyword mapping.

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
