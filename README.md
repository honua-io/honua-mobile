# Honua Mobile SDK for .NET

.NET MAUI mobile SDK for [Honua Server](https://github.com/honua-io/honua-server) --
offline-first field data collection with GeoPackage storage, gRPC transport,
dynamic forms, and background sync.

## Packages

| Package | Purpose |
|---------|---------|
| **Honua.Mobile.Sdk** | Transport, auth, gRPC-first client with REST fallback |
| **Honua.Mobile.Field** | Dynamic forms, validation, calculated fields, record workflow |
| **Honua.Mobile.Offline** | GeoPackage storage, sync queue, map area download, conflict resolution |
| **Honua.Mobile.Maui** | MAUI service registration and DI extensions |

## Quick Start

```csharp
// In MauiProgram.cs
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;

builder.Services
    .AddHonuaMobileSdk(new HonuaMobileClientOptions
    {
        BaseUri = new Uri("https://your-honua-server.com"),
        GrpcEndpoint = new Uri("https://your-honua-server.com"),
        ApiKey = "<your-api-key>",
        PreferGrpcForFeatureQueries = true,
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

## Offline Sync

GeoPackage-backed offline storage with queue-based sync:

- **GeoPackage storage** -- standards-compliant `.gpkg` files (interoperable with QGIS, ArcGIS)
- **Sync queue** -- queued edits with claim/lease semantics to prevent duplicate processing
- **Conflict resolution** -- ClientWins, ServerWins, or ManualReview strategies
- **Background sync** -- connectivity-aware with periodic timer and semaphore gating
- **Map area download** -- offline basemap packages with path traversal protection
- **Delta sync** -- replica-based incremental downloads with cursor persistence

## Field Collection

- **Dynamic forms** -- 18 field types (text, numeric, photo, video, signature, barcode, etc.)
- **Validation** -- required fields, regex with ReDoS timeout, numeric ranges, min media count
- **Calculated fields** -- `concat()` and `sum()` with `$fieldId` references
- **Record workflow** -- Draft -> Submitted -> Approved/Rejected state machine
- **Duplicate detection** -- Haversine distance + attribute matching

## gRPC Transport

gRPC-first with automatic REST fallback:

```csharp
var features = await client.QueryFeaturesAsync(serviceId, layerId, query);

await foreach (var feature in client.QueryFeaturesStreamAsync(serviceId, layerId, query))
{
    ProcessFeature(feature);
}
```

Transport security enforced -- API keys and bearer tokens are never sent over HTTP
unless `AllowInsecureTransportForDevelopment` is explicitly set.

## Repository Structure

```
src/
  Honua.Mobile.Sdk/           Core mobile client
  Honua.Mobile.Field/         Field collection components
  Honua.Mobile.Offline/       GeoPackage sync engine
  Honua.Mobile.Maui/          MAUI platform integration
  Honua.Mobile.IoT/           IoT sensor abstractions (interface-only, future)
apps/
  Honua.Mobile.App/           Reference MAUI application
tests/
  Honua.Mobile.Sdk.Tests/     Transport security, gRPC translation (6 tests)
  Honua.Mobile.Field.Tests/   Validation, calculated fields, visibility (6 tests)
  Honua.Mobile.Offline.Tests/ Sync engine, conflicts, map download (10 tests)
  Honua.Mobile.Smoke.Tests/   End-to-end smoke paths (6 tests)
proto/
  honua/v1/                   gRPC protocol definitions
```

## Building

```bash
dotnet build Honua.Mobile.sln
dotnet test Honua.Mobile.sln
```

Building Android targets requires a configured Android SDK. The library projects
(`Sdk`, `Field`, `Offline`, `Maui`) target `net10.0` and build on any platform
without the MAUI workload.

## Status

Production-ready foundation for offline sync, forms, and gRPC transport.
28 tests across 4 test projects.

The IoT module (`Honua.Mobile.IoT`) contains interface definitions only --
no implementation yet.

## Documentation

- **[Getting Started](docs/getting-started/)** -- installation, tutorial, and developer checklist
- **[Guides](docs/guides/)** -- in-depth guides for offline sync, security, camera, performance, and more
- **[API Reference](docs/api/)** -- core SDK API documentation

## License

[Apache 2.0](LICENSE)
