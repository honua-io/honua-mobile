# AGENTS.md

## Overview

`honua-mobile` is the **Honua Mobile SDK for .NET** — a .NET MAUI mobile SDK for
[Honua Server](https://github.com/honua-io/honua-server) providing offline-first
field data collection: GeoPackage storage, gRPC-first transport (with REST
fallback), dynamic forms, routing, 3D scene metadata, and background sync. It
also ships `@honua-io/embed`, a framework-agnostic web component package for
embedding maps/scenes.

This repo owns **mobile runtime behavior** and display/app integration. It
consumes reusable platform-neutral logic from the `honua-sdk-dotnet` SDK
(`Honua.Sdk.*` NuGet packages) rather than re-implementing it. See
"Conventions & Gotchas" for the ownership boundary.

## Tech Stack

- **.NET 10** (`net10.0`) — library projects (`Sdk`, `Field`, `Offline`, `Maui`)
  target `net10.0` and build on any platform without the MAUI workload.
- **.NET MAUI** `10.0.70` — app/platform projects (`apps/Honua.Mobile.App`,
  `apps/Honua.Mobile.FieldCollection`). Android targets need a configured
  Android SDK; iOS targets need macOS + Xcode.
- **Honua .NET SDK** `1.0.0` train (`Honua.Sdk.*`), pinned in
  `Directory.Build.props`. Pulled from the private GitHub Packages feed.
- **TypeScript / Vite / Vitest** — `src/Honua.Embed` (`@honua-io/embed`), with
  Cesium, MapLibre, deck.gl; React/Vue/Angular wrappers.
- gRPC + Protobuf for transport; GeoPackage/SQLite for offline storage.

## Setup

Fresh checkouts need read access to the private Honua GitHub Packages feed
(`github-honua` source in `NuGet.config`) for `Honua.Sdk.*` packages:

```bash
gh auth refresh -s read:packages
export HONUA_GITHUB_PACKAGES_USER="$(gh api user --jq .login)"
export HONUA_GITHUB_PACKAGES_TOKEN="$(gh auth token)"
```

`scripts/validate-local.sh` uses a temporary NuGet config built from those
vars and removes it on exit; without them it falls back to existing credentials
configured for the `github-honua` source.

## Commands

Full local validation baseline (restore, build, test, smoke, format, embed
build/tests):

```bash
scripts/validate-local.sh
# options: --configuration <name> --skip-dotnet --skip-format --skip-npm --skip-npm-ci
```

Equivalent manual .NET commands:

```bash
dotnet restore Honua.Mobile.sln
dotnet build   Honua.Mobile.sln
dotnet test    Honua.Mobile.sln
dotnet test tests/Honua.Mobile.Smoke.Tests/Honua.Mobile.Smoke.Tests.csproj
```

Format check (CI runs this on core libraries with warnings-as-errors):

```bash
dotnet format src/Honua.Mobile.Sdk/Honua.Mobile.Sdk.csproj --verify-no-changes
# (also Offline, Field, and equivalent for Maui)
```

Embed web component package (`src/Honua.Embed`):

```bash
npm ci    --prefix src/Honua.Embed
npm run build     --prefix src/Honua.Embed
npm test          --prefix src/Honua.Embed   # vitest run
npm run typecheck --prefix src/Honua.Embed
```

Opt-in test env vars: set `HONUA_MOBILE_LIVE_SERVER_TESTS=1` for live-server
integration tests (Testcontainers / pre-started Honua URL); set
`HONUA_MOBILE_SMOKE_BASE_URL`, `_SERVICE_ID`, `_LAYER_ID`, optionally
`_API_KEY` for the optional smoke live query.

> Do not build/run as part of documentation tasks. The above are for reference.

## Architecture

gRPC-first client with automatic REST fallback. Transport security is enforced
(API keys / bearer tokens never sent over plain HTTP unless
`AllowInsecureTransportForDevelopment` is set). Auth tokens persist via
`IAuthTokenProvider` into iOS Keychain / Android secure storage. Offline storage
is GeoPackage-backed with a queue-based, connectivity-aware sync engine
(ClientWins/ServerWins/ManualReview conflict strategies, delta/replica sync).

Wiring is via DI extension methods (e.g. `AddHonuaMobileSdk`,
`AddHonuaGeoPackageOfflineSync`, `AddHonuaBackgroundSync`,
`AddHonuaMobileFieldCollection`) — see `README.md` Quick Start.

## Directory Layout

```
src/
  Honua.Mobile.Sdk/      Core mobile client: transport, auth, gRPC/REST, routing, scene adapter
  Honua.Mobile.Field/    Mobile adapters over SDK-owned field forms/validation/capture
  Honua.Mobile.Offline/  GeoPackage storage, sync queue, map-area download, conflict resolution
  Honua.Mobile.Maui/     MAUI DI registration, native display, location, scene anchoring
  Honua.Mobile.IoT/      IoT sensor interfaces only — no implementation yet
  Honua.Embed/           @honua-io/embed web component package (TS/Vite); tests in tests/
apps/
  Honua.Mobile.App/                 Reference MAUI application
  Honua.Mobile.FieldCollection*/    Field collection app + core
tests/                   Sdk / Field / FieldCollection / Offline / Maui / ServerIntegration /
                         Smoke / PlatformSmoke test projects; tests/seed has vendored live seed SQL
templates/  examples/    Field collector template and sample apps
scripts/    quality/      Validation/release scripts, store-prereq validators, release checklists
docs/       contracts/    Guides (validation-strategy, offline-sync, sdk-contract-stability), proto/contracts
Directory.Build.props    SDK train version + MauiVersion pins (shared MSBuild props)
NuGet.config             Package sources + source mapping (Honua.* -> github-honua feed)
.github/workflows/       CI (ci.yml, pr-validation.yml), live-server-integration, publish, store dist
```

## Conventions & Gotchas

- **SDK ownership boundary** — this repo owns only mobile/platform-specific
  behavior. Before adding code here, check `honua-sdk-dotnet` first:
  - **Belongs here:** MAUI registration/DI glue, app lifecycle, native storage
    (GeoPackage/SQLite placement & lifecycle), background sync scheduling,
    reachability, permissions, camera/GPS/sensors, field workflow screens &
    capture UX, native/AR/VR map UI, the `@honua/embed` viewer package, and thin
    adapters that translate SDK contracts into mobile runtime behavior.
  - **Does NOT belong here:** new plain `net*` server API clients, provider-
    neutral query/edit/gRPC/OGC/routing/geocoding/catalog/admin/stream/replica
    contracts, scene metadata models, field form schemas/validation engines,
    record workflow rules, shared geometry/spatial-reference primitives.
  - If a class can run without MAUI/DOM/native storage/OS permissions/renderer
    APIs, it likely belongs in the SDK. If it does geometry predicates, WKT/WKB,
    GeoJSON, spatial indexes, or CRS transforms, consume the SDK's
    NetTopologySuite/ProjNet surface instead of adding mobile-local geometry.
- **Consume `Honua.Sdk.*` as versioned NuGet packages.** Do not copy SDK source
  or add long-lived `ProjectReference` links to `honua-sdk-dotnet`; temporary
  local references need an explicit removal issue.
- `src/Honua.Mobile.IoT` is interface-only (no implementation yet).
- CI builds core libraries with **warnings-as-errors** and enforces
  `dotnet format`; run format checks before pushing.
- `Honua.Mobile.IoT`-style migration: files under `Honua.Mobile.Sdk` that are
  server API clients or plain models are migration input for the SDK, not new
  mobile-owned surface.
- The `Live Server Integration` workflow is a hard gate on PRs and pushes to
  `main`; it uses a vendored seed at `tests/seed/mobile-offline-demo-v1.sql`.
- License: Apache 2.0.

## Shared dev-environment rules (multi-agent WSL)

This machine runs many agents concurrently (**Codex + Claude**, often via agentflow with multiple tabs/agents). To prevent host lockups and lost work, every agent MUST follow these:

1. **Heavy builds/tests are throttled by a shared lock.** `dotnet` and `npm` are PATH-shimmed, so their build/test/publish/pack and ci/install/test/run-build/run-test subcommands automatically run under a global semaphore (default 1 concurrent, `HONUA_BUILD_SLOTS`). For other heavy tools, call the wrapper explicitly: `with-build-lock pytest ...`, `with-build-lock cargo build`, `with-build-lock make build`. The lock is shared across ALL of this user's processes (every Codex/Claude tab, agentflow children). Do not bypass it for compiles or test suites. Long-running servers (`dotnet run`, `npm run dev`) are intentionally NOT locked — never wrap those.

2. **Commit and push when you finish a task** so your worktree can be reclaimed. An hourly job (`honua-clean`) removes a worktree ONLY when it is clean AND fully pushed (merged, remote-gone, or idle >=2d). Dirty or unpushed worktrees are NEVER touched — but uncommitted/unpushed work blocks reclamation and is at risk if the instance is reset. Build artifacts (bin/obj and untracked node_modules) are reclaimed automatically and safely.
