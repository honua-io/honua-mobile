# Migrating from the ArcGIS Maps SDK for .NET (MAUI) to Honua

**Move a .NET MAUI / Xamarin field app off the ArcGIS Maps SDK for .NET onto the
Honua Mobile SDK using a guided reimplement path plus runtime adapters.**

This guide is the deliverable for [honua-mobile#280](https://github.com/honua-io/honua-mobile/issues/280).
It covers the supported migration approach, an API-idiom mapping table from the
ArcGIS Maps SDK for .NET to the Honua MAUI surface, a phased reimplement plan,
and a federate-the-old-stack bridge for the transition.

It is a migration *planning aid*. It includes target-state examples; it is not a
guarantee that every compared capability is fully shipped and validated. For
source-backed status use
[docs/features/README.md](../features/README.md),
[docs/guides/validation-strategy.md](validation-strategy.md), and
[docs/guides/mobile-sdk-backlog-roadmap.md](mobile-sdk-backlog-roadmap.md).

For platform-agnostic field-platform migrations (Fulcrum, Survey123, KoBo) see
the companion [Migration Guide](migration-guide.md).

---

## Is there a `honua-migrate maui` codemod?

**No — and there is not going to be one.** App migration off the ArcGIS Maps SDK
for .NET is a **guided reimplement path plus adapters**, not an automated
codemod. This mirrors the decision recorded for the desktop/server .NET SDK in
[honua-sdk-dotnet#182](https://github.com/honua-io/honua-sdk-dotnet/issues/182).

### Why a codemod is not feasible for compiled .NET

The JavaScript and Python `honua-migrate` converters work because their inputs
are **source you ship and we can parse**: ES modules / TypeScript and `arcgis` /
`arcpy` Python. A codemod rewrites import paths and call sites and emits a
diff with `TODO(honua-migrate)` review markers.

A MAUI app built on the ArcGIS Maps SDK for .NET is different in kind:

1. **The Esri surface is a compiled binary dependency**, not source you own. The
   `Esri.ArcGISRuntime.*` types your code calls live in NuGet assemblies. A
   source-to-source codemod can rewrite *your* `using` lines and constructor
   calls, but it cannot reimplement the runtime behind them — `MapView`
   rendering, the `ServiceFeatureTable` sync model, `GeodatabaseSyncTask`, the
   ArcGIS Runtime licensing handshake, and the renderer/symbol pipeline have no
   1:1 type-for-type Honua equivalent to retarget to.
2. **The map control is a native renderer, not a thin client.** `Esri.ArcGISRuntime.Maui.MapView`
   owns its own rendering surface and interaction model. Honua's display path is
   a different architecture (native display adapter boundary + the `@honua/embed`
   viewer for web) — see
   [Native Display and Location Integration](native-display-and-location.md). You
   reimplement the map page; you do not retarget a control constructor.
3. **Behavioral semantics differ.** Esri's offline model (`GeodatabaseSyncTask` /
   replica geodatabases) and Honua's GeoPackage + queue-based sync engine
   (ClientWins / ServerWins / ManualReview, delta/replica) are not drop-in
   equivalents. A mechanical rewrite would produce code that compiles against a
   stub and silently changes sync behavior — worse than no tool.

A codemod that only rewrites `using` lines and leaves every call site marked
`TODO(honua-migrate)` would offer no real automation over this guide, while
implying a completeness it cannot deliver. So the honest, supported path is a
**guided reimplement**: this guide maps each Esri idiom to its Honua equivalent,
and you reimplement page-by-page with the old app running side by side.

### What *is* automatable

- **Data migration.** Feature data and attachments move with batch edits — see
  [Data and attachment migration](#5-data-and-attachment-migration). The
  platform-agnostic [Migration Guide](migration-guide.md) covers exporter
  scripts for hosted field platforms.
- **Server-side service migration.** Migrating the *services* your app consumes
  (FeatureServer/MapServer-style endpoints) onto Honua Server is tracked
  separately from this client-app guide; Honua Server speaks GeoServices- and
  OGC-compatible APIs so the data tier can be cut over independently.

---

## Migration approach: guided reimplement + federate bridge

```
┌────────────────────┐     ┌────────────────────┐     ┌────────────────────┐
│ 1. Inventory        │ →   │ 2. Reimplement      │ →   │ 3. Cut over         │
│  Esri call sites    │     │  page-by-page on    │     │  retire Esri NuGet, │
│  + offline model    │     │  Honua MAUI surface │     │  validate parity    │
└────────────────────┘     └────────────────────┘     └────────────────────┘
          │                          │                          │
          └──────────── federate the old app during the cut ────┘
             (run Esri build + Honua build side by side per workflow)
```

You do **not** flip the whole app at once. Stand up the Honua DI registration
alongside the existing Esri code, reimplement one workflow (e.g. the map page,
then the field-collection form, then offline sync), validate parity against the
[disconnected field workflow harness](disconnected-field-workflow-harness.md),
then retire the matching Esri code. Keep both builds installable during the
pilot so field crews can fall back.

---

## API idiom mapping: ArcGIS Maps SDK for .NET → Honua MAUI

This is the heart of the guided path. Each row is a reimplement target, not a
mechanical rewrite.

### Bootstrap & DI

| ArcGIS Maps SDK for .NET | Honua Mobile SDK (MAUI) |
|--------------------------|-------------------------|
| `builder.UseArcGISRuntime()` + `ArcGISRuntimeEnvironment.ApiKey = ...` in `MauiProgram` | `builder.Services.AddHonuaMobileSdk(new HonuaMobileClientOptions { BaseUri = ..., GrpcEndpoint = ... })` |
| Runtime license / API key handshake (`ArcGISRuntimeEnvironment`) | API key / bearer token stored via `IAuthTokenProvider.StoreTokenAsync(...)`; persisted in Keychain / Android secure storage by `AddHonuaMobilePlatformAuth()` |
| `Esri.ArcGISRuntime.*` NuGet packages | `Honua.Mobile.Maui`, `Honua.Mobile.Sdk`, `Honua.Mobile.Offline` (consume `Honua.Sdk.*` transitively) |

### Map / display

| ArcGIS Maps SDK for .NET | Honua Mobile SDK (MAUI) |
|--------------------------|-------------------------|
| `Esri.ArcGISRuntime.Maui.MapView` in XAML | Honua native display adapter — see [Native Display and Location Integration](native-display-and-location.md); web/embed via `<honua-map>` (see [Embeddable Map](embeddable-map.md)) |
| `new Map(BasemapStyle.ArcGISTopographic)` | Server-managed basemap / map-area package (`AddHonuaMapAreaDownload()`) |
| `SceneView` + `Scene` + 3D Tiles | `AddHonuaScenes()` + `HonuaSceneMetadata` discovery — see [3D Scene Embed](3d-scene-embed.md) |
| `GraphicsOverlay` / `Graphic` | Reimplement as Honua display overlays via the display adapter (no direct port) |
| `LocationDisplay` on `MapView` | Device location lifecycle via the native location integration |

### Layers & data access

| ArcGIS Maps SDK for .NET | Honua Mobile SDK (MAUI) |
|--------------------------|-------------------------|
| `new FeatureLayer(new ServiceFeatureTable(uri))` | Query through `IHonuaMobileClient.QueryFeaturesAsync(QueryFeaturesRequest)` (gRPC-first, REST fallback) and render via the display adapter |
| `ServiceFeatureTable.QueryFeaturesAsync(QueryParameters)` | `client.QueryFeaturesAsync(new QueryFeaturesRequest { ServiceId, LayerId, Where, OutFields, ReturnGeometry })` |
| Paged `FeatureQueryResult` iteration | `await foreach (var page in client.QueryFeaturesStreamAsync(request))` |
| `PortalItem` / `Portal` / WebMap loaders | Honua Server service catalog; no portal model — resolve services by id |
| `serviceFeatureTable.ApplyEditsAsync(edits)` | `client.ApplyEditsAsync(serviceId, layerId, FeatureEditBatch)` (online) or queue offline (below) |

### Offline & sync

| ArcGIS Maps SDK for .NET | Honua Mobile SDK (MAUI) |
|--------------------------|-------------------------|
| `GeodatabaseSyncTask` + `GenerateGeodatabaseJob` | `AddHonuaGeoPackageOfflineSync(GeoPackageSyncStoreOptions, OfflineSyncEngineOptions)` |
| Replica `.geodatabase` file | Standards-compliant GeoPackage `.gpkg` (interoperable with QGIS / ArcGIS) |
| `SyncGeodatabaseJob` + conflict handling | Queue-based sync engine with `SyncConflictStrategy.ClientWins` / `ServerWins` / `ManualReview`; delta/replica sync with cursor persistence |
| `OfflineMapTask` / preplanned map areas | `AddHonuaMapAreaDownload()` (offline basemap packages, path-traversal-protected) |
| Manual background sync scheduling | `AddHonuaBackgroundSync()` (connectivity-aware, semaphore-gated) |

### Field forms

| ArcGIS Maps SDK for .NET / Survey123 | Honua Mobile SDK (MAUI) |
|--------------------------------------|-------------------------|
| `FeatureForm` / `FormDefinition` (ArcGIS forms) | `AddHonuaMobileFieldCollection()` over `Honua.Sdk.Field` form schemas, validation, calculated fields, duplicate detection |
| Survey123 XLSForm / OpenRosa form | Convert to Honua form schema — mapping table in the [Migration Guide](migration-guide.md#-from-survey123-to-honua) |
| Attachment capture on a feature | Mobile capture adapters keep local media paths mobile-owned, converting to portable SDK attachment metadata before sync |

### Routing & geocoding

| ArcGIS Maps SDK for .NET | Honua Mobile SDK (MAUI) |
|--------------------------|-------------------------|
| `RouteTask` / `RouteParameters` | `client.Routing` (`GetDirectionsAsync`, fluent `Route()` builder, `GetServiceAreaAsync`) — see README Routing |
| `LocatorTask` (geocode) | Honua geocoding via the SDK client surface (server-side providers) |

---

## Step-by-step

### 1. Inventory the Esri surface

Find every Esri call site so you scope the reimplement. From the app's solution
root:

```bash
# Esri package references in the MAUI project(s)
grep -rEn 'Esri\.ArcGISRuntime' --include='*.csproj' .

# Esri usings and the controls/tasks you depend on
grep -rEn 'using Esri\.ArcGISRuntime|MapView|SceneView|ServiceFeatureTable|GeodatabaseSyncTask|RouteTask|LocatorTask|FeatureForm' \
  --include='*.cs' --include='*.xaml' .
```

Bucket the hits into: bootstrap/license, map/scene pages, layers/queries,
offline/sync, forms, routing/geocode. Each bucket maps to a section above.

### 2. Stand up Honua DI alongside Esri

Add the Honua registration to `MauiProgram.cs` without removing Esri yet:

```csharp
// MauiProgram.cs
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;

builder.Services
    .AddHonuaMobilePlatformAuth()
    .AddHonuaMobileSdk(new HonuaMobileClientOptions
    {
        BaseUri = new Uri("https://your-honua-server.com"),
        GrpcEndpoint = new Uri("https://your-honua-server.com"),
        PreferGrpcForFeatureQueries = true,
    })
    .AddHonuaRouting()
    .AddHonuaScenes()
    .AddHonuaMobileFieldCollection()
    .AddHonuaGeoPackageOfflineSync(
        new GeoPackageSyncStoreOptions
        {
            DatabasePath = Path.Combine(FileSystem.Current.AppDataDirectory, "honua-offline.gpkg"),
        },
        new OfflineSyncEngineOptions
        {
            ConflictStrategy = SyncConflictStrategy.ClientWins,
            BatchSize = 50,
        })
    .AddHonuaMapAreaDownload()
    .AddHonuaBackgroundSync();
```

### 3. Reimplement page-by-page

Pick the workflow with the clearest acceptance test first (usually a read-only
map page), reimplement it against the mapping table, and feature-flag it so it
runs next to the Esri version. Repeat for forms, then offline sync. Example
swap of an Esri query for the Honua client:

```csharp
// Before (ArcGIS Maps SDK for .NET)
var table = new ServiceFeatureTable(new Uri(serviceUrl));
await table.LoadAsync();
var query = new QueryParameters { WhereClause = "status = 'active'", ReturnGeometry = true };
query.OutFields.Add("*");
var esriResult = await table.QueryFeaturesAsync(query);

// After (Honua Mobile SDK)
using var honuaResult = await client.QueryFeaturesAsync(new QueryFeaturesRequest
{
    ServiceId = serviceId,
    LayerId = layerId,
    Where = "status = 'active'",
    OutFields = new[] { "*" },
    ReturnGeometry = true,
});
```

### 4. Validate parity per workflow

Run each reimplemented workflow through the
[disconnected field workflow harness](disconnected-field-workflow-harness.md):
online download → offline edits → reconnect sync → verification → evidence. Do
not retire the matching Esri code until the harness passes.

### 5. Data and attachment migration

Feature data and attachments move with batch edits, not a code rewrite. Export
from the source (ArcGIS service / replica geodatabase / hosted platform) and
apply to Honua:

```csharp
var batch = new FeatureEditBatch();
foreach (var record in exportedRecords)
{
    batch.Adds.Add(new Feature
    {
        Geometry = new Point(record.Longitude, record.Latitude),
        Attributes = MapAttributes(record),   // map source field names → Honua schema
    });
}
var result = await client.ApplyEditsAsync(targetServiceId, targetLayerId, batch);
```

Attachments: capture adapters keep local media paths mobile-owned and convert to
portable SDK attachment metadata before sync, so migrated photos/signatures flow
through the same sync path as new captures.

### 6. Federate during the cut, then retire Esri

During the pilot, keep the Esri build installable as a fallback. The server data
tier can be federated independently (Honua Server speaks GeoServices/OGC-
compatible APIs), so a workflow can read Honua data before its UI is fully
reimplemented. When every workflow passes parity, remove the
`Esri.ArcGISRuntime.*` package references and the Runtime license handshake.

---

## Acceptance checklist

Before retiring the Esri app:

- [ ] Every Esri call-site bucket from step 1 has a reimplemented Honua equivalent or a documented gap.
- [ ] Each workflow passes the disconnected field workflow harness (offline edit → reconnect → sync → verify).
- [ ] Migrated data validated against sample exports before production cutover.
- [ ] `Esri.ArcGISRuntime.*` package references removed; no Runtime license handshake remains.
- [ ] Field crews trained; fallback build retired only after sign-off.

---

## Related

- [Migration Guide](migration-guide.md) — platform-agnostic field-platform migrations (Fulcrum, Survey123, KoBo).
- [Native Display and Location Integration](native-display-and-location.md) — the display/location adapter boundary you reimplement against.
- [Offline Sync](offline-sync.md) — GeoPackage storage and sync engine configuration.
- [Disconnected Field Workflow Harness](disconnected-field-workflow-harness.md) — the parity acceptance runbook.
- [Mobile Contract Harmonization](mobile-contract-harmonization.md) — ownership boundary between `honua-mobile` and `honua-sdk-dotnet`.
- [honua-sdk-dotnet#182](https://github.com/honua-io/honua-sdk-dotnet/issues/182) — the desktop/server .NET precedent for guided reimplement over codemod.
