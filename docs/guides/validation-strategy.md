# Validation Strategy and Coverage Matrix

This document is the single source of truth for what is tested in the Honua
Mobile SDK (.NET MAUI) repository, at what layer, on which CI workflow, and
which capabilities remain deferred. A reviewer should be able to answer
"what is actually validated and how much should I trust it?" without reading
the test source.

Counts in this document are derived from `[Fact]` and `[Theory]` attributes
present on `main` and may drift between releases; rerun
`find tests -name "*.cs" -exec grep -c "\[Fact\]\|\[Theory\]" {} +` to
re-verify before quoting these numbers in release notes.

## 1. Test Pyramid

The repository validates the SDK in five concentric layers, from cheapest
and most reliable inward to most expensive and most operationally gated:

```
                       Physical device  (0 today, deferred to GA)
                  Cloud acceptance      (7 tests, manual dispatch)
              Live server (Docker)      (11 tests, hard-gated on every PR)
          Server integration            (9 loopback tests + 4 fixture config)
      Unit                              (294 tests across 5 projects)
  Embed DOM                             (npm, src/Honua.Embed/tests/)
```

- **Unit (294 .NET tests)** -- run on every PR via `.github/workflows/ci.yml`
  `test` job. Cover SDK, Offline, Field, FieldCollection, and MAUI library
  surfaces with no network or platform dependencies.
- **Server integration in-process (9 loopback tests + 4 fixture-config
  tests)** -- a live ASP.NET Core loopback server
  (`HonuaIntegrationServer`) exercises SDK, Offline, FieldCollection,
  and exception-reporting HTTP paths via
  `SdkServerIntegrationTests` (3), `OfflineServerIntegrationTests` (3),
  and `FieldCollectionServerIntegrationTests` (3). The same project also
  hosts `LiveHonuaServerFixtureOptionsTests` (4), which validate live
  fixture configuration (env wiring, Testcontainers options) without
  hitting the loopback server. Run on every PR via the same `test`
  job; no external infrastructure required.
- **Live server (Docker, 11 tests)** -- `LiveHonuaServerInteractionTests`
  spin up the official Honua server image via Testcontainers (or attach to
  a pre-started Honua URL) when `HONUA_MOBILE_LIVE_SERVER_TESTS=1` is set.
  The `Live Server Integration` workflow
  (`.github/workflows/live-server-integration.yml`) sets the env var on
  every PR and points it at the vendored seed
  (`tests/seed/mobile-offline-demo-v1.sql`, see `tests/seed/UPSTREAM.md`),
  so the suite is now a hard gate on PRs -- a failing live test blocks the
  merge. When run locally without the env var, the suite reports as
  *Skipped* (Xunit.SkippableFact), not falsely Passed.
- **Cloud acceptance (7 tests)** -- `DisconnectedFieldWorkflowAcceptanceTests`
  run only when `HONUA_MOBILE_CLOUD_ACCEPTANCE=1` is set against a staging
  Honua deployment. Triggered manually via the `Cloud Acceptance` workflow
  (`.github/workflows/cloud-acceptance.yml`, `workflow_dispatch`).
