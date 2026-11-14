# Honua Mobile Feature Map

This repository owns mobile SDK packages, MAUI integration, offline field workflows, and embeddable web components.

## Current Capabilities

- `Honua.Mobile.Sdk`: transport, auth, gRPC-first feature queries, REST fallback, routing, scene metadata adapter, and secure transport checks.
- `Honua.Mobile.Offline`: GeoPackage storage, sync queue, pull/push sync, conflicts, map area download, delta cursors, TTL/cache governance, and R-tree bbox lookup.
- `Honua.Mobile.Maui`: DI registration, native display boundaries, map annotations, secure auth token storage, device location, background location, and geofencing contracts.
- `@honua/embed`: framework-agnostic `<honua-map>` and `<honua-scene>` components with display adapters, scene package caching, snippets, and DOM behavior tests.
- Reference MAUI applications, field collection example, embed example, scene example, and field collector template.
- Integration and smoke tests for loopback server paths, SDK offline sync, MAUI helpers, embed components, and optional live Honua query. Collect owns the field app workflows; the duplicate app and its app-specific tests have been retired.

## Source Evidence

- Mobile packages: `src/Honua.Mobile.*`
- Embed package: `src/Honua.Embed/`
- Apps and examples: `apps/`, `examples/`, `templates/`
- Tests: `tests/`, `src/Honua.Embed/tests/`
- 3D/offline/capture docs: `docs/guides/3d-scene-embed.md`, `docs/guides/offline-3d-scene-packages.md`, `docs/guides/protected-3d-scene-auth.md`, `docs/guides/field-ai-assisted-capture.md`
- No-cloud field parity docs: `docs/guides/no-cloud-field-parity-information-model.md`

## 3D Status

The repository can discover and embed server-managed scene metadata and cache offline scene packages. Native 3D/AR anchoring and richer Cesium-style controls are active backlog items, not fully shipped runtime capabilities.
