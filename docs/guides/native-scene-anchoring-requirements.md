# Native Scene Anchoring Requirements

This spike resolves the native anchoring decision for
[#38](https://github.com/honua-io/honua-mobile/issues/38) so
[#23](https://github.com/honua-io/honua-mobile/issues/23) can start from a
bounded platform plan instead of a generic AR/VR promise. It is part of the
3D/scene sequence in
[#12](https://github.com/honua-io/honua-mobile/issues/12) and depends on the
scene foundation described in the
[Mobile 3D and AR Dependency Matrix](mobile-3d-ar-dependency-matrix.md).

Platform requirements and source links are current as of 2026-05-01. Recheck
the vendor pages before creating implementation PRs because ARKit, ARCore,
WebXR, and MAUI support matrices change outside this repository.

## Recommendation

Use a native MAUI-hosted AR prototype with platform-specific handlers, not a
WebXR or WebView-first AR runtime.

The first implementation ticket should target Android ARCore Geospatial anchors
inside a MAUI Android handler. Use ARCore Geospatial Terrain or WGS84 anchors
when VPS is available, then apply a Honua-owned control-point calibration layer
before rendering utility or asset overlays. Add the iOS ARKit implementation as
the next runtime ticket using the same shared calibration and overlay policy.

Do not use GPS-only placement for production field overlays. GPS-only can power
a coarse "nearby scene" preview, but inspection, utility, excavation, and depth
visualization workflows require visible uncertainty and either platform
geospatial tracking, surveyed control points, RTK/external GNSS, or another
tenant-approved calibration method.

Do not use WebXR as the first #23 runtime. WebXR remains useful for browser
experiments after #42, but browser `immersive-ar` support is not consistent
enough across iOS, Android, WebView, and managed enterprise devices to be the
primary mobile field path.

## Target Prototype

| Decision | Requirement |
|----------|-------------|
| Host | MAUI app surface with platform-native AR view handlers. Shared .NET code owns DI, permissions, scene selection, offline package selection, telemetry, and field workflow state. |
| First runtime | Android ARCore Geospatial API with local ARCore anchors and Terrain/WGS84 anchors when available. |
| Second runtime | iOS ARKit world tracking plus ARGeoTracking where available; support LiDAR/depth as an optional quality tier, not a baseline requirement. |
| Rendering payload | Lightweight field overlays: points, polylines, simple meshes, labels, and bounded extrusions derived from SDK scene/feature contracts. Full Cesium 3D Tiles rendering stays in `<honua-scene>` and MAUI WebView scene wrappers until a native renderer ticket is justified. |
| Anchoring mode | Geospatial anchor plus local visual-inertial tracking plus Honua control-point calibration. GPS-only is degraded mode. |
| Offline mode | Cached scene metadata, features, terrain/elevation samples, and control points can render offline. Platform VPS/geotracking cannot be assumed offline. |
| Safety posture | AR output is a visual decision aid. It must not claim survey, excavation, or locate-grade certainty unless the tenant supplies accepted survey-grade inputs and the app shows the resulting residual error. |

## Device Capability Matrix

| Runtime | Minimum capability | Prototype target | Hard constraints | Runtime checks |
|---------|--------------------|------------------|------------------|----------------|
| iOS ARKit | iOS 11+ on A9 or later devices for ARKit; camera permission; `ARConfiguration.isSupported` for the selected mode. | iOS device with A12 or later and cellular/GPS capability for ARGeoTracking; LiDAR preferred for depth/occlusion validation. | ARGeoTracking requires iOS/iPadOS 14+, A12 or later, cellular/GPS capability, outdoor use, supported geographic coverage, and an internet connection for localization imagery. | Check AR configuration support, camera permission, precise location permission, AR tracking state, ARGeoTracking availability at the current coordinate, and location accuracy. |
| Android ARCore | ARCore-certified Android device with Google Play Services for AR or approved regional equivalent; camera permission. | ARCore device that supports Geospatial API and Depth API where possible; include at least one rugged/enterprise Android device in the test pool. | Geospatial API needs ARCore API enablement, location permission, device sensor/GPS data, VPS coverage, and a device that supports the feature. Depth support is device-specific. | Check ARCore install/update, camera permission, fine location permission, `Session.isGeospatialModeSupported`, Earth tracking state, VPS availability, horizontal/vertical/yaw accuracy, and Depth support. |
| Browser/WebXR | Secure context, WebGL, browser support for WebXR, and an `immersive-ar` capable device/browser. | Prototype-only browser demo after #42 if a customer needs web AR evaluation. | WebXR is limited availability and not safe as the default field runtime, especially for iOS/WebView coverage and managed-device policy. | Check `navigator.xr`, `isSessionSupported("immersive-ar")`, user activation requirements, storage/cache availability, and fallback path. |
| MAUI | Supported .NET MAUI Android/iOS target with platform-specific native interop. | Shared MAUI shell plus native iOS/Android AR handlers. | MAUI WebView can host `<honua-scene>` for 3D previews, but it does not solve camera-pose anchoring. Windows and Mac Catalyst remain out of native AR scope for #23. | Check target framework/platform, handler availability, permissions, app lifecycle pause/resume, battery/thermal state, and local package availability. |

## Anchoring Strategy Comparison

| Strategy | Fit | Accuracy behavior | Offline behavior | Risks | Recommendation |
|----------|-----|-------------------|------------------|-------|----------------|
| GPS-only WGS84 to local ENU transform | Coarse nearby-scene preview, broad asset awareness, route context. | Bounded by reported location accuracy and heading error. Multipath, canopy, urban canyons, and approximate-location permissions can move overlays by meters or worse. | Works offline when GNSS is available and scene data is cached. | Looks authoritative while being too coarse for underground assets or safety decisions. | Use only as degraded planning mode with uncertainty rings and disabled precision actions. |
| Visual-inertial local tracking | Stable local overlay once a session tracks well. | Good short-range relative stability, but not globally aligned unless tied to a known origin. Drift and relocalization change perceived placement. | Works offline after AR session starts, subject to lighting and environment quality. | Blank surfaces, low light, fast movement, vibration, and lens obstruction reduce tracking quality. | Required for every native AR runtime, but never enough by itself for geospatial field overlays. |
| Platform geospatial anchors | Outdoor site overlays tied to latitude/longitude/altitude. | Better global alignment when VPS/geotracking localizes. Must honor platform-provided horizontal, vertical, and yaw accuracy. | Cannot be assumed offline because ARKit geotracking and ARCore VPS depend on provider localization data. | Coverage gaps, provider terms, quota, network, and regional availability. | Primary online anchor source for the prototype, with runtime availability checks and fallback. |
| Known control points | Utility corridors, construction sites, facility yards, repeat inspections. | Best Honua-owned path when survey control, QR markers, plaques, known valves, or mapped assets are available. Residual error can be measured and shown. | Works offline if control-point definitions and scene package are cached. | Requires field setup and a calibration UX; incorrect control metadata creates false confidence. | Required for operational field overlays and the default calibration layer on top of geospatial anchors. |
| Feature or asset anchors | Indoor equipment, cabinets, valves, markers, repeatable asset inspections. | Strong relative placement near the recognized target; weak for broad outdoor scenes. | Image/object targets can work offline when reference assets are packaged; cloud anchors need network. | Asset movement, dirty markers, viewpoint changes, and platform-specific recognition limits. | Use for focused equipment workflows after Android/iOS baseline tickets, not for the first utility corridor prototype. |
| WebXR hit-test or anchors | Browser demo and stakeholder review. | Depends on browser/device support and may lack persistent, geospatial, or native sensor access needed for field confidence. | Browser cache/storage behavior depends on #42 and platform policy. | Inconsistent production support and harder enterprise device validation. | Keep as optional spike after native path and #42. |

## Calibration And Accuracy Policy

Native AR must expose an explicit confidence state before drawing field overlays:

1. Resolve the scene through `Honua.Sdk.*` scene contracts and choose either an
   online endpoint or a validated offline package.
2. Collect the platform location fix and reject precise overlay mode until the
   reported horizontal accuracy is under the configured workflow threshold.
3. Start visual-inertial tracking and wait for normal tracking state before
   accepting calibration input.
4. If online and available, initialize ARCore Geospatial or ARKit GeoTracking
   and record horizontal, vertical, and yaw accuracy where the platform exposes
   them.
5. Ask the user to confirm at least one known control point for coarse
   visualization, at least two separated control points for horizontal heading
   and scale validation, and at least three non-collinear 3D control points when
   vertical placement is part of the workflow. More points should improve
   confidence and reveal local distortion.
6. Compute and display residual error for the selected transform from scene
   coordinates to AR world coordinates.
7. Persist calibration only with the scene id, scene revision, package id,
   platform runtime, device model, timestamp, and control-point ids. Invalidate
   it when any of those inputs change materially.

Prototype thresholds should be configurable per tenant and workflow. These are
starting defaults, not accuracy guarantees:

| Workflow state | Entry threshold | Allowed UX |
|----------------|-----------------|------------|
| Coarse preview | Location permission granted, scene resolved, reported horizontal accuracy <= 10 m, and AR tracking not failed. | Show nearby assets with large uncertainty rings; disable measurements, depth claims, and excavation/safety language. |
| Site review | Platform geospatial tracking available or at least one confirmed control point; horizontal accuracy <= 2 m; yaw accuracy <= 15 degrees when provided. | Show overlays, labels, photos, and issue capture with visible confidence status. |
| Precision inspection | Two or more surveyed horizontal control points, or three 3D control points for vertical placement, transform residual <= 0.5 m, and source feature/depth metadata declares survey quality and vertical datum. | Enable measurement capture and high-confidence annotations, still with residual/error metadata attached to exported evidence. |
| Utility locate or excavation safety | Tenant-defined survey/RTK/control process outside AR alone. | AR may supplement documentation, but the UI must not present itself as the locate authority. |

Underground assets need stricter handling than surface assets:

- Depth values must include unit, vertical datum, source, collection method,
  timestamp, and quality class before the app renders a depth-specific overlay.
- Terrain/elevation and utility depth data may use different vertical
  references. The app must either transform them through an SDK/server-provided
  contract or mark the vertical relationship as unknown.
- If vertical uncertainty is larger than the utility depth or clearance being
  inspected, render a warning band rather than a precise pipe/cable centerline.
- Captured AR photos and reports must include device pose accuracy, control
  residual, scene revision, package id, and whether the session was online or
  offline.

## Offline Constraints

Offline AR is allowed only when the local package can answer the same anchoring
questions the online path would answer:

| Offline input | Requirement |
|---------------|-------------|
| Scene metadata | Cached from `IHonuaSceneClient` / `Honua.Sdk.*` and tied to the same scene id and revision as the rendered data. |
| Scene package | Validated through the package manifest policy from [Offline 3D Scene Packages](offline-3d-scene-packages.md). Expired or partial packages must not render protected assets. |
| Terrain/elevation | Include tiles or precomputed samples needed for the AR area. Missing terrain falls back to 2D/relative-height overlays with a visible warning. |
| Control points | Package known surveyed points, marker definitions, or field asset anchors with quality metadata and revision ids. |
| Geospatial tracking | Treat ARCore VPS and ARKit GeoTracking as online-only unless a platform explicitly documents an offline mode for the target deployment. |
| Auth | Follow [Protected 3D Scene Auth](protected-3d-scene-auth.md). Do not store bearer tokens or long-lived signed URLs in AR calibration records. |

Offline fallback order:

1. Valid package plus surveyed control points.
2. Valid package plus marker/image/object anchors.
3. Valid package plus GPS-only coarse preview.
4. No valid package: do not render protected scene content.

## Server And SDK Dependencies

Mobile AR runtime work must consume versioned SDK packages and server-provided
scene capabilities. Do not add new provider-neutral scene contracts or geometry
logic in this repository.

| Dependency | Needed for AR anchoring | Status at spike time |
|------------|-------------------------|----------------------|
| [honua-io/honua-sdk-dotnet#70](https://github.com/honua-io/honua-sdk-dotnet/issues/70) SDK scene metadata and package contracts | Shared scene resolution, endpoint metadata, access envelopes, and offline package models. | Closed; consume through versioned `Honua.Sdk.*` NuGet packages. |
| [#55](https://github.com/honua-io/honua-mobile/issues/55) mobile scene contract migration | Keeps this repo focused on runtime/display adapters after SDK contract migration. | Closed; do not recreate local contract models. |
| [#31](https://github.com/honua-io/honua-mobile/issues/31) `<honua-scene>` | Web/WebView 3D scene preview and non-AR rendering path. | Closed; not the native AR camera runtime. |
| [#36](https://github.com/honua-io/honua-mobile/issues/36), [#40](https://github.com/honua-io/honua-mobile/issues/40), [#41](https://github.com/honua-io/honua-mobile/issues/41) offline scene package policy, contracts, and MAUI downloader | Offline native AR package selection and validation. | Closed; native AR should consume these outputs. |
| [#42](https://github.com/honua-io/honua-mobile/issues/42) browser/WebView cache adapter | WebXR or WebView scene package experiments. | Separate embed/display workstream; do not duplicate it here. |
| [#37](https://github.com/honua-io/honua-mobile/issues/37) and [#44](https://github.com/honua-io/honua-mobile/issues/44) protected scene auth and access envelope models | Online protected scene rendering and refresh behavior. | Closed on mobile; server support still needed. |
| [honua-io/honua-server#837](https://github.com/honua-io/honua-server/issues/837) hosted 3D Tiles serving | Honua-owned scene asset roots and metadata. | Open. |
| [honua-io/honua-server#839](https://github.com/honua-io/honua-server/issues/839) terrain/elevation tiles | Terrain surface context for field overlays and offline package content. | Closed. |
| [honua-io/honua-server#840](https://github.com/honua-io/honua-server/issues/840) elevation query/profile API | Point elevation, line profiles, and depth/terrain reconciliation. | Open. |
| [honua-io/honua-server#841](https://github.com/honua-io/honua-server/issues/841) extruded 3D feature output | Lightweight buildings/assets/utility overlays before full generated 3D Tiles. | Open. |
| [honua-io/honua-server#842](https://github.com/honua-io/honua-server/issues/842) 3D Tiles generation | Future generated meshes and larger operational scenes. | Open; not required for first AR overlay prototype. |
| [honua-io/honua-server#844](https://github.com/honua-io/honua-server/issues/844) scene dataset registry | Stable scene ids, bounds, attribution, access policy, and operator-managed configuration. | Open. |
| [honua-io/honua-server#849](https://github.com/honua-io/honua-server/issues/849) signed access envelope | Protected 3D asset access for browser, WebView, and native renderers. | Open. |

Minimum server capability for #23 is a scene id, bounds, attribution, access
policy, terrain/elevation metadata when used, and a lightweight field overlay
payload. A full generated 3D Tiles pipeline is useful for #12 but should not
block the first AR utility/asset overlay prototype.

## #23 And #12 Sequencing

The sequence from #12 to #23 should be:

1. Keep `<honua-scene>` and SDK scene discovery as the non-AR 3D baseline.
2. Finish or explicitly mock the server dependencies needed by the prototype:
   scene registry, terrain/elevation metadata, access envelope, and lightweight
   overlay payloads.
3. Implement the Android ARCore native handler prototype and calibration flow.
4. Validate the field workflow with one offline package and one online scene.
5. Implement the iOS ARKit native handler using the same shared calibration and
   workflow state.
6. Decide whether WebXR deserves a separate demo after the native runtime and
   #42 cache adapter are stable.

#23 is ready to start when the team accepts this spike, chooses the first test
devices, and creates native-runtime tickets from the split below. #12 remains
the parent scene-services epic for server 3D Tiles, terrain, extrusion, and
scene registry maturity.

## Follow-Up Ticket Split

Create these as separate implementation tickets rather than one cross-platform
AR issue:

| Ticket title | Runtime | Scope |
|--------------|---------|-------|
| `feat(ar-android): ARCore geospatial field overlay prototype` | Android / MAUI handler | ARCore session lifecycle, Geospatial/Terrain/WGS84 anchors, local anchors, accuracy telemetry, Android permissions, simple point/line overlay rendering, and degraded GPS-only preview. |
| `feat(ar-ios): ARKit geo-tracked field overlay prototype` | iOS / MAUI handler | ARKit session lifecycle, world tracking, ARGeoTracking availability checks, ARGeoAnchor placement, optional LiDAR depth/occlusion, iOS permissions, and parity with Android confidence states. |
| `feat(ar-maui): shared calibration and workflow shell` | Shared MAUI runtime | DI registration, scene/package selection, control-point calibration state, residual error model, confidence UI state, photo/report metadata capture, and runtime telemetry. |
| `feat(ar-offline): native AR scene package resolver` | Shared MAUI plus iOS/Android storage adapters | Consume #41 package catalog and SDK package manifest contracts, resolve package-local overlay/terrain assets for native AR, enforce expiry, and expose package quality state. |
| `quality(ar): field anchoring validation checklist and fixtures` | QA / field validation | Define test sites, control-point fixtures, device matrix, offline package fixture, acceptance thresholds, and evidence captured for each run. |
| `spike(webxr): browser AR scene anchoring feasibility` | Browser / WebXR | Optional follow-up after #42 for customer demos; validate `immersive-ar`, hit test/anchor support, package cache integration, and unsupported-browser UX. |

No Flutter ticket should be created for this workstream.

## Validation Checklist

Before #23 is considered implemented on a native runtime, each platform must
pass this checklist on physical devices:

- Device support is checked at runtime, and unsupported devices get a normal
  fallback path.
- Camera, precise location, motion/sensor, and storage permissions are requested
  only when needed and are handled when denied.
- The AR session pauses/resumes correctly across app backgrounding, lock screen,
  phone calls, and low battery/thermal states.
- The UI displays tracking state, geospatial availability, location accuracy,
  yaw/heading accuracy when available, package freshness, and calibration
  residual.
- The app can render the same scene in online mode and with a validated offline
  package.
- A stale, expired, partial, or revoked protected package does not render.
- Overlay captures include scene id, scene revision, package id, runtime,
  device model, timestamp, accuracy metrics, and control-point residual.
- GPS-only mode never enables precision measurements or utility locate/safety
  language.
- Two-device validation covers at least one iOS ARKit target and one Android
  ARCore target before the workflow is promoted beyond prototype.

## Platform References

- Apple: [Verifying Device Support and User Permission](https://developer.apple.com/documentation/arkit/verifying-device-support-and-user-permission)
- Apple: [Understanding World Tracking](https://developer.apple.com/documentation/arkit/understanding-world-tracking)
- Apple: [ARGeoTrackingConfiguration](https://developer.apple.com/documentation/arkit/argeotrackingconfiguration)
- Apple: [Core Location `horizontalAccuracy`](https://developer.apple.com/documentation/corelocation/cllocation/horizontalaccuracy)
- Google: [ARCore supported devices](https://developers.google.com/ar/discover/supported-devices)
- Google: [Enable AR in your Android app](https://developers.google.com/ar/develop/java/enable-arcore)
- Google: [ARCore Geospatial API](https://developers.google.com/ar/develop/geospatial)
- Google: [GeospatialPose accuracy fields](https://developers.google.com/ar/reference/java/com/google/ar/core/GeospatialPose)
- Google: [ARCore Depth](https://developers.google.com/ar/develop/depth)
- Android: [`Location.getAccuracy()`](https://developer.android.com/reference/android/location/Location#getAccuracy())
- W3C: [WebXR Device API](https://www.w3.org/TR/webxr/)
- MDN: [WebXR Device API](https://developer.mozilla.org/en-US/docs/Web/API/WebXR_Device_API)
- Microsoft: [.NET MAUI supported platforms](https://learn.microsoft.com/en-us/dotnet/maui/supported-platforms)
- Microsoft: [.NET MAUI native embedding](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/native-embedding)
