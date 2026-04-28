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
| `honua-mobile` source packages from `main` after #52 | `Honua.Sdk.Abstractions` `0.1.0-alpha.1` | Fixture-level compatibility for shared feature/source contracts |

`honua-mobile` does not currently publish versioned NuGet packages. Until it
does, compatibility is stated as source-baseline compatibility against the
shared SDK package versions above. When mobile packages gain `PackageVersion`,
add rows here before changing public DTO ownership.

## Ownership Map

| Model family | Owner | Mobile disposition |
|--------------|-------|--------------------|
| Feature query requests/results | `honua-sdk-dotnet` / `Honua.Sdk.Abstractions` | Mobile DTOs are transport shims; add adapters to `FeatureQueryRequest` and `FeatureQueryResult`. |
| Feature edit envelopes/results | `honua-sdk-dotnet` / `Honua.Sdk.Abstractions` | Mobile edit DTOs and offline queue payloads should map to `FeatureEditRequest`. |
| Geometry and spatial references | Split pending SDK geometry package | Keep mobile coordinates at platform edges until SDK geometry contracts graduate. |
| Offline sync state, journals, conflicts | Split pending SDK offline contracts | Mobile owns native queue persistence, scheduling, and GeoPackage behavior; SDK should own portable manifests and conflict envelopes. |
| Form-related feature schemas | Split | SDK source schema owns provider-neutral fields; mobile owns form rendering, validation, calculated fields, and record workflow. |
| Scene metadata and offline scene packages | `honua-mobile` candidate for future SDK scene package | Keep manifest/server handoff portable and fixture-backed. |
| Routing and network analysis | `honua-mobile` | Keep NAServer encoding and device location provider behavior in mobile. |
| GeoPackage sync and native storage adapters | `honua-mobile` | Keep database tables, background sync, file-system downloads, and MAUI registration in mobile. |

## Migration Rules

- New provider-neutral feature read code should target
  `Honua.Sdk.Abstractions.Features.FeatureQueryRequest`,
  `FeatureQueryResult`, `FeatureSource`, and `SourceDescriptor`.
- New provider-neutral feature edit code should target
  `FeatureEditRequest`, `FeatureEditResponse`, and related edit result models.
- Mobile-only APIs may keep device, MAUI, GeoPackage, background execution,
  camera/location, route-location-provider, and offline file-system concerns.
- Any DTO copied or recovered from `honua-mobile-sdk` must first be classified
  in the fixture as SDK-owned, mobile-owned, or quarantined.
- Companion SDK issues must close SDK gaps before mobile deletes local runtime
  shims that still have no shared package owner.

## Follow-Up Work

- #49 maps current offline queue and GeoPackage state to future SDK offline
  package, journal, checkpoint, and conflict-envelope contracts.
- `honua-sdk-dotnet#68` should consume the fixture and add matching tests on
  the shared SDK side.
- Once a mobile package version exists, add it to the compatibility table and
  fixture before changing public model ownership.
