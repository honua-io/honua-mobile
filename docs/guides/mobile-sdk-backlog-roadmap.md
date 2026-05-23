# Mobile SDK Backlog Roadmap

Last reviewed: 2026-05-23.

This roadmap integrates the remaining non-Flutter backlog for #1, the mobile SDK
epic. It covers open children #10, #12, #16, #23, #38, #42, #50, #51, and #57.
It intentionally excludes #22, which owns Flutter and broader platform parity
expansion.

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
| Offline mobile runtime | Mobile-owned runtime behavior is established around GeoPackage/SQLite, queueing, file placement, and app lifecycle. | Remaining work should extend the runtime through SDK contracts instead of adding provider-neutral clients here. |
| Display and embed | Active work is split between map embedding, web display adapters, scene rendering, and native display evaluation. | #10, #50, and #57 should remain separate so product packaging, web rendering, and native .NET evaluation do not block each other unnecessarily. |
| 3D, offline scene, and AR/VR | Policy and dependency order are documented; implementation depends on server, SDK, browser/WebView, and native platform decisions. | #12 remains the umbrella. #42 and #38 are immediate prerequisites for production offline scenes and native AR/VR work. |
| Field location behavior | Geofencing acquisition remains a mobile-owned runtime slice once SDK evaluation contracts are available. | #51 should not define portable geofence rules; it should consume the SDK and own permissions, sensors, background behavior, and battery policy. |
| Plugins | Mobile/web hosts own runtime loading and UI integration; non-UI manifests and permission contracts belong in shared SDK/server work. | #16 can consume SDK manifests from `Honua.Sdk.Abstractions`; server plugin APIs remain tracked in honua-io/honua-server#347. |

## Fulcrum/Survey123 Parity Backlog Index

These issues are the current execution backlog for closing the gap between the
historical Phase 0 parity targets and source-backed shipped status.

