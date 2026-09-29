# Honua Mobile SDK for .NET

[![CI](https://github.com/honua-io/honua-mobile/actions/workflows/ci.yml/badge.svg?branch=trunk)](https://github.com/honua-io/honua-mobile/actions/workflows/ci.yml)
[![Live Server Integration](https://github.com/honua-io/honua-mobile/actions/workflows/live-server-integration.yml/badge.svg?branch=trunk)](https://github.com/honua-io/honua-mobile/actions/workflows/live-server-integration.yml)
[![OpenSSF Scorecard](https://api.securityscorecards.dev/projects/github.com/honua-io/honua-mobile/badge)](https://scorecard.dev/viewer/?uri=github.com/honua-io/honua-mobile)
[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)

.NET MAUI mobile SDK for [Honua Server](https://github.com/honua-io/honua-server),
the multi-protocol cloud-native geospatial server. It gives .NET mobile developers
an offline-first foundation for field apps: GeoPackage storage, gRPC-first
transport with REST fallback, dynamic field-collection forms, routing, 3D scene
metadata, and connectivity-aware background sync. It also ships
[`@honua-io/embed`](src/Honua.Embed/), a framework-agnostic web component package
for embedding Honua maps and scenes.

This repo is the **SDK** (libraries, reference apps, templates). If you want a
ready-made field data collection **app** built on this SDK, see
[honua-collect](https://github.com/honua-io/honua-collect).

## Status

Pre-1.0, alpha. Package contracts can still change between releases — see the
[SDK Contract Stability Roadmap](docs/guides/sdk-contract-stability.md) for the
alpha → beta → stable exit criteria and
[docs/guides/mobile-sdk-backlog-roadmap.md](docs/guides/mobile-sdk-backlog-roadmap.md)
for the backlog roadmap. The source-backed feature map (what is actually
implemented vs. planned) is in [docs/features/README.md](docs/features/README.md).

## Packages

| Package | Purpose | Published |
|---------|---------|-----------|
| **Honua.Mobile.Sdk** | Transport, auth, gRPC-first client, REST fallback, routing, and SDK scene metadata adapter | Public release target: [nuget.org](https://www.nuget.org/packages/Honua.Mobile.Sdk), from signed `mobile-dotnet-v*` tags |
| **Honua.Mobile.Offline** | GeoPackage storage, sync queue, map area download, conflict resolution | Public release target: [nuget.org](https://www.nuget.org/packages/Honua.Mobile.Offline) |
| **Honua.Mobile.Maui** | MAUI service registration, DI extensions, native display boundaries, native scene anchoring, and device location orchestration | Public release target: [nuget.org](https://www.nuget.org/packages/Honua.Mobile.Maui) |
| **@honua-io/embed** | Framework-agnostic `<honua-map>` and `<honua-scene>` web components (plus React/Vue/Angular wrappers) for ISV embeds | Public release target: [npmjs.com](https://www.npmjs.com/package/@honua-io/embed), from signed `mobile-embed-v*` tags |

Registry links are release evidence, not promises: use a version only when it
appears at the linked public registry. The release workflows fail closed until
that exact version installs anonymously, and then create a checksum-bearing
GitHub Release. See [RELEASING.md](RELEASING.md) for the gates and one-time
credential setup.

The library packages target `net10.0` and build on any platform without the MAUI
workload. The reference apps and templates are .NET MAUI (`net10.0-android`,
plus `net10.0-ios`/`net10.0-maccatalyst` on macOS and `net10.0-windows` on
Windows). Platform-neutral client logic comes from the
[honua-sdk-dotnet](https://github.com/honua-io/honua-sdk-dotnet) `Honua.Sdk.*`
packages, pinned as a single release train in
[`Directory.Build.props`](Directory.Build.props).

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

See [docs/getting-started/](docs/getting-started/) for installation, a full
tutorial, and the field collector project template
([`templates/honua-fieldcollector`](templates/honua-fieldcollector/)), and
[examples/](examples/) for runnable samples (field data collection, embeds,
scenes, AR utility visualization).

## Key Features

### Offline Sync

GeoPackage-backed offline storage with queue-based sync:

- **GeoPackage storage** -- standards-compliant `.gpkg` files (interoperable with QGIS, ArcGIS)
- **Sync queue** -- queued edits with claim/lease semantics to prevent duplicate processing
- **Conflict resolution** -- ClientWins, ServerWins, or ManualReview strategies
- **Background sync** -- connectivity-aware with periodic timer and semaphore gating
- **Map area download** -- offline basemap packages with path traversal protection
- **Delta sync** -- replica-based incremental downloads with cursor persistence
- **Cache governance** -- per-layer TTL eviction and R-tree-backed bbox lookups for replicated features

See [docs/guides/offline-sync.md](docs/guides/offline-sync.md).

### Field Collection

- **SDK-owned contracts** -- `Honua.Sdk.Field` owns form schemas, validation, calculated fields, duplicate detection, and record workflow
- **Mobile capture adapters** -- local media paths stay mobile-owned and convert to portable SDK attachment metadata before sync
- **Validation and workflow DI** -- `AddHonuaMobileFieldCollection()` registers a mobile adapter over SDK field services

### gRPC Transport

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

### Routing

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

### 3D Scene Metadata

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

## Migrating from the ArcGIS Maps SDK for .NET

Moving a MAUI/Xamarin field app off the ArcGIS Maps SDK? See the
[migration guide](docs/guides/migration-arcgis-maps-sdk-maui.md) (API-idiom
mapping table, phased reimplement plan, transition bridge) and the
`honua-migrate-maui` codemod CLI under [tools/](tools/). For
platform-agnostic field-platform migrations (Fulcrum, Survey123, KoBo) see the
[Migration Guide](docs/guides/migration-guide.md).

## Validation Status

| Layer | Status | Run on | Coverage |
| --- | --- | --- | --- |
| Unit | ✅ | every PR | Across the SDK, Offline, FieldCollection, and MAUI .NET projects |
| Integration (in-process loopback) | ✅ | every PR | `Honua.Mobile.ServerIntegration.Tests` against a real ASP.NET Core loopback server |
| Smoke | ✅ | every PR | `Honua.Mobile.Smoke.Tests` (`quality-gates` job) |
| Embed DOM | ✅ | every PR | jsdom suites under `src/Honua.Embed/tests/` |
| Live server (Docker image) | ✅ | every PR via the `Live Server Integration` workflow (hard gate) | `LiveHonuaServerInteractionTests` (incl. unary + server-streaming live gRPC); Testcontainers spins up `honuaio/honua-server:nightly` + PostGIS with the vendored seed at `tests/seed/mobile-offline-demo-v1.sql` |
| Cloud acceptance (staging) | 🟡 | manual `workflow_dispatch` | `DisconnectedFieldWorkflowAcceptanceTests`; production promotion blocked on honua-server#965 |
| Physical device | 🟡 | deferred to GA | AR/VR field workflow follow-ups in [docs/guides/native-scene-anchoring-requirements.md](docs/guides/native-scene-anchoring-requirements.md); emulator/simulator platform smoke covers part of the surface |

Exact test counts are intentionally not pinned here (they drift every PR); the
authoritative numbers are the per-project totals reported by each CI run. See
[docs/guides/validation-strategy.md](docs/guides/validation-strategy.md) for the
per-capability coverage matrix, known gaps, and which CI workflow runs which
bucket, and
[Disconnected Field Workflow Harness](docs/guides/disconnected-field-workflow-harness.md#live-server-integration-workflow)
for the live-server workflow's scope and triggers.

## Repository Structure

```
src/
  Honua.Mobile.Sdk/           Core mobile client: transport, auth, gRPC/REST, routing, scenes
  Honua.Mobile.Offline/       GeoPackage storage, sync engine, conflicts, map download
  Honua.Mobile.Maui/          MAUI platform integration, native display, location, scene anchoring
  Honua.Embed/                @honua-io/embed web component package (tests/ inside)
apps/
  Honua.Mobile.App/           Reference MAUI application
  Honua.Mobile.FieldCollection*/  Field collection reference app + core library
tools/
  Honua.Migrate.Maui*/        ArcGIS Maps SDK -> Honua migration codemod + CLI
templates/                    honua-fieldcollector project template
examples/                     Field collection, embed, scene, and AR samples
tests/                        Sdk / Offline / FieldCollection / Maui / ServerIntegration /
                              Smoke / PlatformSmoke test projects; tests/seed has the vendored seed SQL
contracts/                    Cross-repo SDK contract harmonization fixtures
docs/                         Getting started, guides, feature map, API reference
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
for the core source projects, and runs the `@honua-io/embed` npm build/tests. It
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

Building Android targets requires a configured Android SDK; iOS/Mac Catalyst
targets require macOS with Xcode. The library projects target `net10.0` and
build on any platform without the MAUI workload.

The server integration project starts a real ASP.NET Core loopback server and
exercises the SDK, offline, FieldCollection auth, and mobile exception-reporting
HTTP paths without external infrastructure. Opt-in live Honua image tests run
when `HONUA_MOBILE_LIVE_SERVER_TESTS=1` is set (Testcontainers or a pre-started
Honua URL; see [docs/guides/offline-sync.md](docs/guides/offline-sync.md)). The
smoke test project can also run an optional live Honua query when
`HONUA_MOBILE_SMOKE_BASE_URL`, `HONUA_MOBILE_SMOKE_SERVICE_ID`,
`HONUA_MOBILE_SMOKE_LAYER_ID`, and optionally `HONUA_MOBILE_SMOKE_API_KEY` are
set.

Release workflow, branch-protection, package metadata, Dependabot, Trivy, and
platform smoke guardrails are documented in
[Repo Scaffolding Gates](docs/guides/repo-scaffolding-gates.md).

## Documentation

- **[Getting Started](docs/getting-started/)** -- installation, tutorial, and developer checklist
- **[Guides](docs/guides/)** -- offline sync, security, camera, performance, 3D scenes, migrations, and more
- **[Feature Map](docs/features/README.md)** -- source-backed map of implemented capabilities
- **[SDK Contract Stability Roadmap](docs/guides/sdk-contract-stability.md)** -- exit criteria for moving from alpha to beta to stable
- **[API Reference](docs/api/core.md)** -- core SDK API documentation
- **[Hosted Honua docs](https://honua.gitbook.io/honuaio/)** -- platform-wide documentation

## Related Honua Repositories

| Repo | What it is |
|------|------------|
| [honua-server](https://github.com/honua-io/honua-server) | Flagship multi-protocol geospatial server this SDK talks to |
| [honua-collect](https://github.com/honua-io/honua-collect) | Offline-first field data collection app built on this SDK |
| [honua-sdk-dotnet](https://github.com/honua-io/honua-sdk-dotnet) | Platform-neutral `Honua.Sdk.*` .NET packages this repo consumes |
| [honua-sdk-js](https://github.com/honua-io/honua-sdk-js) | JavaScript/TypeScript SDKs + MCP server |
| [honua-console](https://github.com/honua-io/honua-console) | Unified web console (Studio, Catalog, Operate, Share) |
| [honua-helm](https://github.com/honua-io/honua-helm) | Helm chart for deploying Honua Server on Kubernetes |

## Security

Report vulnerabilities to **security@honua.io** -- see the
[org security policy](https://github.com/honua-io/.github/blob/main/SECURITY.md).
Do not open public issues for security reports.

## License

[Apache 2.0](LICENSE)
