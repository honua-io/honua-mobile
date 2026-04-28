# Mobile 3D and AR Dependency Matrix

This matrix is the technical dependency baseline for AR/VR work in #23 and the
sequencing reference for the mobile epic in #1. It connects the server 3D scene
backlog, the mobile SDK scene contracts, the `<honua-scene>` web component, and
future native AR/VR prototypes.

Client SDK packages remain available to all editions. Edition gates below apply
to hosted server capabilities, production modules, or managed data services.

## Dependency Path

The 3D and AR path should move in this order:

1. Honua Server serves or registers scene assets: hosted 3D Tiles, terrain,
   elevation profiles, extruded features, and generated tilesets.
2. `HonuaSceneService` resolves scene metadata, endpoint URLs, capability flags,
   attribution, and auth requirements for mobile and web clients.
3. `<honua-scene>` renders resolved 3D Tiles and terrain in browser or WebView
   hosts through CesiumJS.
4. MAUI, React Native, Flutter, Swift, and Kotlin hosts wrap either the web
   scene component or a native renderer after the server and SDK contracts are
   stable.
5. AR/VR prototypes anchor lightweight scene data to device pose, camera, and
   field context after the basic scene path is proven.

## Recommended Sequence

| Step | Work | Why it comes here |
|------|------|-------------------|
| 1 | honua-io/honua-server#837 hosted 3D Tiles serving | Gives clients a Honua-owned `tileset.json` and asset path instead of only external sample data. |
| 2 | honua-io/honua-server#838 CesiumJS smoke suite | Proves server output works in the renderer before mobile wrappers depend on it. |
| 3 | #31 `<honua-scene>` web component | Provides the narrow client rendering surface for browser and WebView hosts. |
| 4 | honua-io/honua-server#844 scene dataset registry | Gives clients stable scene IDs, endpoint metadata, attribution, and auth policy. |
| 5 | #32 `HonuaSceneService` contracts | Lets apps discover scenes without hard-coded renderer URLs. |
| 6 | honua-io/honua-server#839 and honua-io/honua-server#840 terrain and elevation | Adds the surface and query data needed for field context and profiles. |
| 7 | honua-io/honua-server#841 and honua-io/honua-server#842 3D features and generation | Produces Honua-owned operational 3D data instead of only hosted external assets. |
| 8 | #36 offline 3D cache packaging policy | Defines whether 3D scenes can be trusted in disconnected field workflows. |
| 9 | #37 protected 3D tiles auth handoff | Decides how private scene assets are safely loaded by browsers, WebViews, and native hosts. |
| 10 | #38 native scene anchoring requirements | Narrows ARKit, ARCore, WebXR, and MAUI requirements before #23 implementation. |
| 11 | #23 AR/VR field workflow enablement | Starts native AR/VR only after scene data, auth, offline, and platform risks are explicit. |

## Capability Matrix

