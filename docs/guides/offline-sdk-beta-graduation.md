# Offline Field SDK Beta Graduation Criteria

This document defines the graduation gate for the **.NET MAUI mobile SDK +
offline sync** Beta label. It is the agreed acceptance bar that must be met
before `interoperability.html` may show offline field collection as GA (or
before the Beta caveat may be removed from honua.io copy).

It exists because the site has a labeling rule (`claims.html`): features in
Beta must be labeled as such. Without a defined graduation gate the team cannot
know when the label may change. This document is that gate; it is intended to be
linked from honua-mobile#92 and honua-mobile#1 as the graduation checklist.

## Scope

In scope for this gate:

- The MAUI mobile SDK offline-first runtime: GeoPackage storage, the queue-based
  sync engine, conflict resolution, background/connectivity-aware sync, and the
  SDK-backed registration path (`AddHonuaSdkGeoPackageOfflineSync`).
- The disconnected field workflow round-trip (download -> disconnect -> edit ->
  reconnect -> push) and its conflict review path.

Out of scope (these do not gate offline-sync graduation, and have their own
bars):

- AR / 3D scene anchoring (deferred to GA per honua-mobile#23).
- Other mobile platform tracks (Flutter, native iOS/Android) tracked in
  `mobile-platform-parity-tracks.md`.
- The `@honua-io/embed` web component package.
- Implementing new offline features (tracked in honua-mobile#92 and
  honua-mobile#1, not here).

## How To Read This Gate

Every criterion below is **objective and checkable**. Where a criterion is
backed by an automated test or CI workflow, the evidence is the green run; where
it is backed by a manual check, the evidence is a dated comment on the
graduation issue. The release owner records the result of each criterion before
proposing the label change.

The current validated/deferred state of every capability referenced here is
maintained in [Validation Strategy](validation-strategy.md); this document sets
the exit bar, that document reports where coverage stands today.

## 1. Functional Completeness

All of the following must be implemented, documented, and on the tested
integration path (not behind an experimental flag):

- [ ] **Round-trip workflow** — online download, logical disconnect, offline
  edit (create/update/delete), reconnect, and queued push complete end to end
  via the documented public API. The four phases (`online-download`,
  `offline-edit`, `reconnect-sync`, `verify`) of the disconnected field workflow
  are all implemented and pass. See
  [Disconnected Field Workflow Harness](disconnected-field-workflow-harness.md).
- [ ] **SDK-backed registration is the documented path** —
  `AddHonuaSdkGeoPackageOfflineSync` is the integration entry point used by docs,
  samples, and the demo harness. The lower-level
  `AddHonuaGeoPackageOfflineSync` remains supported for existing apps but is not
  the recommended path for new work. See [Offline Sync](offline-sync.md).
- [ ] **Conflict review path works end to end** on at least one platform (iOS or
  Android): a `ManualReview` conflict is surfaced, an operator decision is
  recorded, and the resolution is pushed. The default engine strategy remains
  `ManualReview` so conflicting edits are never silently overwritten.
- [ ] **Durable queue across process restart** — pending edits persist in the
  device GeoPackage and resume after app restart and after connectivity is
  restored, with claim/lease semantics that do not double-apply edits.
- [ ] **Connectivity-aware scheduling** — background sync gates upload/download
  on `IConnectivityStateProvider` and does not claim queue rows while offline.
- [ ] **Sync telemetry is emitted** — `mobile_sync_runs_total`,
  `mobile_sync_conflicts_total`, and `mobile_pending_operations` are published
  via the `Honua.Mobile.Sync` meter, and upload failures are mapped to
  user-facing reasons (raw gRPC/SQLite exception names kept only as inner
  diagnostics).

## 2. Test Coverage Bar

The graduation bar requires coverage at multiple layers of the test pyramid, not
just unit tests. The structure and current counts are tracked in
[Validation Strategy](validation-strategy.md); the bar to graduate is:

- [ ] **Unit** — sync engine (queue claim/lease, retry), GeoPackage store, and
  conflict policy rules are covered by green unit tests on `trunk`. No
  offline-sync capability in the capability matrix is `N/A` solely because a
  cheap unit test was never written.
- [ ] **In-process integration (loopback)** — the offline server-integration
  tests exercise download, queued offline edit, and reconnect upload against the
  loopback Honua server on every PR.
- [ ] **Live server (Docker), hard-gated** — the `Live Server Integration`
  workflow runs against the official Honua server image with the vendored seed
  (`tests/seed/mobile-offline-demo-v1.sql`) and is a **hard merge gate** (no
  `continue-on-error`). The offline replica-sync and edit round-trip paths are
  among the live-covered capabilities. This gate is green on the graduating
  build.
- [ ] **Cloud acceptance (staging)** — `DisconnectedFieldWorkflowAcceptanceTests`
  pass against a staging Honua deployment via the `Cloud Acceptance` workflow,
  producing an evidence artifact
  (`honua.mobile.disconnected-field-workflow.evidence.v1`) with all four phases
  `passed`. This run is captured (run id + artifact link) on the graduation
  issue as graduation evidence.
- [ ] **Seed not drifted** — the weekly `seed-drift-check` workflow is green
  (the vendored seed matches upstream `honua-server`) at graduation time, so the
  live coverage reflects the real server schema.

A graduation candidate may not rely on a loopback stub to claim live coverage; a
live or cloud test that fails when the server route is missing is required for
each round-trip claim.

## 3. Performance And Reliability Targets

Targets are enforced by the `quality-gates` job against
`quality/performance-budget.json`. To graduate, the graduating build must be at
or under the **warning** threshold (not merely under the error threshold) for
each budget below, measured on the platform-smoke runners:

| Metric | Budget key | Warning target |
| --- | --- | --- |
| GeoPackage store init | `geopackage_init_ms` | <= 200 ms |
| Sync batch of 50 operations | `sync_batch_50_ms` | <= 5000 ms |
| Feature query returning 100 results | `feature_query_100_ms` | <= 500 ms |
| Offline package size (`Honua.Mobile.Offline`) | `sdk_package_size_kb` | <= 512 KB |

Reliability:

- [ ] **Round-trip determinism** — the disconnected field workflow harness is
  deterministic: repeated runs of the same fixture produce the same
  create/update/delete outcome and the same drained-queue end state. No flaky
  retries are required to make the live or cloud gate pass.
- [ ] **No data loss on interruption** — process kill, connectivity loss, and
  app suspend during a sync cycle leave the queue in a recoverable state (no
  silently dropped or double-applied edits). This is exercised by the queue
  claim/lease and connectivity tests.
- [ ] **Lifecycle cancellation** — prefetch and download work observes
  `Suspend` / `LowMemory` lifecycle cancellation before the OS reclaims
  resources.

## 4. Documentation And Support Readiness

- [ ] [Offline Sync](offline-sync.md) documents the SDK-backed registration,
  conflict policy, telemetry, and troubleshooting for the shipping API surface.
- [ ] [Disconnected Field Workflow Harness](disconnected-field-workflow-harness.md)
  is current and runnable.
- [ ] The [Mobile Beta Feedback Loop](mobile-beta-feedback-loop.md) intake path
  is active so field defects found post-graduation have a triage home.
- [ ] Known limitations that remain at GA are documented (see the exit list
  below); nothing in the graduating scope is silently broken.

## 5. Known-Gaps Exit List

A capability may be **deferred past graduation** only if it appears on this list
with an explicit owner and a stated reason, and it is not on the round-trip
critical path. Anything not on this list and not met **blocks** graduation.

Permitted-to-defer at offline-sync graduation:

- **AR / native scene anchoring physical-device validation** — deferred to GA
  per honua-mobile#23; unit-tested only. Not on the offline-sync critical path.
- **Background + connectivity-aware sync end-to-end fixture** — may remain
  unit-only at graduation *if* the live or cloud round-trip already exercises the
  same queue + reconnect path; the dedicated timer+connectivity+queue+server
  integration fixture is a tracked follow-up, not a blocker.
- **Map annotations / plugin host UI harness** — unit-only; not part of offline
  sync.
- **Embed builder API-key validation in a real browser** — jsdom-only; out of
  scope for the mobile SDK gate.

Must NOT be on the exit list (these are hard blockers if unmet):

- The download -> disconnect -> edit -> reconnect -> push round trip.
- The manual-review conflict path on at least one platform.
- The live-server (Docker) hard gate being green.
- The performance budgets in Section 3.

## 6. Open-Defect Bar

- [ ] **No P0/P1 bug open against offline sync for more than 14 days** at the
  time of graduation, and none open at all that affect the round-trip critical
  path or the conflict review path.
- [ ] Any P2/P3 offline-sync defects that remain open are either on the
  Known-Gaps Exit List (Section 5) or carry a dated triage note.

## 7. Sign-Off Owners

Graduation requires sign-off recorded as dated comments on the graduation issue
from each of the following roles:

| Role | Confirms |
| --- | --- |
| Mobile SDK maintainer / release owner | Sections 1, 4, 6 — functional completeness, docs, and the open-defect bar are met; live and cloud evidence links are attached. |
| QA / validation owner | Section 2 — the test pyramid bar is met and the live-server hard gate plus cloud acceptance are green on the graduating build. |
| Performance owner | Section 3 — all budgets are at or under their warning thresholds on the platform-smoke runners. |
| Product / site owner | Section 5 is acceptable for GA, and authorizes the `interoperability.html` label change from Beta to GA. |

When all four sign-offs are recorded and every non-deferred checkbox above is
checked, the offline field SDK graduates from Beta. The final mechanical step is
to update `interoperability.html` so the offline label reflects GA (or, if any
criterion is not yet met, to correct the check-mark copy to accurately show Beta
until graduation).

## Related Documentation

- [Validation Strategy](validation-strategy.md)
- [Offline Sync](offline-sync.md)
- [Disconnected Field Workflow Harness](disconnected-field-workflow-harness.md)
- [Mobile Beta Feedback Loop](mobile-beta-feedback-loop.md)
- [Mobile Release Promotion](mobile-release-promotion.md)
- [Mobile SDK Backlog Roadmap](mobile-sdk-backlog-roadmap.md)
