# Mobile SDK Backlog Roadmap

Last reviewed: 2026-05-24.

This roadmap integrates the remaining non-Flutter backlog for #1, the mobile SDK
epic. The historical child issues #12, #16, #23, #38, #42, #50, #51, and #57
are now closed; #10 remains open for embeddable map packaging. The current open
mobile-owned parity items are #92 for final hosted cloud acceptance and #225 for
AR/3D GA physical-device validation. Mobile DevOps/store-release work remains
tracked separately in #82 and #85. This roadmap intentionally excludes #22,
which owns Flutter and broader platform parity expansion.

This page is a sequencing and closure matrix. It also indexes the current
Fulcrum/Survey123 parity backlog. It does not replace the detailed
source-of-truth documents for implemented capabilities, validation coverage,
contracts, 3D/AR, offline packaging, protected scene auth, or display
implementation.

## Source Documents

| Area | Source |
|------|--------|
| Current implemented capability map | [Feature Map](../features/README.md) |
| Current validation and coverage map | [Validation Strategy](validation-strategy.md) |
| Historical Phase 0 parity, innovation, and test baseline | [Phase 0 Summary](../phase-0/PHASE_0_SUMMARY.md) |
| SDK/mobile contract ownership | [Mobile Contract Harmonization](mobile-contract-harmonization.md) |
| 3D, scene, and AR dependency order | [Mobile 3D and AR Dependency Matrix](mobile-3d-ar-dependency-matrix.md) |
| Offline 3D package policy | [Offline 3D Scene Packages](offline-3d-scene-packages.md) |
| Protected scene auth handoff | [Protected 3D Scene Auth](protected-3d-scene-auth.md) |
| Web scene rendering surface | [3D Scene Embed](3d-scene-embed.md) |
| Web map embedding surface | [Embeddable Map](embeddable-map.md) |

## Status Vocabulary

Use these labels consistently when comparing Honua Mobile to Fulcrum,
Survey123, or other field collection platforms:

| Status | Meaning | Source of truth |
|--------|---------|-----------------|
| Implemented | Source exists in this repository and belongs in mobile. | [Feature Map](../features/README.md) plus `src/`, `apps/`, `examples/`, and `templates/`. |
| Validated | Automated tests or CI jobs exercise the behavior. | [Validation Strategy](validation-strategy.md), `tests/`, `src/Honua.Embed/tests/`, and CI job status. |
| Backlog | Product parity need is identified but not fully implemented or validated. | The backlog index below and linked GitHub issues. |
| Cross-repo dependency | Required contracts or server behavior belong outside this repo. | Linked `honua-sdk-dotnet` or `honua-server` issues and dependency docs. |
| Historical baseline | Phase 0 planning claim or target, not shipped status by itself. | `docs/phase-0/*` with the status notes at the top of each document. |

## Epic State

| Layer | Status | How it affects #1 |
|-------|--------|-------------------|
| Phase 0 foundation | Complete as a planning baseline. | Keeps #1 anchored to parity, innovation, and test gates instead of reopening broad discovery. |
| SDK contract alignment | Baseline documented and partially migrated to published `Honua.Sdk.*` packages. | New portable contracts should land in `honua-sdk-dotnet`; mobile should stay limited to adapters, DI, native storage, renderer integration, and lifecycle behavior. |
| Offline mobile runtime | Mobile-owned runtime behavior is established around GeoPackage/SQLite, queueing, file placement, app lifecycle, local package intake, assignment packets, lifecycle transitions, and export evidence. | Remaining work should extend the runtime through SDK contracts instead of adding provider-neutral clients here. #92 stays open only for hosted cloud acceptance after honua-server#965. |
| Display and embed | Web display adapter and native .NET evaluation slices are closed; embeddable map product packaging remains #10. | #10 should close over the closed #50 adapter foundation without reopening #57 native display evaluation. |
| 3D, offline scene, and AR/VR | Scene, browser offline cache, and anchoring decision slices are closed; first AR/3D workflow GA validation remains #225. | #225 should attach ARCore/ARKit adapter and physical-device evidence before GA closure. |
| Field location behavior | Geofencing acquisition/background location is closed as a mobile-owned runtime slice. | Future geofence work should continue consuming SDK contracts while mobile owns permissions, sensors, background behavior, and battery policy. |
| Plugins | Mobile/web host runtime scope is closed; non-UI manifests and permission contracts remain shared SDK/server concerns. | Server plugin APIs remain tracked in honua-io/honua-server#347 and should not be redefined in mobile. |

