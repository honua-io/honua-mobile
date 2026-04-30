# Mobile Contract Harmonization

Issue #48 aligns `honua-mobile` with `honua-sdk-dotnet` so mobile and shared
.NET SDK packages use one contract vocabulary instead of parallel DTOs.

The baseline fixture is
`contracts/fixtures/mobile-sdk-contract-harmonization.v1.json`. Keep that file
portable: it is intentionally plain JSON so `honua-sdk-dotnet` tests can read
the same ownership map without referencing mobile assemblies.

## Compatibility Baseline

| Mobile baseline | Shared SDK baseline | Status |
|-----------------|---------------------|--------|
| `honua-mobile` source packages from `main` after #68 plus scene and field adapter work | `Honua.Sdk.*` `0.1.8-alpha.1` | Fixture-level compatibility for shared feature, attachment, source, edit, routing, scene, field, and offline contracts |

`honua-mobile` does not currently publish versioned NuGet packages. Until it
does, compatibility is stated as source-baseline compatibility against the
published shared SDK package versions above. When mobile packages gain
`PackageVersion`, add rows here before changing public DTO ownership.

## Ownership Map

| Model family | Owner | Mobile disposition |
|--------------|-------|--------------------|
| Feature query requests/results | `honua-sdk-dotnet` / `Honua.Sdk.Abstractions` | Mobile DTOs are transport shims; add adapters to `FeatureQueryRequest` and `FeatureQueryResult`. |
| Feature edit envelopes/results | `honua-sdk-dotnet` / `Honua.Sdk.Abstractions` | Mobile edit DTOs and offline queue payloads should map to `FeatureEditRequest`. |
| Feature attachment operations | `honua-sdk-dotnet` / `Honua.Sdk.Abstractions` | Mobile exposes `IHonuaFeatureAttachmentClient` through adapters only; no mobile-local attachment DTOs. |
| Geometry and spatial references | Split pending SDK geometry package | Keep mobile coordinates at platform edges until SDK geometry contracts graduate. |
| Offline sync state, journals, conflicts | `Honua.Sdk.Offline.Abstractions` plus mobile runtime adapters | Mobile owns native queue persistence, scheduling, and GeoPackage behavior; SDK owns portable manifests, journals, checkpoints, retry checkpoints, and conflict envelopes. |
| Form-related feature schemas and field workflows | `honua-sdk-dotnet` / `Honua.Sdk.Abstractions` plus `Honua.Sdk.Field` | SDK owns provider-neutral source fields, form schema, validation, calculated fields, duplicate detection, and record workflow. Mobile owns rendering, capture UX, local media paths, GPS acquisition, DI, and offline adapter integration. |
| Scene metadata and offline scene packages | `honua-sdk-dotnet` / `Honua.Sdk.Abstractions.Scenes` plus `Honua.Sdk.Scenes` | Mobile owns renderers, downloads, local storage, and display lifecycle. |
| Routing and network analysis | `honua-sdk-dotnet` / `Honua.Sdk.Abstractions` plus `Honua.Sdk.GeoServices` NAServer client | Mobile keeps only device location providers, permission flows, route display, and map interaction adapters. |
| GeoPackage sync and native storage adapters | `honua-mobile` | Keep database tables, background sync, file-system downloads, and MAUI registration in mobile. |
| Display/embed maps | `honua-mobile` / `Honua.Embed` | SDK returns portable contracts only; MapLibre, deck.gl, Cesium, Mapsui, WebGL/WebGPU, and map controls stay outside SDK core. |
| Non-UI plugin contracts | Split pending SDK plugin contracts after server dependency | Hosts own runtime loading, UI registration, sandboxing, and signing. |
| Legacy `honua-mobile-sdk` contracts | Quarantine | Migrate concepts only after the fixture assigns ownership. |

## Migration Rules

- New provider-neutral feature read code should target
  `Honua.Sdk.Abstractions.Features.FeatureQueryRequest`,
  `FeatureQueryResult`, `FeatureSource`, and `SourceDescriptor`.
- New provider-neutral feature edit code should target
  `FeatureEditRequest`, `FeatureEditResponse`, and related edit result models.
- New provider-neutral attachment code should target
  `IHonuaFeatureAttachmentClient` and the SDK `FeatureAttachment*` request/result
  contracts.
- New provider-neutral routing code should target
  `Honua.Sdk.Abstractions.Routing.IHonuaRoutingClient`, `RoutingLocation`,
  `RouteDirectionsRequest`, `RouteOptimizationRequest`, `ServiceAreaRequest`,
  and `ClosestFacilityRequest`.
- New provider-neutral scene metadata code should target
  `Honua.Sdk.Abstractions.Scenes.IHonuaSceneClient`,
  `HonuaSceneResolveRequest`, `HonuaSceneResolution`, and
  `HonuaScenePackageManifest`, with `Honua.Sdk.Scenes` providing the portable
  client implementation.
- New provider-neutral field workflow code should target
  `Honua.Sdk.Field.Forms.FormDefinition`, `FormValidator`,
  `CalculatedFieldEvaluator`, `Honua.Sdk.Field.Records.FieldRecord`,
  `DuplicateDetector`, and `RecordWorkflow`; mobile code should add only
  capture, local path, UI, DI, and offline adapter behavior around those
  contracts.
- Mobile-only APIs may keep device, MAUI, GeoPackage, background execution,
  camera/location, route-location-provider, display, and offline file-system
  concerns.
- Sibling repos consume `Honua.Sdk.*` through published NuGet packages from
  GitHub Packages. Do not copy SDK source and do not add long-lived project
  references.
- Canonical `.proto` definitions stay in `geospatial-grpc`; SDK and mobile
  consume generated or published protocol bindings instead of redefining them.
- Any migrated SDK contract that requires backend behavior must link the
  corresponding `honua-server` dependency issue before implementation starts.
- Any DTO copied or recovered from `honua-mobile-sdk` must first be classified
  in the fixture as SDK-owned, mobile-owned, or quarantined.
- Companion SDK issues must close SDK gaps before mobile deletes local runtime
  shims that still have no shared package owner.

## Follow-Up Work

- #49 maps current offline queue and GeoPackage state to future SDK offline
  package, journal, checkpoint, and conflict-envelope contracts.
- `honua-sdk-dotnet#68` added the matching SDK-side fixture and tests.
- #54 moves reusable `Honua.Mobile.Sdk` feature clients toward SDK contracts and
  package consumption.
- #54 now consumes SDK routing contracts and the `Honua.Sdk.GeoServices` routing
  client from `Honua.Sdk.*`; mobile keeps only
  location-provider helpers.
- #55 consumes SDK scene metadata and package manifest contracts from
  `Honua.Sdk.*` `0.1.8-alpha.1`; mobile keeps downloader, GeoPackage catalog,
  display, and renderer lifecycle code.
- #56 consumes SDK field contracts from `Honua.Sdk.Field` `0.1.8-alpha.1`;
  mobile keeps field rendering/capture UX, local media paths, GPS acquisition,
  DI, and offline adapter integration.
- Once a mobile package version exists, add it to the compatibility table and
  fixture before changing public model ownership.
