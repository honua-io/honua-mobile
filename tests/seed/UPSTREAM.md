# Vendored Seed: `mobile-offline-demo-v1.sql`

This directory contains a vendored snapshot of the mobile offline fixture
seed SQL that lives upstream in
[`honua-io/honua-server`](https://github.com/honua-io/honua-server) at
`tests/seed/mobile-offline-demo-v1.sql`.

It is consumed by the `Live Server Integration` GitHub Actions workflow
(`.github/workflows/live-server-integration.yml`) and by local runs of
`LiveHonuaServerInteractionTests` -- the fixture loads it into the
PostGIS container before the live Honua server starts, so the
`mobile_offline_demo` service, layers, scene, and routing assets are
registered before any test runs.

## Why vendor it?

The seed needs to be present on every PR-trigger run so the live tests
can be a hard gate (not soft-fail). Fetching it from the upstream repo at
PR time requires a cross-repo token with `honua-server` read scope, which
isn't available to forked-PR workflows. Vendoring sidesteps that problem
at the cost of having to keep it in sync manually -- see the drift-control
section below.

## Current snapshot

| Field | Value |
| --- | --- |
| Upstream path | `tests/seed/mobile-offline-demo-v1.sql` |
| Upstream repo | `honua-io/honua-server` |
| Upstream blob SHA | `3420275023fa3cee45b1a4517ee1ac5cc74e9a55` |
| Upstream `trunk` commit at fetch time | `5c179776929e4b62fdce11a5bc2e1e0369897466` |
| Vendored on | 2026-06-20 |
| Bytes | 14684 |

When updating, refresh every row above (the blob SHA in particular --
that's what the sync script checks against to detect drift).

## Drift control

`tools/sync-mobile-offline-seed.sh` is the only supported way to update
this file:

```bash
# Diff vendored copy against the latest upstream version (no writes).
tools/sync-mobile-offline-seed.sh

# Overwrite the vendored copy and refresh this UPSTREAM.md.
tools/sync-mobile-offline-seed.sh --write
```

The script refuses to overwrite without `--write`, so the default
behaviour in CI or a casual local run is to surface drift as a diff
rather than silently take the upstream version. Commit the refreshed
seed + updated `UPSTREAM.md` together in a single change so the SHA and
the file always move in lockstep.

A scheduled GitHub Actions workflow watches this file for upstream
drift: `.github/workflows/seed-drift-check.yml` runs weekly on Monday
14:00 UTC (and supports `workflow_dispatch`). When the upstream blob
differs from the vendored copy it opens (or comments on) an issue
titled `seed-drift: mobile-offline-demo-v1.sql` and fails the run so the
drift is visible on the Actions page. Refresh via the sync script
above, then close the tracking issue when the new SHA is committed.

## When to update

Update when any of the following change upstream:

- The `mobile_offline_demo` FeatureServer service definition, layers, or
  attribute schema.
- Scene/routing identifiers referenced by `LiveHonuaServerInteractionTests`
  (currently `downtown-honolulu` and `Routing`).
- Seed feature counts that any live test asserts against.

Routine upstream churn that doesn't affect these surfaces can be left
alone -- a stale-but-compatible seed is fine. The sync script's diff
output is the source of truth on whether an update is required.
