# honua-migrate maui

ArcGIS Maps SDK for .NET (MAUI) → Honua source codemod. This is the .NET MAUI
member of the `honua-migrate` family that already ships for the JavaScript SDK
(`@arcgis/core` call-site rewriter) and the Python SDK (arcpy translation). It
follows the same **scan → translate → report** playbook.

## What it does

MAUI app code is C# *source* (views, view-models, code-behind), so unlike the
compiled-`.dll` ArcObjects/GP case scoped not-feasible in
`honua-sdk-dotnet#182`, the high-frequency call sites are safely rewritable with
a [Roslyn](https://github.com/dotnet/roslyn) syntax rewriter — no compilation or
type resolution required, so it runs on any platform without the MAUI workload.

The codemod:

- recognizes `new` expressions of ArcGIS Maps SDK types (import-scoped via
  `using Esri.ArcGISRuntime.*` directives, or fully qualified);
- rewrites the high-frequency MapView/Map/layer/graphics/geometry surface 1:1 to
  the Honua mobile SDK and fixes up the `using` directives;
- emits `TODO(honua-migrate)` review markers (and, with `--annotate-todos`,
  inline comments) for recognized-but-not-mechanically-safe constructs;
- prints a parity report with per-construct auto/manual metrics.

## Usage

```bash
# Dry run (report only):
dotnet run --project tools/Honua.Migrate.Maui.Cli -- ./MobileApp

# Apply the rewrite in place:
dotnet run --project tools/Honua.Migrate.Maui.Cli -- ./MobileApp --write

# Annotate manual-review call sites with inline TODO comments:
dotnet run --project tools/Honua.Migrate.Maui.Cli -- ./MobileApp --write --annotate-todos

# CI gate: fail if any manual-review markers remain:
dotnet run --project tools/Honua.Migrate.Maui.Cli -- ./MobileApp --fail-on-manual
```

`<path>` may be a directory tree (recursively scanned; `bin`/`obj`/`.git`/`.vs`
and generated `*.g.cs`/`*.designer.cs` files are skipped) or a single `.cs`
file.

## Coverage (first increment)

Auto-migrated (deterministic 1:1 constructor rewrite):

| ArcGIS type | Honua type |
| --- | --- |
| `MapView` | `HonuaMapView` |
| `Map` | `HonuaMap` |
| `Basemap` | `HonuaBasemap` |
| `FeatureLayer` | `HonuaFeatureLayer` |
| `GraphicsOverlay` | `HonuaGraphicsOverlay` |
| `Graphic` | `HonuaGraphic` |
| `SimpleMarkerSymbol` / `SimpleLineSymbol` / `SimpleFillSymbol` | `HonuaMarkerSymbol` / `HonuaLineSymbol` / `HonuaFillSymbol` |
| `MapPoint` / `Polyline` / `Polygon` / `Envelope` / `SpatialReference` | `HonuaPoint` / `HonuaPolyline` / `HonuaPolygon` / `HonuaEnvelope` / `HonuaSpatialReference` |

Guided-manual (recognized, `TODO(honua-migrate)` marker emitted): `SceneView`,
`Scene`, `ServiceFeatureTable`, `QueryParameters`. These need data-binding,
URL→service-id, or property-rename decisions that are not mechanically safe.

Deferred long tail (not yet recognized): portal items, renderers, route/locator
tasks, the widget/toolkit surface, XAML markup migration. These are tracked as
follow-up increments on the migration ticket and will extend
[`MauiMappingTable`](MauiRewriteSpec.cs) the same way the JS codemod grew its
`REWRITE_SPECS`.
