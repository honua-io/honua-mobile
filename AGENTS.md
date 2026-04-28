# Repository Guidance

This repo owns mobile runtime behavior and display/app integration. It should
consume reusable .NET SDK packages from `honua-sdk-dotnet` rather than carrying
parallel platform-neutral clients or contracts.

## Belongs Here

- MAUI registration, dependency injection glue, app lifecycle integration, and
  platform-specific service wiring.
- Native storage adapters, GeoPackage/SQLite file placement and lifecycle,
  mobile cache directories, cleanup, and platform constraints.
- Background sync scheduling, reachability, battery-aware retry behavior,
  permissions, camera/media capture, GPS acquisition, and device sensors.
- Field workflow screens, form rendering, local media paths, capture UX, and
  mobile validation presentation.
- Native/mobile map UI, annotations/drawing UI, AR/VR anchoring, and platform
  display integration.
- `@honua/embed`, web components, Cesium, MapLibre, deck.gl, browser cache
  adapters, and viewer packaging.
- Native .NET map/viewer adapters, including any Mapsui evaluation or
  integration, as long as they consume SDK source descriptors and geometry
  contracts rather than redefining them.
- Thin compatibility adapters that translate SDK contracts into mobile runtime
  behavior.

## Does Not Belong Here

- New plain `net*` service clients for Honua Server APIs.
- Provider-neutral feature query/edit contracts, gRPC/GeoServices/OGC/WFS
  clients, routing clients, geocoding clients, catalog clients, admin REST
  clients, real-time stream contracts, or replica sync API clients.
- Stable scene metadata models, scene package manifests, field form schemas,
  field validation engines, record workflow rules, non-UI plugin manifests, or
  shared geometry/spatial-reference primitives.
- Server contract DTOs that should be tested across repos.

## Mismatch Checks

- If a class can run without MAUI, DOM APIs, native storage, OS permissions, or
  renderer APIs, check `honua-sdk-dotnet` first.
- If a class implements geometry predicates, topology, buffers, simplification,
  WKT/WKB parsing, GeoJSON conversion, ring orientation, spatial indexes, or CRS
  transforms, consume the SDK's NetTopologySuite/ProjNet-backed geometry surface
  instead of adding mobile-local geometry logic.
- If a file under `src/Honua.Mobile.Sdk` is a server API client or plain model,
  treat it as migration input for the SDK, not as new mobile-owned surface.
- If a feature requires server functionality, link the `honua-server` dependency
  issue from the mobile issue.
- If a mobile task consumes new SDK contracts, cross-link the SDK issue and keep
  local work limited to adapters, registration, tests, and migration shims.
- Consume `Honua.Sdk.*` through published, versioned NuGet packages. Do not copy
  SDK source or add long-lived sibling `ProjectReference` links to
  `honua-sdk-dotnet`; temporary local references need an explicit removal issue.

## Migration Targets

- Replace `Honua.Mobile.Sdk` query/edit/OGC/gRPC/routing/scene clients with
  versioned `Honua.Sdk.*` NuGet packages.
- Replace generic mobile geometry/bounds primitives with SDK geometry and
  spatial-reference types.
- Keep native SQLite/GeoPackage implementations here unless a separate
  platform-specific SDK storage package is explicitly created.
