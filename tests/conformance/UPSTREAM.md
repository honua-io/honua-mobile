# Shared conformance fixtures: `geospatial-grpc` Compatibility Train

This directory wires the `Live Server Integration` suite into the **Compatibility
Train** (epic [`geospatial-grpc#18`](https://github.com/honua-io/geospatial-grpc/issues/18)).
Rather than asserting ad-hoc payload shapes, `LiveHonuaServerInteractionTests`
validates live `honua-server` FeatureServer/OGC responses against the **shared,
versioned, published conformance fixtures** owned by
[`honua-io/geospatial-grpc`](https://github.com/honua-io/geospatial-grpc)
(delivered by `geospatial-grpc#19`), so a contract regression like
[`honua-server#1238`](https://github.com/honua-io/honua-server/issues/1238) is
attributed to a specific named `geospatial.v1` contract instead of surfacing as
an opaque HTTP 400/500.

The fixtures are **not vendored** into this repo. CI pulls the pinned version at
run time with `tests/conformance/fetch-fixtures.sh` (a verbatim copy of the
downstream helper published by `geospatial-grpc#19`). This is the same
"pin + pull a released artifact" model the SDK train uses, mirroring how
`tests/seed/UPSTREAM.md` records the vendored seed's provenance.

## Pinned fixture version

| Field | Value |
| --- | --- |
| Fixture version | `0.1.0-alpha.1` |
| Pinned in | `Directory.Build.props` → `<HonuaConformanceFixturesVersion>` |
| Upstream repo | `honua-io/geospatial-grpc` |
| GitHub Release / tag | `v0.1.0-alpha.1` |
| Release commit | `216a00193c36822aa980d2b371f7d9eef06a176b` |
| Release published | `2026-05-30T22:00:50Z` |
| Tarball asset | `conformance-fixtures-0.1.0-alpha.1.tar.gz` |
| Tarball SHA-256 | `e43deb8831dc8e4dc570b5b339ecb7619240b8bdaa439704cdf7f44bb0826a17` |
| Schema mapping | fixture version maps **1:1** to the `geospatial.v1` schema at tag `v0.1.0-alpha.1` (enforced upstream by `conformance/check-version.sh` == `Geospatial.Grpc.csproj <Version>`) |

The fixture version is a **pin, not "latest"**, so the live suite is
deterministic and a fixture set unambiguously corresponds to one
`geospatial.v1` schema release.

## How the suite consumes the fixtures

1. **Fetch (CI / local).** `tests/conformance/fetch-fixtures.sh --version
   <X.Y.Z> [--dest DIR]` downloads the release asset
   `conformance-fixtures-<version>.tar.gz` (+ `.sha256`) from the `v<version>`
   GitHub Release of `honua-io/geospatial-grpc` (via `gh release download` when
   available, else `curl`/`wget`), verifies the tarball SHA-256, extracts it,
   re-verifies every file against the in-tarball `SHA256SUMS`, and asserts the
   embedded `VERSION` equals the requested pin. It leaves `fixtures/`
   (+ `manifest.txt`), `golden/`, `run.sh`, and `VERSION` in `--dest`
   (default `./conformance-fixtures-<version>/`).

   Equivalent raw pull (no helper):

   ```bash
   v=0.1.0-alpha.1
   base="https://github.com/honua-io/geospatial-grpc/releases/download/v${v}"
   curl -fsSLO "${base}/conformance-fixtures-${v}.tar.gz"
   curl -fsSLO "${base}/conformance-fixtures-${v}.tar.gz.sha256"
   sha256sum -c "conformance-fixtures-${v}.tar.gz.sha256"
   tar -xzf "conformance-fixtures-${v}.tar.gz"
   ```

2. **Locate (tests).** `LiveHonuaServerInteractionTests` discovers the fetched
   fixture directory via the `HONUA_MOBILE_CONFORMANCE_FIXTURES_DIR` environment
   variable (set by the workflow to the `--dest` directory). When unset, the
   contract-conformance assertions are skipped (the rest of the live suite still
   runs), so a developer who has not fetched the fixtures is not blocked.

3. **Assert (tests).** The mobile-relevant subset — `FeatureService` query /
   apply-edits and the OGC Features read/CRUD paths the suite exercises against
   the `mobile_offline_demo` seed (layer `68910`) — is validated against the
   canonical `feature_query_response` / `feature_apply_edits_response` fixtures
   using the protobuf JSON mapping conventions documented in
   `geospatial-grpc/conformance/README.md` (camelCase fields, enum names as
   strings, `int64` as strings). See
   `tests/Honua.Mobile.ServerIntegration.Tests/ConformanceFixtures.cs`.

## When a contract drifts

A live response that no longer conforms fails the suite with a message that
**names the drifted contract and the specific field** — e.g.
`FeatureService.QueryFeatures response contract drift: …`. The
[`honua-server#1238`](https://github.com/honua-io/honua-server/issues/1238)
class of regression (JSONB-attribute projection emitting bare SQL columns →
FeatureServer 400 / OGC 500) reads as a named contract failure rather than a
bare HTTP error.

Some `honua-server:nightly` gaps are **already tracked** and are marked
known-expected-failing (xfail) in the suite, with an explicit issue reference,
so the job stays green while the harness is wired — but any **new / untracked**
contract drift still fails. When a tracked server fix lands, flip the xfail to
required (see `KnownServerGaps` in the test source). Currently tracked:

| Issue | Contract surface |
| --- | --- |
| [`honua-server#1238`](https://github.com/honua-io/honua-server/issues/1238) | FeatureServer/OGC JSONB-attribute projection (layer `68910`) |
| [`honua-server#1166`](https://github.com/honua-io/honua-server/issues/1166) | Temporal query/filter |
| [`honua-server#1167`](https://github.com/honua-io/honua-server/issues/1167) | Replica sync |
| [`honua-server#1237`](https://github.com/honua-io/honua-server/issues/1237) | Analysis list/estimate |

## Updating the pin

To adopt a newer fixture release:

1. Bump `<HonuaConformanceFixturesVersion>` in `Directory.Build.props`.
2. Refresh the **Pinned fixture version** table above (release commit, publish
   timestamp, tarball SHA-256 — grab them with
   `gh release view v<version> --repo honua-io/geospatial-grpc --json …`).
3. Re-run the `Live Server Integration` workflow and reconcile any new contract
   diffs (a real server fix may let you flip a tracked xfail to required).

Commit the version bump and this doc together so the pin and its provenance
always move in lockstep.
