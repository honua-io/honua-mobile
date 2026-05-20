# Offline Synchronization Guide

This guide covers implementing offline-first data synchronization in mobile applications using the Honua Mobile SDK.

## Overview

The Honua Mobile SDK provides comprehensive offline synchronization capabilities designed for field data collection scenarios where network connectivity may be intermittent or unavailable.

The reusable offline package, journal, checkpoint, conflict, and sync engine contracts are supplied by `honua-sdk-dotnet`:

- `Honua.Sdk.Offline.Abstractions`
- `Honua.Sdk.Offline`

This mobile repo supplies the native runtime layer around those SDK contracts: GeoPackage/SQLite adapters, local file placement, MAUI dependency injection, app lifecycle integration, reachability checks, background scheduling, permissions, and field workflow UX.

## Core Concepts

### Offline-First Architecture

The SDK follows an offline-first approach:
1. **Local Storage**: All data is stored locally first using SQLite
2. **Background Sync**: Automatic synchronization when connectivity is available
3. **Conflict Resolution**: Intelligent conflict resolution for concurrent edits
4. **Progress Tracking**: Real-time sync progress with UI-friendly observables

### Storage Technologies

- **SQLite**: Local database for structured data
- **File System**: Local storage for photos, attachments, and cached maps
- **GeoPackage**: Standards-compliant offline geodatabase format

### SDK-backed sync registration

For new offline workflows, register the SDK sync core with the mobile GeoPackage adapters:

```csharp
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Offline.Abstractions;
using SdkOfflineSyncEngineOptions = Honua.Sdk.Offline.OfflineSyncEngineOptions;

var manifest = new OfflinePackageManifest
{
    PackageId = "mobile-offline-field-ops-v1",
    DisplayName = "Mobile Offline Field Operations",
    Version = "2026.05",
    Sources =
    [
        new OfflineSourceDescriptor
        {
            SourceId = "mobile_offline_demo/FeatureServer/68910",
            Source = new SourceDescriptor
            {
                Id = "mobile-offline-field-sites",
                Protocol = FeatureProtocolIds.GeoServicesFeatureService,
                Locator = new SourceLocator { ServiceId = "mobile_offline_demo", LayerId = 68910 },
            },
            Where = "1=1",
            OutFields = ["objectid", "globalid", "site_name", "status", "priority", "assigned_to", "inspection_date", "sync_version", "offline_action", "notes"],
            ReturnGeometry = true,
            PageSize = 100,
        },
        new OfflineSourceDescriptor
        {
            SourceId = "mobile_offline_demo/FeatureServer/68920",
            Source = new SourceDescriptor
            {
                Id = "mobile-offline-work-zones",
                Protocol = FeatureProtocolIds.GeoServicesFeatureService,
                Locator = new SourceLocator { ServiceId = "mobile_offline_demo", LayerId = 68920 },
            },
            Where = "1=1",
            OutFields = ["objectid", "globalid", "zone_name", "zone_status", "sync_version", "notes"],
            ReturnGeometry = true,
            PageSize = 100,
        },
    ],
    Metadata = new Dictionary<string, string>
    {
        ["fixture"] = "mobile-offline-field-ops-v1",
        ["serviceId"] = "mobile_offline_demo",
        ["editableLayerId"] = "68910",
        ["contextLayerId"] = "68920",
    },
};

builder.Services
    .AddHonuaMobileSdk(new HonuaMobileClientOptions
    {
        BaseUri = new Uri("https://api.honua.io"),
    })
    .AddHonuaSdkGeoPackageOfflineSync(
        new GeoPackageSyncStoreOptions
        {
            DatabasePath = Path.Combine(FileSystem.Current.AppDataDirectory, "fielddata.gpkg"),
            DefaultFeatureCacheTtl = TimeSpan.FromDays(7),
        },
        manifest,
        new SdkOfflineSyncEngineOptions
        {
            BatchSize = 50,
            ConflictStrategy = OfflineConflictStrategy.ManualReview,
        })
    .AddHonuaBackgroundSync(new BackgroundSyncOrchestratorOptions
    {
        SyncInterval = TimeSpan.FromMinutes(5),
    });
```