| Client capability | Required server ticket | Required SDK/mobile ticket | Platform requirement | Offline constraint | Platform risk | Edition gate |
|-------------------|------------------------|----------------------------|----------------------|-------------------|---------------|--------------|
| Public/external 3D Tiles preview in `<honua-scene>` | None for external tilesets; honua-io/honua-server#837 for Honua-hosted assets | #31 | Browser or WebView with WebGL support and CesiumJS assets available | None for first slice; network required | Low: renderer integration and asset bundling are already proven | Community for SDK/component; Pro for Honua-hosted 3D Tiles |
| Scene discovery by stable scene ID | honua-io/honua-server#844, plus honua-io/honua-server#837 for hosted tilesets | #32 | Any .NET/MAUI host that can call the SDK transport | Metadata can be cached, but endpoint URLs and auth expiry must be respected | Medium: server schema and endpoint auth need to stay stable | Community for contracts; Pro for hosted scenes |
| Terrain surface in a 3D scene | honua-io/honua-server#839 | #32, #31 | Browser/WebView renderer with terrain provider support | Terrain tile packages need extent, LOD, attribution, and invalidation rules | Medium: raster source quality, tile density, and mobile memory pressure | Community for basic terrain tiles; Pro for managed hosted terrain |
| Elevation query and profile UX | honua-io/honua-server#840 | Future SDK method if the existing transport is not enough | Native or MAUI UI for profile charts; renderer optional | Offline profiles need precomputed samples or local raster/terrain cache | Medium: accuracy and sampling behavior must match server output | Pro |
| Extruded 3D feature overlays | honua-io/honua-server#841 | Future feature-layer scene overlay ticket | Browser/WebView renderer first; native renderer later | Offline mode needs feature attributes, Z/height fields, styling, and versioning | Medium: styling parity between 2D MapLibre and 3D renderer can drift | Community for basic extrusion; Pro for hosted managed layers |
| Generated 3D Tiles from Honua data or model assets | honua-io/honua-server#842 | #32 for discovery; future upload/import client contracts if needed | Browser/WebView renderer first; native renderer optional | Large generated tilesets need resumable download and storage quotas | High: data pipeline, LOD, textures, and device memory can dominate effort | Pro |
| I3S / Esri Scene Layer compatibility | honua-io/honua-server#843 | Future compatibility or adapter ticket | ArcGIS-compatible clients and conformance fixtures | Offline I3S packaging is out of scope until the spike defines demand | High: protocol compatibility and conformance risk | Enterprise |
| Browser/WebXR scene prototype | honua-io/honua-server#837, honua-io/honua-server#838, honua-io/honua-server#844 | #31, #32, #38 | Secure browser context with WebXR and WebGL-capable device/browser | Offline support is limited until #36 defines packages and cache behavior | High: browser/device support varies and must be validated per target | Enterprise for production AR/VR modules |
| Native AR overlay prototype | honua-io/honua-server#837, honua-io/honua-server#839, honua-io/honua-server#840, honua-io/honua-server#841 | #23, #38 | iOS ARKit or Android ARCore capable devices; MAUI wrapper strategy TBD | Needs lightweight cached features, terrain/elevation context, and predictable auth | High: device pose, GPS accuracy, calibration, and depth alignment are product risks | Enterprise |
| Offline 3D scene package | honua-io/honua-server#837, honua-io/honua-server#839, honua-io/honua-server#840, honua-io/honua-server#842, honua-io/honua-server#844 | #36 policy, #40, #41, #42, #8 | iOS/Android/MAUI storage management; browser cache support where viable | Package manifest must cover extent, LOD, byte budget, hashes, auth expiry, and eviction | High: package size, stale data, and battery/network usage can break field UX | Pro for managed offline packages; Enterprise for large operational deployments |
| MAUI scene wrapper | honua-io/honua-server#837, honua-io/honua-server#844 | #31, #32, future MAUI wrapper ticket | MAUI WebView first; native graphics surface only after renderer decision | Same as underlying renderer; WebView cache must not outlive auth policy | Medium: WebView differences across Android, iOS, Windows, and Mac Catalyst | Community for wrapper; Pro/Enterprise by backing service |

## Platform Capability Requirements

| Platform | Baseline requirement | Notes |
|----------|----------------------|-------|
| iOS | WKWebView/WebGL for `<honua-scene>`; ARKit-capable devices for native AR | Validate memory pressure with real tilesets before committing to offline packages. |
| Android | Android WebView/WebGL for `<honua-scene>`; ARCore-capable devices for native AR | Device and GPU variability make #38 mandatory before broad AR commitments. |
| Browser | WebGL for CesiumJS; secure context and WebXR-capable device/browser for AR experiments | Treat WebXR as a prototype path until target browser/device support is validated. |
| MAUI | WebView host for the first scene wrapper; platform-specific native bridge only after renderer choice | Keep `HonuaSceneService` as the shared contract so renderer choice stays replaceable. |

## Edition Gates

| Edition | 3D/AR scope |
|---------|-------------|
| Community | SDK contracts, public/external scene rendering, basic terrain and extrusion clients when backed by open endpoints. |
| Pro | Honua-hosted 3D Tiles, managed terrain, elevation APIs, generated 3D Tiles, and managed offline scene packages. |
| Enterprise | Production AR/VR modules, I3S compatibility, large operational offline 3D deployments, and advanced building/floor-aware scene workflows. |

## Open Questions

Open decisions are tracked as follow-up tickets:

| Ticket | Question |
|--------|----------|
| #37 | Should protected tilesets use signed URLs, short-lived scene tokens, proxying, request-header injection, or a hybrid model? |
| #38 | Which AR anchoring strategy and first prototype platform should #23 use? |

The offline 3D package model from #36 is captured in
[Offline 3D Scene Packages](offline-3d-scene-packages.md), with implementation
follow-ups in #40, #41, and #42.
