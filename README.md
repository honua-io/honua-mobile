# Honua Mobile SDK for .NET

.NET MAUI mobile SDK for [Honua Server](https://github.com/honua-io/honua-server) --
offline-first field data collection with GeoPackage storage, gRPC transport,
dynamic forms, and background sync.

Current mobile SDK roadmap coordination is tracked from
[honua-server#811](https://github.com/honua-io/honua-server/issues/811) and the
[mobile SDK roadmap](https://github.com/honua-io/honua-server/blob/trunk/docs/developer/mobile-sdk-roadmap.md).

## Packages

| Package | Purpose |
|---------|---------|
| **Honua.Mobile.Sdk** | Transport, auth, gRPC-first client, REST fallback, routing, and SDK scene metadata adapter |
| **Honua.Mobile.Field** | Mobile adapters for SDK-owned field forms, validation, media capture metadata, and workflow |
| **Honua.Mobile.Offline** | GeoPackage storage, sync queue, map area download, conflict resolution |
| **Honua.Mobile.Maui** | MAUI service registration, DI extensions, native display boundaries, and device location orchestration |
| **@honua/embed** | Framework-agnostic `<honua-map>` and `<honua-scene>` web components for ISV embeds |

## Quick Start

```csharp
// In MauiProgram.cs
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;

builder.Services
    .AddHonuaMobileAuth()
    .AddHonuaMobileSdk(new HonuaMobileClientOptions
    {
        BaseUri = new Uri("https://your-honua-server.com"),
        GrpcEndpoint = new Uri("https://your-honua-server.com"),
        ApiKey = "<your-api-key>",
        PreferGrpcForFeatureQueries = true,
    })
    .AddHonuaRouting()
    .AddHonuaScenes()
    .AddHonuaApiOfflineUploader()
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

## Offline Sync

GeoPackage-backed offline storage with queue-based sync:

- **GeoPackage storage** -- standards-compliant `.gpkg` files (interoperable with QGIS, ArcGIS)
- **Sync queue** -- queued edits with claim/lease semantics to prevent duplicate processing
- **Conflict resolution** -- ClientWins, ServerWins, or ManualReview strategies
- **Background sync** -- connectivity-aware with periodic timer and semaphore gating
- **Map area download** -- offline basemap packages with path traversal protection
- **Delta sync** -- replica-based incremental downloads with cursor persistence
- **Cache governance** -- per-layer TTL eviction and R-tree-backed bbox lookups for replicated features

## Field Collection

- **SDK-owned contracts** -- `Honua.Sdk.Field` owns form schemas, validation, calculated fields, duplicate detection, and record workflow
- **Mobile capture adapters** -- local media paths stay mobile-owned and convert to portable SDK attachment metadata before sync
- **Validation and workflow DI** -- `AddHonuaMobileFieldCollection()` registers a mobile adapter over SDK field services

## gRPC Transport

gRPC-first with automatic REST fallback:

```csharp
var request = new QueryFeaturesRequest
{
    ServiceId = serviceId,
    LayerId = layerId,
    Where = "1=1",
    OutFields = new[] { "*" },
};

using var features = await client.QueryFeaturesAsync(request);

await foreach (var page in client.QueryFeaturesStreamAsync(request))
{
    using (page)
    {
        ProcessFeaturePage(page.RootElement);
    }
}
```

Transport security enforced -- API keys and bearer tokens are never sent over HTTP
unless `AllowInsecureTransportForDevelopment` is explicitly set.

## Routing

Experimental GeoServices-compatible NAServer client for directions, service
areas, closest facility, and route optimization:

```csharp
var route = await client.Routing.GetDirectionsAsync(
    RoutingLocation.FromLatitudeLongitude(21.3069, -157.8583, "Start"),
    RoutingLocation.FromLatitudeLongitude(21.2810, -157.8037, "Finish"));

var optimized = await client.Routing.Route()
    .From(currentLocation)
    .Via(jobSite)
    .To(depot)
    .WithTraffic()
    .AvoidTolls()
    .ExecuteAsync();

var reachable = await client.Routing.GetServiceAreaAsync(depot, TimeSpan.FromMinutes(30));
```

## 3D Scene Metadata

Scene discovery resolves server-managed 3D Tiles and terrain URLs before a
renderer loads them:

```csharp
using Honua.Sdk.Abstractions.Scenes;

var scene = await client.Scenes.ResolveSceneAsync(
    "downtown-honolulu",
    new HonuaSceneResolveRequest
    {
        RequiredCapabilities = new[] { HonuaSceneCapabilities.ThreeDimensionalTiles },
    });

var tilesetUrl = scene.TilesetUrl;
var terrainUrl = scene.TerrainUrl;
```

## Repository Structure

```
src/
  Honua.Embed/                Embeddable map web component package
    tests/                    Web component DOM behavior tests (17 tests)
  Honua.Mobile.Sdk/           Core mobile client
  Honua.Mobile.Field/         SDK field workflow adapters
  Honua.Mobile.Offline/       GeoPackage sync engine
  Honua.Mobile.Maui/          MAUI platform integration
  Honua.Mobile.IoT/           IoT sensor abstractions (interface-only, future)
apps/
  Honua.Mobile.App/           Reference MAUI application
tests/
  Honua.Mobile.Sdk.Tests/     HTTP client, transport security, gRPC translation, routing, scenes (36 tests)
  Honua.Mobile.Field.Tests/   SDK field adapter validation, calculated fields, workflow (11 tests)
  Honua.Mobile.Offline.Tests/ Sync engine, conflicts, map download, GeoPackage (59 tests)
  Honua.Mobile.Maui.Tests/    MAUI integration helpers, map annotations, native display, location (24 tests)
  Honua.Mobile.Smoke.Tests/   End-to-end smoke paths (6 tests)
proto/
  honua/v1/                   gRPC protocol definitions
```

## Building

```bash
dotnet build Honua.Mobile.sln
dotnet test Honua.Mobile.sln
dotnet test tests/Honua.Mobile.Smoke.Tests/Honua.Mobile.Smoke.Tests.csproj
npm ci --prefix src/Honua.Embed
npm run build --prefix src/Honua.Embed
npm test --prefix src/Honua.Embed
```

Building Android targets requires a configured Android SDK. The library projects
(`Sdk`, `Field`, `Offline`, `Maui`) target `net10.0` and build on any platform
without the MAUI workload.

## Status

Production-ready foundation for offline sync, forms, and gRPC transport.
.NET test coverage across SDK, Field, Offline, MAUI, and Smoke projects, plus
DOM tests for the embeddable map package.

The IoT module (`Honua.Mobile.IoT`) contains interface definitions only --
no implementation yet.

## Documentation

- **[Getting Started](docs/getting-started/)** -- installation, tutorial, and developer checklist
- **[Guides](docs/guides/)** -- in-depth guides for offline sync, security, camera, performance, and more
- **[API Reference](docs/api/)** -- core SDK API documentation

## License

[Apache 2.0](LICENSE)