`AddHonuaSdkGeoPackageOfflineSync` wires `Honua.Sdk.Offline.OfflineSyncEngine` to:

- `GeoPackageSdkOfflineStoreAdapter` for SDK feature store, journal, checkpoint, and sync state interfaces.
- `HonuaMobileSdkFeatureClient` for SDK query/edit abstractions over the existing `HonuaMobileClient`.
- `SdkOfflineSyncRunner` for the existing mobile `IOfflineSyncRunner` used by foreground and background sync scheduling.

The adapter partitions cached features and queued edits by SDK package ID and source ID, so multiple offline packages can safely include the same source without overwriting feature rows or claiming each other's pending edits.

`AddHonuaGeoPackageOfflineSync(...)` remains available as lower-level mobile runtime plumbing for existing apps that already use the mobile-owned sync engine and uploader. New apps should prefer `AddHonuaSdkGeoPackageOfflineSync(...)` so portable package manifests, feature query/edit clients, journal, checkpoint, sync state, and `Honua.Sdk.Offline.OfflineSyncEngine` stay aligned with `honua-sdk-dotnet`, while this repo continues to own GeoPackage/SQLite storage, native file placement, connectivity, background scheduling, and permissions.

## Cache Policy And Spatial Indexing

`GeoPackageSyncStoreOptions` supports per-layer TTL policy for replicated feature caches:

```csharp
new GeoPackageSyncStoreOptions
{
    DatabasePath = "fielddata.gpkg",
    DefaultFeatureCacheTtl = TimeSpan.FromDays(7),
    LayerFeatureCacheTtls = new Dictionary<string, TimeSpan>
    {
        ["inspection-districts"] = TimeSpan.FromDays(30),
        ["weather-alerts"] = TimeSpan.FromHours(6),
    },
};
```

`EvictExpiredFeaturesAsync` removes expired feature rows and their R-tree index records. Point and envelope geometries are indexed on insert in `rtree_honua_features`, and bbox queries use that finite spatial index before returning cached feature JSON. The GeoPackage SRS table keeps the OGC GeoPackage 1.3 built-in records, including EPSG:4326 as the default WGS-84 SRS for Honua-managed cache tables.

SpatiaLite is not bundled for the baseline mobile cache path. Current feature-cache lookups only require SQLite R-tree envelope filtering, so SQLite-PCL-raw plus the platform SQLite provider is sufficient. If future work needs exact topology, CRS transforms, or SpatiaLite SQL functions on-device, add platform native binaries for iOS and Android in a separate packaging PR and gate them with AOT/trim smoke tests.

## Lifecycle-Aware Prefetch

`BackgroundPrefetchScheduler` runs optional cache-warming work with bounded concurrency. Call `CancelForLifecycleEventAsync(PrefetchLifecycleEvent.Suspend)` from app suspend handlers and `PrefetchLifecycleEvent.LowMemory` from platform memory-pressure hooks so downloads and cache fills observe cancellation before the OS reclaims resources.

## Configuration

## Disconnected Field Workflow Acceptance Harness

`tests/Honua.Mobile.ServerIntegration.Tests/DisconnectedFieldWorkflowAcceptanceTests.cs`
defines the acceptance scaffold for the cloud Honua disconnected field workflow:

1. `online-download`: create or reuse a replica and download server changes into the local GeoPackage cache.
2. `offline-edit`: queue a deterministic offline feature edit while the harness is logically disconnected.
3. `reconnect-sync`: run the mobile sync engine and upload queued edits.
4. `verify`: assert local cache/cursor state, drained queue state, and cloud fixture evidence.

The loopback test runs by default against the in-process integration server. The cloud/staging path is gated and only runs when explicitly enabled:

| Variable | Required | Purpose |
| --- | --- | --- |
| `HONUA_MOBILE_CLOUD_ACCEPTANCE` | Yes | Set to `1` or `true` to run cloud acceptance. Otherwise the cloud test emits skipped evidence and returns. |
| `HONUA_MOBILE_CLOUD_BASE_URL` | Yes when enabled | Cloud Honua base URL. |
| `HONUA_MOBILE_CLOUD_SERVICE_ID` | Yes when enabled | FeatureServer service id supplied by the honua-server#895 fixture. |
| `HONUA_MOBILE_CLOUD_LAYER_IDS` | No | Comma-separated FeatureServer layer IDs. Defaults to `0`. |
| `HONUA_MOBILE_CLOUD_API_KEY` | No | API key sent as `X-API-Key`. |
| `HONUA_MOBILE_CLOUD_BEARER_TOKEN` | No | Bearer token for authenticated staging fixtures. |
| `HONUA_MOBILE_ACCEPTANCE_EVIDENCE_DIR` | No | Directory for evidence JSON artifacts. Defaults to a temp evidence directory for cloud runs. |
| `HONUA_MOBILE_ACCEPTANCE_RUN_ID` | No | Stable run id used in artifact names and operation metadata. |
| `HONUA_MOBILE_ACCEPTANCE_PACKAGE_ID` | No | Offline package id recorded in evidence. Defaults to `pkg_acceptance_field_workflow`. |
| `HONUA_MOBILE_ACCEPTANCE_DATABASE_PATH` | No | GeoPackage path for the cloud acceptance run. |

Evidence artifacts are written as `<run-id>.evidence.json` and use schema
`honua.mobile.disconnected-field-workflow.evidence.v1`:

```json
{
  "schemaVersion": "honua.mobile.disconnected-field-workflow.evidence.v1",
  "workflowName": "disconnected-field-workflow",
  "runId": "cloud-disconnected-field-workflow-20260504120000",
  "status": "passed",
  "packageId": "pkg_acceptance_field_workflow",
  "serviceId": "assets",
  "sourceIds": ["0"],
  "operationIds": ["op-acceptance-add-001"],
  "cursorState": {
    "replica:assets": "replica-abc-123",
    "servergen:assets": "100"
  },
  "phases": [
    { "name": "online-download", "status": "passed" },
    { "name": "offline-edit", "status": "passed" },
    { "name": "reconnect-sync", "status": "passed" },
    { "name": "verify", "status": "passed" }
  ],
  "finalState": {
    "localFeatureCount": 1,
    "pendingOperationCount": 0,
    "localVerification": "downloaded features retained and sync queue drained",
    "cloudVerification": "fixture-specific verification result"
  }
}
```

Until honua-server#895 is available, the cloud test intentionally does not
assume server-side readback endpoints beyond the replica and `applyEdits`
contract. The loopback harness verifies request-level behavior locally; the
cloud harness records the fixture assumption in the queued operation metadata
and evidence artifact.

### Conflict Resolution

`OfflineConflictStrategy` controls how the SDK-owned `OfflineSyncEngine` reacts
when a queued local edit collides with a newer server version:

- `OfflineConflictStrategy.ClientWins` -- re-apply the local edit, overwriting
  the server record.
- `OfflineConflictStrategy.ServerWins` -- drop the local edit and accept the
  server version.
- `OfflineConflictStrategy.ManualReview` -- defer the conflicting edit so the
  application can surface it to the user before retrying.

Use `SdkOfflineSyncEngineOptions.ConflictStrategy` to pick the policy, as shown
in the SDK-backed registration above.

### Conflict Telemetry

Conflict outcomes from each sync run are returned in
`Honua.Sdk.Offline.OfflineSyncResult`. Mobile apps can surface counts via the
mobile `IOfflineSyncRunner` wrapper that `AddHonuaSdkGeoPackageOfflineSync(...)`
registers.

### Sync Telemetry And Background Scheduling

`AddHonuaBackgroundSync(...)` registers `BackgroundSyncOrchestrator`, which
runs `IOfflineSyncRunner.SyncAsync(...)` on an interval and exposes the most
recent run result for lifecycle hooks (for example, an app `OnSleep` handler
calling `RunOnceIfOnlineAsync`).

## Related Documentation

- [Mobile SDK Overview](../../README.md)
- [Camera Integration](camera-integration.md)
- [Offline 3D Scene Packages](offline-3d-scene-packages.md)
- [Performance Guide](performance.md)
- [Troubleshooting](troubleshooting.md)
