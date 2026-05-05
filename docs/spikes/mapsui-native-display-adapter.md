# Mapsui-Inspired Native Display Adapter Spike

Issue: [honua-mobile#57][mobile-57]

## Decision

Use a mobile-owned native renderer adapter shape that consumes SDK source
descriptors, SDK feature query pages, SDK CRS metadata, and SDK geometry
surfaces. Do not introduce platform-neutral display contracts in this repo, and
do not add Mapsui to `honua-sdk-dotnet` or any SDK core package.

Mapsui is a strong candidate for a future MAUI/native renderer package because
its map, layer, provider, projection, and renderer split matches the boundary
already used by `Honua.Mobile.Maui.Display`. This slice should borrow that
architecture and defer a direct Mapsui package reference until a prototype can
measure MAUI handler lifecycle, pan/zoom refresh, annotation redraw, and offline
GeoPackage layer performance.

## Ownership

This repo owns the native display runtime adapter:

- MAUI registration and lifecycle hooks.
- Native renderer bridge classes under `Honua.Mobile.Maui.Display` or a future
  mobile renderer package.
- Renderer-specific conversion from SDK feature pages to native map features.
- Native storage and offline package placement for renderer assets.
- Device interaction, annotations, selections, hit testing, and capture UI.

The SDK owns reusable contracts and geometry:

- `SourceDescriptor`, `FeatureSource`, `FeatureQueryRequest`, and
  `FeatureQueryResult`.
- NetTopologySuite geometry surfaces and conversion helpers.
- CRS metadata and ProjNet-backed coordinate transforms.
- Provider-neutral feature, tile, scene, routing, and catalog client contracts.

Mapsui belongs only in a MAUI/native display package or app package. A future
`Honua.Mobile.Maui.Mapsui` package could reference Mapsui, BruTile, and
renderer-specific assets, while `Honua.Sdk.*` packages remain display-free.

## Adapter Shape

The existing `HonuaNativeMapDisplayController` is the intended orchestration
shape:

1. A mobile scene supplies ordered layers with SDK `SourceDescriptor` values,
   visibility, z-index, output fields, filters, renderer hints, and CRS
   metadata.
2. The current native view supplies a display extent and target display CRS.
3. The controller creates SDK `FeatureQueryRequest` values for visible feature
   layers and requests geometry with the display CRS as `OutputCrs`.
4. The adapter receives `FeatureQueryResult` pages and converts each batch to
   renderer-native features at the edge.
5. The adapter owns view updates, layer creation, style application, feature
   replacement, annotations, hit testing, and renderer disposal.

That keeps feature query shape in the SDK and renderer concerns in mobile. If
the SDK returns GeoJSON or protocol JSON for an interim package version, the
adapter should use SDK conversion helpers once available. When SDK feature pages
expose typed NetTopologySuite geometries, the native adapter should pass NTS
geometries into Mapsui-compatible features without redefining point, line,
polygon, envelope, or spatial-reference types locally.

## Mapsui Mapping

Candidate Mapsui mapping for a later prototype:

- `MapControl` and platform handlers: MAUI view ownership and lifecycle.
- `Map`: native scene container built from mobile layer state.
- `ILayer`: one layer per SDK source descriptor or offline tile source.
- `IProvider` or a custom provider: converts SDK feature batches into Mapsui
  features.
- Mapsui/NTS geometry handling: renderer-edge conversion only.
- Projection wrappers: adapter-local use of ProjNet or Mapsui projection
  support when source and display CRS differ.
- BruTile/MBTiles support: native/offline tile rendering only when source
  descriptors or scene packages identify compatible tile sets.

Prototype acceptance should measure:

- First render and pan/zoom refresh with paged `FeatureQueryResult` batches.
- Feature replacement and diff strategy for live updates.
- Annotation redraw latency for field capture workflows.
- Offline GeoPackage and MBTiles startup cost on Android and iOS.
- Memory behavior when layers are toggled or scenes are disposed.

## CRS And Raster Policy

Feature geometry can request the viewer target CRS through SDK query metadata or
use SDK/ProjNet conversion helpers at the adapter edge. The native display
package must not implement custom CRS transforms, topology operations, WKT/WKB
parsing, ring orientation, simplification, buffering, or spatial indexes.

Raster tile reprojection is out of scope. Native display may render raster or
terrain tiles only when the server or offline package emits tiles in the viewer
target CRS and tile matrix set. If the target CRS is unsupported for a raster
source, the adapter should reject that layer or ask the user to switch the view
CRS instead of warping tiles client-side.

## Web Display Relationship

Web display remains MapLibre GL JS and deck.gl first. The browser adapter uses
the same SDK source descriptors and feature query pages, then translates them to
GeoJSON or binary deck.gl attributes inside `@honua/embed`.

Native .NET display should follow the same contract flow:

- SDK source descriptors identify feature, vector tile, raster tile, stream, or
  offline package sources.
- SDK feature pages carry records, geometry, attributes, object ids, pagination,
  and CRS metadata.
- The renderer adapter is the only layer that knows whether output becomes a
  deck.gl layer, a Mapsui layer, a platform map overlay, or another native
  renderer primitive.

No shared display abstraction is needed between web and native at this stage.
The shared surface is the SDK contract set, not a display contract defined in
`honua-mobile`.

## Dependency Links

SDK dependencies:

- [honua-sdk-dotnet#55][sdk-55]: typed geometry and spatial reference core.
- [honua-sdk-dotnet#62][sdk-62]: browser/WASM-safe SDK support for web display.
- [honua-mobile#50][mobile-50]: MapLibre/deck.gl web display adapter.

Server dependencies to link as native display paths are implemented:

- [honua-server#108][server-108]: Mapbox Vector Tile endpoints.
- [honua-server#197][server-197]: OGC API Tiles vector tile endpoints.
- [honua-server#310][server-310]: raster outputs and broader CRS/tile-matrix
  support.
- [honua-server#339][server-339]: real-time feature streaming subscriptions.
- [honua-server#501][server-501]: WebSocket/SSE transport lifecycle.
- [honua-server#503][server-503]: real-time spatial and attribute filters.
- [honua-server#505][server-505]: CDC delta push pipeline.
- [honua-server#531][server-531]: public map sharing and collaborative editing.
- [honua-server#653][server-653]: unified GeoJSON and tile execution paths.
- [honua-server#692][server-692]: transactional outbox for feature-change CDC.
- [honua-server#839][server-839]: terrain and elevation tile service.

## Follow-Up Slices

- Prototype `MapsuiHonuaMapAdapter` in a mobile/native renderer package or
  sample app without changing SDK packages.
- Add renderer-edge conversion tests for SDK feature batches to Mapsui features
  after the target SDK geometry package version is selected.
- Add lifecycle and disposal tests around native map handlers before making
  Mapsui a default dependency.
- Add explicit server dependency links to implementation issues for vector
  tiles, raster tiles, streams, and public sharing when those display paths move
  beyond the spike.

## PR Body Recommendation

Include this line in the PR body:

```text
Related to #57
```

Suggested summary:

```text
## Summary
- Documented the Mapsui-inspired native .NET display adapter decision.
- Kept Mapsui scoped to future MAUI/native display packages, not SDK core.
- Captured web/native parity over shared SDK contracts and raster CRS limits.

## Validation
- Markdown validation
```

[mobile-50]: https://github.com/honua-io/honua-mobile/issues/50
[mobile-57]: https://github.com/honua-io/honua-mobile/issues/57
[sdk-55]: https://github.com/honua-io/honua-sdk-dotnet/issues/55
[sdk-62]: https://github.com/honua-io/honua-sdk-dotnet/issues/62
[server-108]: https://github.com/honua-io/honua-server/issues/108
[server-197]: https://github.com/honua-io/honua-server/issues/197
[server-310]: https://github.com/honua-io/honua-server/issues/310
[server-339]: https://github.com/honua-io/honua-server/issues/339
[server-501]: https://github.com/honua-io/honua-server/issues/501
[server-503]: https://github.com/honua-io/honua-server/issues/503
[server-505]: https://github.com/honua-io/honua-server/issues/505
[server-531]: https://github.com/honua-io/honua-server/issues/531
[server-653]: https://github.com/honua-io/honua-server/issues/653
[server-692]: https://github.com/honua-io/honua-server/issues/692
[server-839]: https://github.com/honua-io/honua-server/issues/839
