# SDK-backed Offline Field Ops Demo Harness

This page documents the first repeatable slice for issue
[honua-mobile#92](https://github.com/honua-io/honua-mobile/issues/92).
It proves the preferred mobile registration path and evidence shape for the
offline field operations demo. It does not close the full epic: cloud fixture
seeding, end-to-end push/pull verification, attachment upload, and in-app
conflict review still require follow-up work.

## Scope

The harness lives in
`tests/Honua.Mobile.Maui.Tests/SdkBackedOfflineFieldOperationsDemoHarnessTests.cs`.
It builds a deterministic `OfflinePackageManifest` and registers:

- `AddHonuaMobileSdk(...)`
- `AddHonuaSdkGeoPackageOfflineSync(...)`
- `AddHonuaBackgroundSync(...)`

The test then exercises the mobile-owned GeoPackage adapter through the SDK
offline abstractions:

1. Cache one editable field-site feature and one context work-zone feature.
2. Queue one deterministic stale-token update for manual conflict review.
3. Persist an SDK checkpoint and sync state cursor.
4. Emit `sdk-backed-offline-demo.evidence.json` with registration, cache,
   journal, cursor, and conflict-review evidence.

## Fixture

The manifest is intentionally SDK-owned shape and mobile-owned runtime wiring:

| Field | Value |
| --- | --- |
| Package id | `mobile-offline-field-ops-v1` |
| Service id | `mobile_offline_demo` |
| Editable source | `mobile_offline_demo/FeatureServer/68910` |
| Context source | `mobile_offline_demo/FeatureServer/68920` |
| Conflict scenario | `stale-sync-version-manual-review` |
| Server dependency | `honua-io/honua-server#895` |

The harness uses `Honua.Sdk.Offline.OfflineSyncEngine` only through
`AddHonuaSdkGeoPackageOfflineSync(...)`; it does not introduce a new server
client, SDK contract, geometry primitive, or mobile-local sync semantic.

## Run

```bash
dotnet test tests/Honua.Mobile.Maui.Tests/Honua.Mobile.Maui.Tests.csproj --filter FullyQualifiedName~SdkBackedOfflineFieldOperationsDemoHarnessTests
```

The evidence artifact is created under the test temp directory during the
default run and asserted by the test. To produce durable evidence for a release
packet, set `HONUA_MOBILE_SDK_OFFLINE_DEMO_EVIDENCE_DIR`:

```bash
export HONUA_MOBILE_SDK_OFFLINE_DEMO_EVIDENCE_DIR=/tmp/honua-mobile-sdk-offline-demo
dotnet test tests/Honua.Mobile.Maui.Tests/Honua.Mobile.Maui.Tests.csproj --filter FullyQualifiedName~SdkBackedOfflineFieldOperationsDemoHarnessTests
```

The harness writes a unique subdirectory containing
`sdk-backed-offline-demo.evidence.json` and `offline-field-ops-demo.gpkg`.

## Evidence Schema

The evidence schema version is
`honua.mobile.sdk-backed-offline-demo-harness.evidence.v1`.

Required fields:

| Field | Meaning |
| --- | --- |
| `packageId` | SDK offline package id registered with the mobile app. |
| `sourceIds` | Manifest sources included in the demo fixture. |
| `registrations` | Resolved mobile runner, SDK engine, GeoPackage adapter, SDK store interfaces, and background orchestrator. |
| `featureCache` | Per-source and total cached feature counts in GeoPackage storage. |
| `journal` | Pending SDK change journal count and deterministic conflict operation id. |
| `syncState` | Persisted SDK sync phase, token, and pulled feature count. |
| `conflictReview` | Manual-review mode and deterministic conflict scenario label. |

## Remaining Work

- Run the existing disconnected workflow harness against the seeded cloud
  fixture once `honua-io/honua-server#895` is available.
- Move reconnect push/pull verification from the mobile-owned sync engine path
  to the SDK-backed runner when the fixture can safely accept deterministic
  edits.
- Surface the conflict-review operation in the field collection app UX.
- Extend evidence with package size, metadata cache status, retry failures, and
  attachment/media upload results.