- **Physical device** -- no current automated coverage. AR/VR field
  workflows (honua-server#23, closed; deferred to GA) and physical-device
  iOS/Android smoke runs are documented as follow-ups; emulator-based
  platform smoke (`scripts/run-android-platform-smoke.sh`,
  `scripts/run-ios-platform-smoke.sh`) is the closest current approximation.

Embed DOM tests (`src/Honua.Embed/tests/`, npm) cover the web-component
`<honua-map>` / `<honua-scene>` surfaces and run on every PR via the
`test` job's "Run Embed Component Tests" step.

## 2. Capability Matrix

Cells reference the test file(s) that exercise the capability and the
number of `[Fact]` / `[Theory]` attributes in that file. "N/A" cells are
honest gaps; the parenthesised reason indicates why.

| Capability | Unit | Integration (loopback) | Live (Docker, gated) | Cloud (staging, gated) | Physical device | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Auth (API key, bearer, token provider) | `AuthTokenProviderTests.cs` (12), `MauiAuthRegistrationTests.cs` (4), `AuthenticationServiceTests.cs` (4) | `SdkServerIntegrationTests.cs` (3), `FieldCollectionServerIntegrationTests.cs` (3) | covered by `LiveHonuaServerInteractionTests.cs` (11, env-gated) | N/A (manual) | N/A (deferred) | covered |
| GeoPackage storage | `GeoPackageSyncStoreTests.cs` (17), `GeoPackageSdkOfflineStoreAdapterTests.cs` (6) | `OfflineServerIntegrationTests.cs` (3) | covered by `LiveHonuaServerInteractionTests.cs` | exercised in `DisconnectedFieldWorkflowAcceptanceTests.cs` (7) | N/A (deferred) | covered |
| Sync engine (queue, claim/lease, retry) | `OfflineSyncEngineTests.cs` (7), `HonuaApiOfflineOperationUploaderTests.cs` (10) | `OfflineServerIntegrationTests.cs` (3) | covered by `LiveHonuaServerInteractionTests.cs` | covered by cloud acceptance suite | N/A (deferred) | covered |
| FeatureServer REST | `HonuaMobileClientHttpTests.cs` (17), `HonuaMobileSdkFeatureClientTests.cs` (5) | `SdkServerIntegrationTests.cs` (3) | covered by `LiveHonuaServerInteractionTests.cs` | covered by cloud acceptance suite | N/A (deferred) | covered |
| OGC Features | `HonuaMobileClientHttpTests.cs` (subset of 17) | `SdkServerIntegrationTests.cs` (3) | covered by `LiveHonuaServerInteractionTests.cs` | N/A (manual) | N/A (deferred) | covered |
| gRPC transport | `HonuaMobileClientHttpTests.cs` (subset), `HonuaMobileClientTransportSecurityTests.cs` (7), `grpc-validation` job in `ci.yml` | N/A (loopback uses REST) | `LiveHonuaServerInteractionTests.LiveImage_GrpcFeatureQueryAndEdit_RoundTrip` (unary RPC + ApplyEdits add) and `LiveImage_GrpcFeatureQueryStream_RoundTrip` (server-streaming RPC), both wired against the testcontainers gRPC endpoint (port 8081 / `HONUA_MOBILE_LIVE_SERVER_GRPC_URL`) with `allowRestFallbackOnGrpcFailure: false` | N/A (manual) | N/A (deferred) | unit + live |
| Replica sync (delta, cursors) | `ReplicaSyncClientTests.cs` (12), `DeltaDownloadEngineTests.cs` (7) | N/A (future) | covered by `LiveHonuaServerInteractionTests.cs` | N/A (manual) | N/A (deferred) | unit + live |
| Offline diagnostics / cache governance | `BackgroundPrefetchSchedulerTests.cs` (1), `GeoPackageSyncServiceTests.cs` (15) | N/A (future) | N/A (future) | N/A (manual) | N/A (deferred) | unit-only |
| Field collection workflow | `FormValidatorTests.cs` (8), `RecordWorkflowTests.cs` (3), `FieldWorkflowOfflineAdapterTests.cs` (1), `FieldCollectionSdkContractMigrationTests.cs` (5) | `FieldCollectionServerIntegrationTests.cs` (3) | covered by `LiveHonuaServerInteractionTests.cs` | covered by `DisconnectedFieldWorkflowAcceptanceTests.cs` | N/A (deferred) | covered |
| Map annotations | `HonuaAnnotationLayerTests.cs` (13) | N/A (UI surface) | N/A (UI surface) | N/A (manual) | N/A (deferred) | unit-only |
| Scene metadata | `HonuaSceneClientAdapterTests.cs` (15) | N/A (future) | covered by `LiveHonuaServerInteractionTests.cs` | N/A (manual) | N/A (deferred) | unit + live |
| Scene packages | `HonuaScenePackageManifestTests.cs` (10), `ScenePackageDownloaderTests.cs` (11) | N/A (future) | covered by `LiveHonuaServerInteractionTests.cs` | N/A (manual) | N/A (deferred) | unit + live |
| AR scene anchoring | `HonuaNativeSceneAnchoringTests.cs` (15) | N/A (UI/platform) | N/A (no device harness) | N/A (manual) | N/A (deferred to GA, #23) | unit-only |
| Sync conflict resolution | `OfflineSyncEngineTests.cs` (subset of 7) | N/A (future) | covered by `LiveHonuaServerInteractionTests.cs` | covered by cloud acceptance suite | N/A (deferred) | covered |
| Background sync orchestration | `BackgroundSyncOrchestratorTests.cs` (4), `BackgroundPrefetchSchedulerTests.cs` (1) | N/A (future) | N/A (future) | N/A (manual) | N/A (deferred) | unit-only |
| Connectivity-aware sync | `BackgroundSyncOrchestratorTests.cs` (4, `ToggleConnectivityProvider`) | N/A (future) | N/A (future) | N/A (manual) | N/A (deferred) | unit-only |
| Telemetry redaction | `MobileExceptionRedactorTests.cs` (3), `LocalMobileExceptionReporterTests.cs` (8), `MobileExceptionReportUploadWorkerTests.cs` (7), `MobileExceptionReporterTests.cs` (10), `DiagnosticRedactorTests.cs` (2) | `FieldCollectionServerIntegrationTests.cs` (3) | N/A (future) | N/A (manual) | N/A (deferred) | covered |
| Embed builder / API-key validation | `src/Honua.Embed/tests/honua-builder.test.ts` (npm, multi-suite) | N/A (DOM-only) | N/A (DOM-only) | N/A (manual) | N/A (deferred) | unit-only (npm) |

Other notable test files contributing to the totals but not listed as a
single-capability row above: `MobileContractHarmonizationFixtureTests.cs`
(5) validates `contracts/fixtures/mobile-sdk-contract-harmonization.v1.json`
(including `Fixture_ShapeInvariants_PinKeyWireShapes`, which enforces
sdk-contract-stability.md exit criterion #4);
`MobileBuildConfigurationTests.cs` (7) and `LiveHonuaServerFixtureOptionsTests.cs`
(4) validate harness configuration; `HonuaRoutingClientTests.cs` (9)
covers routing; `HonuaNativeDisplayTests.cs` (4) and
`HonuaDeviceLocationTests.cs` (12) cover native display and device
location boundaries; `HonuaMapPluginHostTests.cs` (9) covers plugin
hosting; `SdkBackedOfflineFieldOperationsDemoHarnessTests.cs` (2),
`SdkOfflineRegistrationTests.cs` (3), `MapAreaDownloaderTests.cs` (4)
cover registration and map-area download. Smoke (`Honua.Mobile.Smoke.Tests`,
18) is run by the `quality-gates` job.

### Count of N/A cells in the capability matrix

There are 18 capability rows by 5 coverage columns (90 cells). Of those,
**55 cells are N/A** (manual, future, deferred, or platform-only), 35
cells carry a concrete test file reference. AR scene anchoring is the
single capability whose deferred status is explicitly tied to a closed
GA-tracking issue (#23).

## 3. Gap Registry

Known coverage gaps, with the owner ticket where one exists:

- **AR physical-device validation** -- deferred to GA per closure of
  honua-io/honua-mobile#23. Per-runtime native AR follow-ups are tracked
  in `docs/guides/native-scene-anchoring-requirements.md` and the
  3D/AR dependency matrix (`docs/guides/mobile-3d-ar-dependency-matrix.md`).
- ~~**Live server (Docker) on every PR**~~ -- closed. The
  `Live Server Integration` workflow now runs
  `LiveHonuaServerInteractionTests` (11 tests) on every PR with the
  vendored seed (`tests/seed/mobile-offline-demo-v1.sql`) loaded into the
  postgres container and the dotnet test step as a hard gate (no
  `continue-on-error`). Adding the workflow to branch-protection required
  checks is a separate maintainer call.
- **gRPC live transport** -- covered.
  `LiveHonuaServerInteractionTests.LiveImage_GrpcFeatureQueryAndEdit_RoundTrip`
  exercises the unary RPC + `ApplyEdits` against the testcontainers
  honua-server image with `preferGrpc: true, allowRestFallbackOnGrpcFailure:
  false`. `LiveImage_GrpcFeatureQueryStream_RoundTrip` exercises the
  server-streaming RPC with `Where`, `OutFields`, and `ReturnGeometry` so
  the live suite covers the mobile query path used by paginated gRPC reads.
- **Background sync and connectivity-aware sync end-to-end** -- only
  unit-tested today (`BackgroundSyncOrchestratorTests.cs`,
  `BackgroundPrefetchSchedulerTests.cs`). No integration or live fixture
  walks the timer + connectivity + queue + server path together.
- **Map annotations and plugin host** -- unit-only; no MAUI UI harness
  yet.
- **Cloud acceptance against production** -- the
  `cloud-acceptance.yml` workflow currently targets
  `staging-api.honua.io` by default and is blocked on production tenancy
  in honua-io/honua-server#965 before it can be promoted to a recurring
  schedule.
- **Embed builder API-key validation in a real browser** -- jsdom-based
  npm tests only; no Playwright or browser-grid coverage yet.

## 4. How CI Surfaces Failures

The local command that most closely mirrors the PR gate is:

```bash
scripts/validate-local.sh
```

For a fresh checkout, configure GitHub Packages credentials first. The mobile
repo consumes versioned `Honua.Sdk.*` packages from the private
`github-honua` NuGet source, so the token must have `read:packages` for the
`honua-io` organization:

```bash
gh auth refresh -s read:packages
export HONUA_GITHUB_PACKAGES_USER="$(gh api user --jq .login)"
export HONUA_GITHUB_PACKAGES_TOKEN="$(gh auth token)"
scripts/validate-local.sh
```

The script writes those credentials to a temporary NuGet config only for the
duration of the run. It then restores and builds `Honua.Mobile.sln`, runs
`dotnet test Honua.Mobile.sln`, runs `Honua.Mobile.Smoke.Tests`, verifies
format for the core source projects, and runs `npm ci`, `npm run build`, and
`npm test` for `src/Honua.Embed`. Live server, cloud, Android emulator, and iOS
simulator coverage still use their existing environment variables and hosted
runner workflows described below.

The relevant workflows under `.github/workflows/`:

- `ci.yml` -- the main PR and trunk gate. Jobs and the test buckets
  they run:
  - `build` -- compiles `Honua.Mobile.Sdk` and `Honua.Mobile.Offline`
    with `TreatWarningsAsErrors`, plus
    `npm run build` for the embed package; runs `dotnet format` checks.
  - `test` -- runs the 5 unit projects (294 tests) plus the
    `Honua.Mobile.ServerIntegration.Tests` project; the 11
    `LiveHonuaServerInteractionTests` report as **Skipped**
    (`Xunit.SkippableFact`) here because `HONUA_MOBILE_LIVE_SERVER_TESTS`
    is unset on the unit-test job. They run for real (and as a hard gate)
    in the `Live Server Integration` workflow below.
  - `npm test` runs for the embed package on the same job.
  - `maui-android`, `maui-ios`, `maui-windows` -- platform builds plus
    Android emulator and iOS simulator platform smoke
    (`scripts/run-android-platform-smoke.sh`,
    `scripts/run-ios-platform-smoke.sh`); skipped when no source
    changes.
  - `grpc-validation` -- proto sanity check and an SDK build
    integration step.
  - `security` -- Trivy filesystem scan plus CodeQL on the three core
    library projects.
  - `quality-gates` -- runs `Honua.Mobile.Smoke.Tests` (18 tests) and
    validates assembly-size budgets against
    `quality/performance-budget.json`.
  - `ci-gate` -- aggregates the above; required for merge.
- `cloud-acceptance.yml` -- `workflow_dispatch` only. Runs
  `DisconnectedFieldWorkflowAcceptanceTests` (7 tests) against a cloud
  Honua URL provided as workflow inputs.
- `seed-drift-check.yml` -- scheduled weekly (Monday 14:00 UTC, plus
  `workflow_dispatch`). Compares `tests/seed/mobile-offline-demo-v1.sql`
  against the upstream copy in `honua-io/honua-server` and, on drift,
  opens or comments on a `seed-drift: mobile-offline-demo-v1.sql` issue
  and fails the run. Not a PR gate -- a red scheduled run on the Actions
  page is the signal to refresh the vendored seed via
  `tools/sync-mobile-offline-seed.sh --write`.
- `pr-validation.yml` -- non-test PR hygiene checks (title, body,
  metadata).
- `android-debug-apk.yml`, `android-internal-distribution.yml`,
  `ios-testflight.yml`, `mobile-production-promotion.yml`,
  `publish-dotnet-mobile.yml`, `publish-npm-embed.yml` -- distribution
  and release workflows, not test gates.

A red box on the PR usually means the `test`, `quality-gates`, or one of
the platform build jobs failed; click into the job, find the named
`Run X Tests` step, and the failing TRX is uploaded under the
`test-results` artifact for 7 days.

## 5. What This Document Is Not

This is the validation story for the **.NET MAUI** Honua Mobile SDK
contained in this repository. Cross-language Honua SDKs (server
contract harness, JS/TS embed beyond the bundled web component,
Python, etc.) are validated in their own repositories and are not
described here. Contract harmonization between the mobile SDK and the
server is validated only at the fixture level
(`contracts/fixtures/mobile-sdk-contract-harmonization.v1.json`); the
authoritative cross-language contract suite lives in `honua-server`
and is referenced from `docs/guides/mobile-contract-harmonization.md`.