## Fulcrum/Survey123 Parity Backlog Index

The focused competitive parity sprint is no longer an open execution backlog.
The local/no-cloud and no-design slices are closed, including #208 through
#224, #226, and the no-cloud child backlog #249 through #258. Those closures
cover local form runtime, validation, rules/calculations, repeat fixtures,
metadata-driven project/form loading, local package import/download, assignment
packets, lifecycle transitions, media metadata/export, conflict replay, device
diagnostics, sync health, and the field-day acceptance harness.

| Status | Issues | Closure signal |
|--------|--------|----------------|
| Closed local parity sprint | [#208](https://github.com/honua-io/honua-mobile/issues/208)-[#224](https://github.com/honua-io/honua-mobile/issues/224), [#226](https://github.com/honua-io/honua-mobile/issues/226), [#249](https://github.com/honua-io/honua-mobile/issues/249)-[#258](https://github.com/honua-io/honua-mobile/issues/258) | Source-backed docs, green validation, functional FieldCollection workflows, local package/catalog/assignment/lifecycle/export coverage, and no-cloud acceptance evidence are in place. |
| Open cloud acceptance | [#92](https://github.com/honua-io/honua-mobile/issues/92), blocked by [honua-server#965](https://github.com/honua-io/honua-server/issues/965) | Re-dispatch `Cloud Acceptance` on `main` after `staging-api.honua.io` DNS/TLS is fixed, then attach the uploaded `cloud-acceptance-evidence` artifact to #92. |
| Open AR/3D GA | [#225](https://github.com/honua-io/honua-mobile/issues/225) | Add ARCore/ARKit adapter evidence and physical-device validation for the first field workflow. |
| Outside mobile/no-cloud scope | hosted designer/admin, supervisor review, hosted reports, tenancy, RBAC, audit, and hosted package catalog/publish | Owned by `honua-server`, admin UI, and SDK packages; mobile consumes published contracts through adapters, DI, local cache, UX, and tests. |

Closed local parity issue links:
[#208](https://github.com/honua-io/honua-mobile/issues/208),
[#209](https://github.com/honua-io/honua-mobile/issues/209),
[#210](https://github.com/honua-io/honua-mobile/issues/210),
[#211](https://github.com/honua-io/honua-mobile/issues/211),
[#212](https://github.com/honua-io/honua-mobile/issues/212),
[#213](https://github.com/honua-io/honua-mobile/issues/213),
[#214](https://github.com/honua-io/honua-mobile/issues/214),
[#215](https://github.com/honua-io/honua-mobile/issues/215),
[#216](https://github.com/honua-io/honua-mobile/issues/216),
[#217](https://github.com/honua-io/honua-mobile/issues/217),
[#218](https://github.com/honua-io/honua-mobile/issues/218),
[#219](https://github.com/honua-io/honua-mobile/issues/219),
[#220](https://github.com/honua-io/honua-mobile/issues/220),
[#221](https://github.com/honua-io/honua-mobile/issues/221),
[#222](https://github.com/honua-io/honua-mobile/issues/222),
[#223](https://github.com/honua-io/honua-mobile/issues/223),
[#224](https://github.com/honua-io/honua-mobile/issues/224), and
[#226](https://github.com/honua-io/honua-mobile/issues/226).

## Back-Office Dependency Handoff

#219 is a dependency-tracking issue, not a request to add server/admin clients to
this repository. Fulcrum/Survey123 parity requires project/form administration,
supervisor review, exports, reports, tenancy, permissions, and audit behavior,
but those capabilities are owned by `honua-server`, admin UI, and SDK packages.
Mobile should consume the resulting `Honua.Sdk.*` contracts through adapters,
DI, local cache integration, UX, and tests only.

| Back-office capability | Owning issue outside mobile | Mobile consumption boundary |
|------------------------|-----------------------------|-----------------------------|
| Project, layer, map-area, and form administration | [honua-server#1158](https://github.com/honua-io/honua-server/issues/1158) | `FieldCollectionMetadataService`, project/layer/form selectors, sync setup, and local cache migration consume published metadata contracts. Mobile must not define long-lived admin DTOs or provider-neutral project/form clients. |
| Submitted-record review, QA, correction requests, and approvals | [honua-server#1159](https://github.com/honua-io/honua-server/issues/1159) | Mobile may show review/status messages or correction prompts after SDK contracts exist. Review queues, approval rules, comments, and supervisor workflows remain server/admin-owned. |
| Back-office exports and report packages | [honua-server#1160](https://github.com/honua-io/honua-server/issues/1160) | #220 covers device-local support export. Server/admin owns supervisory exports, scheduled reports, report templates, and export authorization. Mobile may link to availability/status through SDK contracts. |
| SSO/OIDC identity policy | [honua-server#348](https://github.com/honua-io/honua-server/issues/348) | Mobile owns secure storage, token refresh orchestration, re-auth UX, and sync blocking. Identity providers, token policy, SCIM/SAML, and tenant identity configuration remain server/admin-owned. |
| Multi-tenancy and tenant isolation | [honua-server#346](https://github.com/honua-io/honua-server/issues/346) | Mobile stores tenant-scoped cache/session state only after SDK/server identifiers exist. Tenant provisioning, schema/data isolation, scoped keys, usage metering, and admin tenancy UI remain server-owned. |
| Permissions and RBAC | [honua-server#349](https://github.com/honua-io/honua-server/issues/349) | Mobile presents authorization failures, disables unavailable actions, and preserves offline edits when permission checks fail. Role definitions, layer/field permissions, operation checks, and policy evaluation remain server-owned. |
| Audit logs and SIEM export | [honua-server#350](https://github.com/honua-io/honua-server/issues/350), [honua-server#507](https://github.com/honua-io/honua-server/issues/507), [honua-server#509](https://github.com/honua-io/honua-server/issues/509) | Mobile may attach device/session/sync context to API calls and diagnostics. Immutable audit storage, audit coverage, retention, operator access, and SIEM export remain server-owned. |

Mobile-owned follow-ups should be opened only after these owners expose stable
SDK/server contracts. Acceptable mobile follow-ups include adapter wiring, MAUI
registration, local cache lifecycle, field UX, diagnostic presentation, and
contract/acceptance tests.

## Acceptance Matrix

| Issue | Role in #1 | Closure criteria | Dependencies and source docs | Disposition |
|-------|------------|------------------|------------------------------|-------------|
| #10 Embeddable map component | Beta product/API packaging for a white-label `<honua-map>` integration surface. | Drop-in component exposes theming, camera/options, events, auth/cache boundaries, and a working sample over the approved display adapter. | Depends on #50 for web display architecture; see [Embeddable Map](embeddable-map.md). | Current slice. Keep product packaging separate from #50's adapter internals. |
| #12 3D / Scene services | GA umbrella for 3D visualization, terrain, building layers, CesiumJS, and related scene service capability. | Close only after server 3D serving/registry/terrain/elevation/generation/I3S decisions and client SDK/render/offline hooks are complete or explicitly split into follow-up epics. | Depends on honua-io/honua-server#837 through #844, SDK scene contracts, #42, #38, and #23; see [Mobile 3D and AR Dependency Matrix](mobile-3d-ar-dependency-matrix.md). | Closed. Remaining AR/3D GA evidence is tracked by #225. |
| #16 Plugin client SDK | GA host/runtime plugin framework for mobile and web. | Hosts can load/register approved plugins, surface UI extension points, enforce sandbox/signing/permission rules, and consume shared non-UI manifests from SDK/server contracts. | Consumes SDK-owned plugin manifests from `Honua.Sdk.Abstractions`; server plugin API dependency remains honua-io/honua-server#347. See [Mobile Contract Harmonization](mobile-contract-harmonization.md). | Closed for mobile/web host runtime scope; server plugin behavior remains outside this repo. |
| #23 AR/VR field workflow enablement | GA field overlay workflow over scene, device pose, camera, and field context. | Native or WebXR prototype uses the selected #38 anchoring strategy, documents platform support and calibration limits, and has sample/test coverage for the first field workflow. | Depends on #38, #12 scene foundations, protected scene auth, and offline scene policy where disconnected AR is in scope. | Closed as enablement. GA physical-device evidence remains #225. |
| #38 Native scene anchoring spike | Decision spike for ARKit, ARCore, WebXR, and MAUI anchoring strategy. | Device capability requirements, anchoring comparison, accuracy/calibration risks, and first prototype target are documented and accepted. | Feeds #23; see [Mobile 3D and AR Dependency Matrix](mobile-3d-ar-dependency-matrix.md). | Closed decision slice. |
| #42 Browser offline 3D scene cache adapter | Browser/WebView package-local asset resolution for `<honua-scene>`. | Adapter strategy is selected, package-local URLs resolve 3D Tiles/terrain/textures/metadata, stale/expired/revoked states match policy, and browser/WebView tests or fixtures cover cache behavior. | Depends on #36, #40, and #41; see [Offline 3D Scene Packages](offline-3d-scene-packages.md). | Closed browser/WebView offline scene slice. |
| #50 Web display adapter | P1 web display adapter using MapLibre GL JS and deck.gl over SDK feature data. | `FeatureQueryResult` pages or streams render through the adapter with base map, camera, picking/highlighting, overlays, and DOM/test coverage. | Feeds #10 and informs display scope in [Mobile Contract Harmonization](mobile-contract-harmonization.md). | Closed adapter foundation; #10 remains the packaging/product surface. |
| #51 Geofencing acquisition and background location | Mobile-owned device location acquisition, permissions, and battery-aware background behavior. | Mobile maps location streams into SDK geofence/event contracts, handles iOS/Android permission/background lifecycle, and includes enter/exit/proximity sample or fixture coverage. | Depends on SDK geofence evaluation contracts; mobile owns sensors and lifecycle behavior. | Closed mobile runtime slice. |
| #57 Mapsui-inspired native .NET display evaluation | Decision spike for native .NET display adapter direction. | Decision record states whether to use Mapsui, borrow architecture patterns, or reject it; follow-up adapter scope and prototype/test criteria are clear. | Informs future native display after #50/#10; see display ownership in [Mobile Contract Harmonization](mobile-contract-harmonization.md). | Closed evaluation slice. |

## Dependency Map

| Dependency owner | Backlog impact |
|------------------|----------------|
| `honua-sdk-dotnet` | Owns portable feature, edit, attachment, scene, field, offline, geofence evaluation, geometry, and future plugin contracts consumed by mobile. |
| `honua-server` | Owns hosted 3D Tiles, scene registry, terrain, elevation, generated tiles, I3S compatibility, plugin server APIs, and other backend behavior needed before mobile production work can close. |
| `honua-mobile` | Owns MAUI registration, native storage, GeoPackage/SQLite lifecycle, background sync scheduling, permissions, camera/media capture, GPS/location acquisition, display/embed packaging, browser/WebView cache adapters, and AR/VR host integration. |
| Other active worktrees | #42 and #50 have active implementation ownership outside this roadmap. Treat this page as dependency coordination only. |

## Recommended Closure Sequence

1. Close #10 when the embeddable map product surface is packaged and documented
   over the closed web display adapter foundation.
2. Keep #92 open until honua-server#965 is fixed, then re-dispatch `Cloud
   Acceptance` on `main` and attach the evidence artifact to #92.
3. Close #225 only after ARCore/ARKit adapter evidence and physical-device
   validation are attached for the first AR/3D field workflow.
4. Keep #82 and #85 in the DevOps/store-release lane rather than counting them
   as mobile product parity gaps.
5. Close #1 only when every non-Flutter child in this matrix is closed or
   intentionally deferred with a linked follow-up. Do not count #22 toward this
   workstream's closure.

## Closure Readiness Summary

The no-cloud/no-design Fulcrum and Survey123 parity backlog is closed for the
mobile-owned local runtime. The remaining competitive-parity risks are now clear
and narrower:

- hosted cloud acceptance for #92 is blocked by honua-server#965 DNS/TLS;
- #10 still needs the embeddable map product surface closure over the already
  closed display adapter foundation;
- #225 still needs physical-device AR/3D validation before GA closure;
- hosted designer/admin/supervisor/reporting/tenancy/RBAC/audit parity remains
  outside this repo and should be consumed through published SDK/server
  contracts.