| Priority | Issues | Closure signal |
|----------|--------|----------------|
| P0 | [#208](https://github.com/honua-io/honua-mobile/issues/208) restore green validation; [#209](https://github.com/honua-io/honua-mobile/issues/209) reconcile parity docs. | Agents and reviewers can trust the local/CI baseline and source-backed docs. |
| P1 | [#210](https://github.com/honua-io/honua-mobile/issues/210) functional FieldCollection workflows; [#211](https://github.com/honua-io/honua-mobile/issues/211) metadata-driven projects/layers/forms; [#212](https://github.com/honua-io/honua-mobile/issues/212) remote sync transports; [#213](https://github.com/honua-io/honua-mobile/issues/213) attachment storage/sync; [#214](https://github.com/honua-io/honua-mobile/issues/214) pilot field-type controls; [#215](https://github.com/honua-io/honua-mobile/issues/215) rules/calculations/conditional visibility/repeats; [#216](https://github.com/honua-io/honua-mobile/issues/216) real map workflow surface. | MVP field workflow is usable end to end from metadata load through capture, validation, map context, offline storage, and sync. |
| P2 | [#217](https://github.com/honua-io/honua-mobile/issues/217) product acceptance suite; [#218](https://github.com/honua-io/honua-mobile/issues/218) auth/session hardening; [#219](https://github.com/honua-io/honua-mobile/issues/219) server/admin dependencies; [#220](https://github.com/honua-io/honua-mobile/issues/220) local record export; [#221](https://github.com/honua-io/honua-mobile/issues/221) diagnostics and sync health. | Beta hardening covers acceptance evidence, operational support, auth robustness, admin dependencies, and field troubleshooting. |
| P3 | [#222](https://github.com/honua-io/honua-mobile/issues/222) external GNSS metadata; [#223](https://github.com/honua-io/honua-mobile/issues/223) geofence/proximity workflows; [#224](https://github.com/honua-io/honua-mobile/issues/224) AI-assisted capture hooks; [#225](https://github.com/honua-io/honua-mobile/issues/225) first AR/3D field workflow; [#226](https://github.com/honua-io/honua-mobile/issues/226) mobile/web plugin host hardening. | GA-oriented differentiators are implemented only after SDK/server dependencies and MVP/Beta workflow gaps are stable. |

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
| #12 3D / Scene services | GA umbrella for 3D visualization, terrain, building layers, CesiumJS, and related scene service capability. | Close only after server 3D serving/registry/terrain/elevation/generation/I3S decisions and client SDK/render/offline hooks are complete or explicitly split into follow-up epics. | Depends on honua-io/honua-server#837 through #844, SDK scene contracts, #42, #38, and #23; see [Mobile 3D and AR Dependency Matrix](mobile-3d-ar-dependency-matrix.md). | Remains epic scope. Do not close from mobile docs alone. |
| #16 Plugin client SDK | GA host/runtime plugin framework for mobile and web. | Hosts can load/register approved plugins, surface UI extension points, enforce sandbox/signing/permission rules, and consume shared non-UI manifests from SDK/server contracts. | Consumes SDK-owned plugin manifests from `Honua.Sdk.Abstractions`; server plugin API dependency remains honua-io/honua-server#347. See [Mobile Contract Harmonization](mobile-contract-harmonization.md). | Host/runtime slice closeable when mobile/web adapters and docs are linked; avoid defining stable plugin contracts locally. |
| #23 AR/VR field workflow enablement | GA field overlay workflow over scene, device pose, camera, and field context. | Native or WebXR prototype uses the selected #38 anchoring strategy, documents platform support and calibration limits, and has sample/test coverage for the first field workflow. | Depends on #38, #12 scene foundations, protected scene auth, and offline scene policy where disconnected AR is in scope. | Remaining implementation stream. Start after #38 closes. |
| #38 Native scene anchoring spike | Decision spike for ARKit, ARCore, WebXR, and MAUI anchoring strategy. | Device capability requirements, anchoring comparison, accuracy/calibration risks, and first prototype target are documented and accepted. | Feeds #23; see [Mobile 3D and AR Dependency Matrix](mobile-3d-ar-dependency-matrix.md). | Closure-friendly decision slice. Close before AR/VR implementation begins. |
| #42 Browser offline 3D scene cache adapter | Browser/WebView package-local asset resolution for `<honua-scene>`. | Adapter strategy is selected, package-local URLs resolve 3D Tiles/terrain/textures/metadata, stale/expired/revoked states match policy, and browser/WebView tests or fixtures cover cache behavior. | Depends on #36, #40, and #41; see [Offline 3D Scene Packages](offline-3d-scene-packages.md). | Current implementation slice owned separately. Reference here, but do not duplicate detailed cache design. |
| #50 Web display adapter | P1 web display adapter using MapLibre GL JS and deck.gl over SDK feature data. | `FeatureQueryResult` pages or streams render through the adapter with base map, camera, picking/highlighting, overlays, and DOM/test coverage. | Feeds #10 and informs display scope in [Mobile Contract Harmonization](mobile-contract-harmonization.md). | Current implementation slice owned separately. This roadmap only sequences it. |
| #51 Geofencing acquisition and background location | Mobile-owned device location acquisition, permissions, and battery-aware background behavior. | Mobile maps location streams into SDK geofence/event contracts, handles iOS/Android permission/background lifecycle, and includes enter/exit/proximity sample or fixture coverage. | Depends on SDK geofence evaluation contracts; mobile owns sensors and lifecycle behavior. | Remaining mobile runtime stream. Start when SDK contracts are available. |
| #57 Mapsui-inspired native .NET display evaluation | Decision spike for native .NET display adapter direction. | Decision record states whether to use Mapsui, borrow architecture patterns, or reject it; follow-up adapter scope and prototype/test criteria are clear. | Informs future native display after #50/#10; see display ownership in [Mobile Contract Harmonization](mobile-contract-harmonization.md). | Closure-friendly evaluation slice. It should not block web display work. |

## Dependency Map

| Dependency owner | Backlog impact |
|------------------|----------------|
| `honua-sdk-dotnet` | Owns portable feature, edit, attachment, scene, field, offline, geofence evaluation, geometry, and future plugin contracts consumed by mobile. |
| `honua-server` | Owns hosted 3D Tiles, scene registry, terrain, elevation, generated tiles, I3S compatibility, plugin server APIs, and other backend behavior needed before mobile production work can close. |
| `honua-mobile` | Owns MAUI registration, native storage, GeoPackage/SQLite lifecycle, background sync scheduling, permissions, camera/media capture, GPS/location acquisition, display/embed packaging, browser/WebView cache adapters, and AR/VR host integration. |
| Other active worktrees | #42 and #50 have active implementation ownership outside this roadmap. Treat this page as dependency coordination only. |

## Recommended Closure Sequence

1. Stabilize the web display foundation: complete #50, then close #10 when the
   embeddable component is packaged and documented over that adapter.
2. Finish the browser/WebView offline scene path in #42 after the shared package
   policy and .NET package pieces are stable.
3. Close #38 as a decision spike before starting #23 implementation.
4. Keep #12 open as the 3D umbrella until server, SDK, renderer, offline, and
   AR/VR scope is either delivered or explicitly split into follow-up epics.
5. Start #51 only after the SDK geofence/event evaluation contracts are ready to
   consume from published `Honua.Sdk.*` packages.
6. Keep #16 scoped to host/runtime adapters now that SDK-owned non-UI plugin
   manifests are available; link server-side plugin behavior to
   honua-io/honua-server#347.
7. Close #1 only when every non-Flutter child in this matrix is closed or
   intentionally deferred with a linked follow-up. Do not count #22 toward this
   workstream's closure.

## Closure Readiness Summary

The foundation is documented enough for #1 coordination: Phase 0, contract
harmonization, offline sync ownership, 3D/AR dependencies, offline scene policy,
and protected scene auth are already present.

The nearest closeable items are decision or narrow implementation slices: #38
and #57 as spikes, and #42/#50 after their active implementation work lands.
#10 can close after #50 provides the display adapter foundation. #12, #16, #23,
#51, and #1 remain broader epic or implementation scope.
