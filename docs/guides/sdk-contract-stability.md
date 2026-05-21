# SDK Contract Stability Roadmap

This is the **consumer-side** view of the `Honua.Sdk.*` .NET package contract: what
"alpha", "beta", and "stable" mean to honua-mobile, and the exit criteria we expect
the SDK to meet before it advances along that track.

> This doc is **not** the SDK's own roadmap. Source-of-truth release planning,
> changelogs, and APIs live in the [`honua-io/honua-sdk-dotnet`](https://github.com/honua-io/honua-sdk-dotnet)
> repository. This doc records what honua-mobile, as a downstream consumer, treats
> as the contract and the bar for advancing it.

## Current state

honua-mobile pins the SDK release train as a single property:

- Pinned version: `0.1.17-alpha.1`
- Pin location: [`Directory.Build.props`](../../Directory.Build.props) line 11
  (`<HonuaSdkDotNetTrainVersion>`)
- Release tag template: `dotnet-sdk-v$(HonuaSdkDotNetTrainVersion)` (line 12)
- Upstream repo: `honua-io/honua-sdk-dotnet` (line 13)
- Cross-repo handshake fixture:
  [`contracts/fixtures/mobile-sdk-contract-harmonization.v1.json`](../../contracts/fixtures/mobile-sdk-contract-harmonization.v1.json)

Mobile pins the **train tag**: a single `HonuaSdkDotNetTrainVersion` property
moves the whole package set together. There is no per-package floating range in
the mobile repo today, and we intend to keep that invariant.

The SDK has cycled through 17+ alpha bumps over the past month
(`0.1.1-alpha.1` through `0.1.17-alpha.1`). PR #187 consumed `0.1.17-alpha.1` and
subsequent PRs (e.g. #190) continued that pin. Each alpha bump has historically
required a mobile-side refactor PR.

## Package surface

The mobile repo currently references the following top-level `Honua.Sdk.*`
packages (from `.csproj` files under `src/`, `apps/`, `tests/`):

| Package | Purpose |
| --- | --- |
| `Honua.Sdk.Abstractions` | Interfaces and DTOs shared across SDK clients (feature, scene, routing, plugins). |
| `Honua.Sdk.Grpc` | gRPC transport and mobile-side request converters. |
| `Honua.Sdk.GeoServices` | ArcGIS-compatible FeatureServer + routing client implementations. |
| `Honua.Sdk.OgcFeatures` | OGC API Features client (models, conversion, exceptions). |
| `Honua.Sdk.Scenes` | 3D scene client and scene-specific exceptions. |
| `Honua.Sdk.Field` | Field forms (`FormDefinition`, `FormField`, `FormFieldType`) and `FieldRecord` types. |
| `Honua.Sdk.Offline.Abstractions` | Interface contracts for the offline sync engine: `IOfflineChangeJournal`, `IOfflineFeatureStore`, `IOfflineSyncCheckpointStore`, `IOfflineSyncStateStore`, `OfflinePackageManifest`. Referenced directly by `Honua.Mobile.Offline` and `Honua.Mobile.Maui`. |
| `Honua.Sdk.Offline` | Offline sync engine, `ReplicaSyncClient`, and the concrete store/journal implementations behind `Honua.Sdk.Offline.Abstractions`. |

Any of these package IDs disappearing, splitting further, or being renamed
constitutes a contract change for the purposes of this doc.

## What "alpha", "beta", "stable" mean here

These definitions are what honua-mobile treats as the contract; they are
intentionally stricter than raw SemVer because alpha versions don't carry
SemVer guarantees on their own.

### alpha (`0.1.x-alpha.N`) — today

- Public API may change in any release, including signature changes, type
  renames, namespace moves, and package splits.
- Mobile must bump `HonuaSdkDotNetTrainVersion` per release; transitive
  refactors in mobile are expected and accepted.
- The harmonization fixture records the exact `0.1.x-alpha.N` mobile is on at a
  given moment.
- No deprecation window. No XML doc completeness guarantee. No back-compat
  promise.

### beta (`0.2.0`)

- Additive changes are allowed within `0.2.x` (new types, new members on
  existing types, new optional parameters via overloads).
- Removals, renames, signature changes, and package splits require either a
  `0.3.0` bump or a deprecation pass that lands at least one minor release
  before the breaking change.
- Mobile updates within `0.2.x` should be a pure version bump in
  `Directory.Build.props` — no compile-error refactors required.

### stable (`1.0.0`)

- Full SemVer in effect. `1.x` is back-compatible; breaking changes wait for
  `2.0.0`.
- Public API removals require a deprecation window of at least one minor
  release (and ideally one full minor cycle) before removal in the next major.
- `[EditorBrowsable(Never)]` and `[Obsolete]` annotations are honored as the
  forward-compatibility contract.

## Exit criteria: `0.1.x-alpha` -> `0.2.0` (beta)

All of the following must hold at the candidate SDK release:

1. **API stability streak.** Two consecutive alpha releases with no breaking
   changes to the public surface of the packages listed above (no removals, no
   renames, no signature changes, no package splits).
2. **Consumer soak.** honua-mobile has consumed the immediately-prior alpha for
   a minimum of 14 days on `main` without a forced refactor PR (i.e. the bump
   PR touched only `Directory.Build.props` and the harmonization fixture).
3. **XML doc coverage.** Every public type and public member in the packages
   listed above carries an XML `<summary>` comment. Verified by enabling
   `GenerateDocumentationFile` and treating CS1591 as an error in the SDK
   build.
4. **Harmonization fixture is whole-shape.** The fixture at
   `contracts/fixtures/mobile-sdk-contract-harmonization.v1.json` validates
   100% of the negotiated contract fields (not only `packageVersion` strings).
   The fixture's `shapeInvariants` block now pins the five wire shapes most
   recently round-tripped against the live Honua server (GeoServices
   `applyEdits` request + response, OGC features collection response, scene
   metadata response, feature attachment info); see
   `Fixture_ShapeInvariants_PinKeyWireShapes` in
   `tests/Honua.Mobile.Sdk.Tests/MobileContractHarmonizationFixtureTests.cs`.
   Advancing to beta requires the remaining family shapes (routing results,
   OGC merge-patch, attachment download envelopes, offline package manifest)
   to be pinned the same way as they round-trip live; the gap is tracked
   under honua-mobile#54.
5. **Live integration tests.** The mobile-side server-integration test project
   passes end-to-end against the candidate release using real transport (gRPC
   + REST), not just unit-level mocks.

That's **5** exit criteria for alpha -> beta.

## Exit criteria: `0.2.0` -> `1.0.0` (stable)

1. **External consumer.** At least one consumer outside `honua-mobile` consumes
   the public packages (a second SDK, a sample app, or a partner integration)
   and has been on the beta train for at least one minor release.
2. **Documented surface.** The SDK ships a `docs/guides/` set in
   `honua-sdk-dotnet` covering each top-level package with at least one
   end-to-end example per package.
3. **No hidden API.** No `[EditorBrowsable(Never)]` and no remaining
   `[Obsolete]` items in the public surface of the listed packages. Anything
   still hidden either becomes private or graduates to documented public API.
4. **Performance budgets.** Documented budgets exist and pass for the two
   hot paths mobile relies on:
   - Streaming feature query throughput
   - `ReplicaSyncClient` initial-package + delta apply

   Specific numeric budgets are **TBD** and tracked in `honua-sdk-dotnet`. This
   doc deliberately does not invent numbers; it requires that the SDK publish
   them and that mobile sign off on them before `1.0.0`.
5. **Deprecation policy published.** `honua-sdk-dotnet` ships a written policy
   matching the "stable" definition above (one-minor deprecation window,
   SemVer-strict majors).

That's **5** exit criteria for beta -> stable.

## Tracking

- The next alpha is cut and tagged in
  [`honua-io/honua-sdk-dotnet`](https://github.com/honua-io/honua-sdk-dotnet).
- Mobile picks up a bump by updating `<HonuaSdkDotNetTrainVersion>` (and the
  associated metadata properties) in
  [`Directory.Build.props`](../../Directory.Build.props), then refreshing
  `contracts/fixtures/mobile-sdk-contract-harmonization.v1.json`.
- Cadence target during alpha: roughly weekly bumps, driven by SDK feature
  work. Cadence target during beta: at most one minor per two weeks. Cadence
  target during stable: at most one minor per month, with patch releases as
  needed.

## What this doc is not

- **Not** the SDK roadmap. The SDK's roadmap, milestones, and release notes
  live in `honua-sdk-dotnet`.
- **Not** a list of features mobile wants. Feature requests belong in
  `mobile-sdk-backlog-roadmap.md` and upstream SDK issues.
- **Not** a binding API specification. The binding contract is the SDK's own
  public surface plus the harmonization fixture; this doc only describes the
  rules under which that contract is allowed to change.
