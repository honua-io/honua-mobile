# Honua Mobile SDK for .NET

.NET MAUI mobile SDK for [Honua Server](https://github.com/honua-io/honua-server) --
offline-first field data collection with GeoPackage storage, gRPC transport,
dynamic forms, and background sync.

Current mobile SDK roadmap coordination is tracked from
[honua-server#811](https://github.com/honua-io/honua-server/issues/811) and the
[mobile SDK roadmap](https://github.com/honua-io/honua-server/blob/trunk/docs/developer/mobile-sdk-roadmap.md).
The current source-backed mobile feature map is in [docs/features/README.md](docs/features/README.md).

## Validation status

| Layer | Status | Run on | Coverage |
| --- | --- | --- | --- |
| Unit | ✅ | every PR | 294 tests across 5 .NET projects (SDK, Offline, Field, FieldCollection, MAUI) |
| Integration (in-process loopback) | ✅ | every PR | 9 loopback tests in `Honua.Mobile.ServerIntegration.Tests` (`SdkServerIntegrationTests`, `OfflineServerIntegrationTests`, `FieldCollectionServerIntegrationTests`) against a real ASP.NET Core loopback server; the same project also hosts 4 `LiveHonuaServerFixtureOptionsTests` harness-config tests |
| Smoke | ✅ | every PR | 18 tests in `Honua.Mobile.Smoke.Tests` (`quality-gates` job) |
| Embed DOM | ✅ | every PR | jsdom suites under `src/Honua.Embed/tests/` |
| Live server (Docker image) | ✅ | every PR via `Live Server Integration` workflow (hard gate; vendored seed at `tests/seed/mobile-offline-demo-v1.sql`) | 11 tests in `LiveHonuaServerInteractionTests` (incl. unary + server-streaming live gRPC); Testcontainers spins up `honuaio/honua-server:nightly` + PostGIS, vendored seed loaded into postgres before the live server starts |
| Cloud acceptance (staging) | 🟡 | manual `workflow_dispatch` | 7 tests in `DisconnectedFieldWorkflowAcceptanceTests`; production promotion blocked on honua-server#965 |
| Physical device | 🟡 | deferred to GA | AR/VR field workflow tracked under honua-mobile#23 (closed, follow-ups in `docs/guides/native-scene-anchoring-requirements.md`); emulator/simulator platform smoke covers part of the surface |

See [docs/guides/validation-strategy.md](docs/guides/validation-strategy.md) for
the per-capability coverage matrix, known gaps, and which CI workflow runs
which bucket.

## Packages

| Package | Purpose |
|---------|---------|
| **Honua.Mobile.Sdk** | Transport, auth, gRPC-first client, REST fallback, routing, and SDK scene metadata adapter |
| **Honua.Mobile.Offline** | GeoPackage storage, sync queue, map area download, conflict resolution |
| **Honua.Mobile.Maui** | MAUI service registration, DI extensions, native display boundaries, native scene anchoring, and device location orchestration |
| **@honua/embed** | Framework-agnostic `<honua-map>` and `<honua-scene>` web components for ISV embeds |

## Quick Start

```csharp
// In MauiProgram.cs
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

After sign-in or bootstrap, store the API key or bearer token with `IAuthTokenProvider.StoreTokenAsync(...)`;
the platform auth registration persists it in iOS Keychain or Android secure storage.

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
  Honua.Mobile.Offline/       GeoPackage sync engine
  Honua.Mobile.Maui/          MAUI platform integration, native display, location, and scene anchoring
  Honua.Mobile.IoT/           IoT sensor abstractions (interface-only, future)
apps/
  Honua.Mobile.App/           Reference MAUI application
tests/
  Honua.Mobile.Sdk.Tests/     HTTP client, transport security, gRPC translation, routing, scenes (80 tests)
  Honua.Mobile.FieldCollection.Tests/ FieldCollection auth, sync, storage, diagnostics (10 tests)
  Honua.Mobile.ServerIntegration.Tests/ Loopback and opt-in live Honua image integration surface
  Honua.Mobile.Offline.Tests/ Sync engine, conflicts, map download, GeoPackage (65 tests)
  Honua.Mobile.Maui.Tests/    MAUI integration helpers, map annotations, native display, location, scene anchoring (40 tests)
  Honua.Mobile.Smoke.Tests/   End-to-end smoke paths and optional live Honua query (7 tests)
proto/
  honua/v1/                   gRPC protocol definitions
```

## Building

Fresh checkouts need access to the private Honua GitHub Packages feed for
`Honua.Sdk.*` packages. Use a GitHub token that can read packages in the
`honua-io` organization:

```bash
gh auth refresh -s read:packages
export HONUA_GITHUB_PACKAGES_USER="$(gh api user --jq .login)"
export HONUA_GITHUB_PACKAGES_TOKEN="$(gh auth token)"
```

Then run the local validation baseline:

```bash
scripts/validate-local.sh
```

The script restores, builds, runs .NET tests and smoke tests, verifies format
for the core source projects, and runs the `@honua/embed` npm build/tests. It
uses a temporary NuGet config for `HONUA_GITHUB_PACKAGES_TOKEN` and removes it
on exit. Without those environment variables it falls back to any existing
NuGet credentials already configured for the `github-honua` source.

Equivalent manual commands:

```bash
dotnet restore Honua.Mobile.sln
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

The server integration project starts a real ASP.NET Core loopback server and
exercises the implemented SDK, offline, FieldCollection auth, and mobile
exception-reporting HTTP paths without requiring external infrastructure. It
also includes opt-in live Honua image tests that use Testcontainers or a
pre-started Honua URL when `HONUA_MOBILE_LIVE_SERVER_TESTS=1` is set; see
`docs/guides/offline-sync.md`.
The smoke test project can also run an optional live Honua query when
`HONUA_MOBILE_SMOKE_BASE_URL`, `HONUA_MOBILE_SMOKE_SERVICE_ID`,
`HONUA_MOBILE_SMOKE_LAYER_ID`, and optionally `HONUA_MOBILE_SMOKE_API_KEY` are
set.

Release workflow, branch-protection, package metadata, Dependabot, Trivy, and
platform smoke guardrails for honua-server #826 are documented in
[Repo Scaffolding Gates](docs/guides/repo-scaffolding-gates.md).

The `Live Server Integration` workflow
(`.github/workflows/live-server-integration.yml`) runs
`LiveHonuaServerInteractionTests` against a Docker-hosted Honua server stack
on every PR and on pushes to `main`. See
[Disconnected Field Workflow Harness](docs/guides/disconnected-field-workflow-harness.md#live-server-integration-workflow)
for scope, triggers, and the seed-SQL gap.

## Status

Production-ready foundation for offline sync, forms, and gRPC transport.
.NET test coverage across SDK, Field, FieldCollection, server integration,
Offline, MAUI, and Smoke projects, plus DOM tests for the embeddable map package.

The IoT module (`Honua.Mobile.IoT`) contains interface definitions only --
no implementation yet.

## Documentation

- **[Getting Started](docs/getting-started/)** -- installation, tutorial, and developer checklist
- **[Guides](docs/guides/)** -- in-depth guides for offline sync, security, camera, performance, and more
- **[SDK Contract Stability Roadmap](docs/guides/sdk-contract-stability.md)** -- exit criteria for moving Honua.Sdk.* from alpha to beta to stable
- **[API Reference](docs/api/)** -- core SDK API documentation

## License

[Apache 2.0](LICENSE)
