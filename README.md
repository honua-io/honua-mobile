# honua-mobile

`.NET` MAUI-first mobile SDK and field data collection foundation for [honua-server issue #359](https://github.com/honua-io/honua-server/issues/359).

## Scope delivered in this repo

- Mobile SDK client for Honua FeatureServer and OGC Features APIs.
- Field data collection domain model:
  - form schema and field types,
  - required/type/regex/range validation,
  - calculated field evaluation,
  - record status workflow (draft/submitted/approved/rejected),
  - duplicate detection by location + attributes.
- GeoPackage-backed offline sync:
  - SQLite `.gpkg` queue persistence,
  - sync cursors,
  - map area package catalog,
  - map area package downloader (`.gpkg` layer payload packaging),
  - deterministic priority-based replay,
  - production uploader (`HonuaApiOfflineOperationUploader`) for queued edit replay to Honua APIs,
  - conflict strategies (`ClientWins`, `ServerWins`, `ManualReview`).
- Background sync orchestration service (`BackgroundSyncOrchestrator`) with connectivity gate.
- MAUI-oriented DI/composition extensions for app startup wiring.
- MAUI app scaffold in `apps/Honua.Mobile.App` wired to SDK + offline services.

## Repository layout

- `src/Honua.Mobile.Sdk`: transport/auth/mobile client surface.
- `src/Honua.Mobile.Field`: forms, workflow, record lifecycle.
- `src/Honua.Mobile.Offline`: GeoPackage storage and sync engine.
- `src/Honua.Mobile.Maui`: MAUI service registration extensions.
- `apps/Honua.Mobile.App`: MAUI app shell.
- `tests/`: unit tests for field logic, uploader behavior, map-area packaging, and offline sync orchestration.

## GeoPackage offline schema

`Honua.Mobile.Offline.GeoPackageSyncStore` creates and manages:

- `gpkg_spatial_ref_sys`
- `gpkg_contents`
- `honua_sync_queue`
- `honua_sync_state`
- `honua_map_areas`

This allows queued edits, map-area package metadata, and sync checkpoints to live in a standards-friendly `.gpkg` file.

## Quick start

```bash
dotnet test Honua.Mobile.sln
```

### MAUI DI wiring example

```csharp
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;

builder.Services
    .AddHonuaMobileSdk(new HonuaMobileClientOptions
    {
        BaseUri = new Uri("https://api.honua.io"),
        ApiKey = "<api-key>",
    })
    .AddHonuaApiOfflineUploader()
    .AddHonuaMobileFieldCollection()
    .AddHonuaGeoPackageOfflineSync(
        new GeoPackageSyncStoreOptions
        {
            DatabasePath = Path.Combine(FileSystem.Current.AppDataDirectory, "honua-offline.gpkg"),
        },
        new OfflineSyncEngineOptions
        {
            ConflictStrategy = SyncConflictStrategy.ClientWins,
            BatchSize = 50,
        })
    .AddHonuaMapAreaDownload()
    .AddHonuaBackgroundSync();
```

### Test status

`dotnet test Honua.Mobile.sln` passes with:

- `Honua.Mobile.Field.Tests`: 4 tests
- `Honua.Mobile.Offline.Tests`: 10 tests

Total: 14 passing tests.

### MAUI app build note

`apps/Honua.Mobile.App` is scaffolded and wired, but building/running Android targets requires a configured Android SDK path on your machine (`XA5300` until configured).

## Current status

This is a production-oriented foundation for the mobile epic, not the full epic scope (AR/VR, no-code form builder UI, and native platform SDK wrappers are follow-on work).
