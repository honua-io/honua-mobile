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

`EvictExpiredFeaturesAsync` removes expired feature rows and their R-tree index records. Point, envelope, GeoJSON point, and GeoJSON `bbox` metadata are indexed on insert in `rtree_honua_features`, and bbox queries use that finite spatial index before returning cached feature JSON.

Cached feature layers default to EPSG:4326. When a FeatureServer payload includes `geometry.spatialReference`, or an SDK download page advertises `SourceDescriptor.Schema.SpatialReference`, the store records the layer CRS in GeoPackage SRS metadata and `honua_feature_layers`; common Web Mercator WKIDs are normalized to EPSG:3857. SDK `FeatureBoundingBox` queries must use the cached layer CRS because the mobile storage layer does not run CRS transforms; projection and topology remain SDK/renderer responsibilities.

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
| `HONUA_MOBILE_CLOUD_VERIFY_READBACK` | No | Defaults to `true`. Set to `0` only for fixture bring-up runs that cannot yet answer FeatureServer readback queries. |
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
    "runTaggedFeatureCountBeforeReconnect": 0,
    "deleteTargetPresentBeforeReconnect": true,
    "runTaggedFeatureCount": 2,
    "deleteTargetPresent": false,
    "localVerification": "downloaded features retained and sync queue drained",
    "cloudVerification": "cloud readback found run-tagged create/update edits and confirmed deterministic delete target removal"
  }
}
```

When cloud acceptance fails with `failureCategory = "transport"`, inspect the
phase error for TLS or certificate text before changing mobile sync code. A
`RemoteCertificateNameMismatch` or certificate hostname/SAN mismatch means the
configured Honua base URL is presenting a certificate for a different host; keep
that tracked as a cloud/server infrastructure issue.

The cloud harness now expects the honua-server#895 fixture path to support
server-side readback on the editable FeatureServer layer. Before reconnect it
asserts that no records carry the current `HONUA_MOBILE_ACCEPTANCE_RUN_ID` and
that the deterministic delete target, `objectid = 3`, exists. After reconnect it
asserts that run-tagged create/update records are visible with
`created-offline` and `inspection-complete` statuses and that `objectid = 3` is
no longer returned. The loopback harness continues to verify request-level
behavior locally for ordinary CI runs.

## Live Honua Image Server Interaction Tests

`tests/Honua.Mobile.ServerIntegration.Tests/LiveHonuaServerInteractionTests.cs`
adds opt-in live image coverage for the mobile server interaction surface. The
tests are disabled unless `HONUA_MOBILE_LIVE_SERVER_TESTS=1` is set. When
enabled without `HONUA_MOBILE_LIVE_SERVER_BASE_URL`, the fixture starts a
PostGIS container plus a Honua Server container image through Testcontainers.
The local image stack publishes REST on the Honua container's port `8080` and
native h2c gRPC on port `8081`; for Testcontainers-managed runs the fixture
derives `GrpcEndpoint` from the mapped host port for container port `8081`.
When `HONUA_MOBILE_LIVE_SERVER_BASE_URL` is set, the tests use that already
running Honua environment instead.

The live suite covers health, FeatureServer REST query/edit, FeatureServer gRPC
query/edit, OGC Features CRUD, attachments, replica sync, offline queued upload
through the background orchestrator path, map area downloads, scene package
asset downloads, scene metadata/resolve, routing, FieldCollection API-key
validation, OAuth refresh, and both app-level and MAUI exception upload paths.

| Variable | Required | Purpose |
| --- | --- | --- |
| `HONUA_MOBILE_LIVE_SERVER_TESTS` | Yes | Set to `1` or `true` to enable live image tests. |
| `HONUA_MOBILE_LIVE_SERVER_BASE_URL` | No | Existing Honua base URL. If omitted, Testcontainers starts the image. |
| `HONUA_MOBILE_LIVE_SERVER_IMAGE` | No | Honua Server image. Defaults to `honuaio/honua-server:latest`. |
| `HONUA_MOBILE_LIVE_SERVER_POSTGRES_IMAGE` | No | PostGIS image for Testcontainers. Defaults to `postgis/postgis:17-3.5-alpine`. |
| `HONUA_MOBILE_LIVE_SERVER_FIXTURE_SQL` | No | SQL fixture applied to the Testcontainers PostGIS database before tests run. Use the `honua-server#895` mobile fixture seed when available. |
| `HONUA_MOBILE_LIVE_SERVER_READY_PATH` | No | Readiness path. Defaults to `/healthz/ready`. |
| `HONUA_MOBILE_LIVE_SERVER_SERVICE_ID` | No | FeatureServer service id. Defaults to `mobile_offline_demo`. |
| `HONUA_MOBILE_LIVE_SERVER_LAYER_ID` | No | Editable FeatureServer layer id. Defaults to `68910`. |
| `HONUA_MOBILE_LIVE_SERVER_REPLICA_LAYER_IDS` | No | Comma-separated replica layer ids. Defaults to the editable layer plus `68920`. |
| `HONUA_MOBILE_LIVE_SERVER_OGC_COLLECTION_ID` | No | OGC collection id. Defaults to the layer id. |
| `HONUA_MOBILE_LIVE_SERVER_SCENE_ID` | No | Scene id used by scene metadata tests. Defaults to `downtown-honolulu`. |
| `HONUA_MOBILE_LIVE_SERVER_SCENE_ASSET_BASE_URL` | No | Base URL that serves `tileset.json`. Defaults to `/scenes/{HONUA_MOBILE_LIVE_SERVER_SCENE_ID}/` under the live base URL. |
| `HONUA_MOBILE_LIVE_SERVER_GRPC_URL` | No | Separate gRPC endpoint. Overrides the Testcontainers-derived gRPC URL. For prestarted servers, omit it to keep the client default of using the base URL. |
| `HONUA_MOBILE_LIVE_SERVER_API_KEY` | No | API key sent as `X-API-Key`. Defaults to the Testcontainers admin password when the fixture starts the image. |
| `HONUA_MOBILE_LIVE_SERVER_BEARER_TOKEN` | No | Bearer token sent on client requests. |
| `HONUA_MOBILE_LIVE_SERVER_OAUTH_REFRESH_PATH` | No | OAuth refresh path. Defaults to `/oauth/token`. |
| `HONUA_MOBILE_LIVE_SERVER_EXCEPTION_UPLOAD_PATH` | No | Mobile exception ingestion path. Defaults to `/api/mobile/exceptions`. |

Example using a local `honua-server` checkout fixture seed:

```bash
HONUA_MOBILE_LIVE_SERVER_TESTS=1 \
HONUA_MOBILE_LIVE_SERVER_FIXTURE_SQL=/home/makani/honua-server/tests/seed/mobile-offline-demo-v1.sql \
dotnet test tests/Honua.Mobile.ServerIntegration.Tests/Honua.Mobile.ServerIntegration.Tests.csproj
```

The live tests intentionally fail when an enabled server image does not expose a
configured interaction route or fixture. That keeps the mobile repo from
claiming live server coverage that is only satisfied by the loopback stub.
When `HONUA_MOBILE_LIVE_SERVER_FIXTURE_SQL` is set, the seed must match the
schema in the selected Honua Server image.

The embed package also has an opt-in live metadata fetch check for
`<honua-scene>`. Set `HONUA_EMBED_LIVE_SCENE_METADATA_URL` to a
`honua-scene-metadata/v1` document and optionally set
`HONUA_EMBED_LIVE_SCENE_ID` before running `npm test --prefix src/Honua.Embed`.

### Basic Setup

Configure offline capabilities in `MauiProgram.cs`:

```csharp
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .AddHonuaMobile(options =>
        {
            options.ServerAddress = "https://api.example.com";
            options.ApiKey = "your-api-key";

            // Offline configuration
            options.EnableOfflineMode = true;
            options.OfflineDatabase = "fielddata.gpkg";
            options.OfflineMaxFeatures = 50000;
            options.OfflineRetentionDays = 30;
            options.AutoCleanup = true;

            // Sync policies
            options.SyncPolicy = SyncPolicy.WifiPreferred;
            options.BatteryPolicy = BatteryPolicy.Conservative;
        });

    return builder.Build();
}
```

### Sync Policies

Control when synchronization occurs:

```csharp
public enum SyncPolicy
{
    Manual,           // User-initiated only
    WifiOnly,         // Only on WiFi connections
    WifiPreferred,    // WiFi preferred, cellular as fallback
    Any              // Any available connection
}

public enum BatteryPolicy
{
    Conservative,     // Minimal background activity
    Normal,          // Balanced performance and battery
    Performance      // Maximum performance, higher battery usage
}
```

## Data Management

### Local Data Storage

Store data offline-first:

```csharp
public class OfflineDataService
{
    private readonly IHonuaMobileClient _client;

    public OfflineDataService(IHonuaMobileClient client)
    {
        _client = client;
    }

    public async Task SaveFeatureOfflineAsync(Feature feature)
    {
        // Always save locally first
        await _client.SaveFeatureOfflineAsync(feature);

        // Queue for sync when network becomes available
        await _client.QueueForSyncAsync(feature.Id);
    }

    public async Task<IEnumerable<Feature>> QueryOfflineAsync(FeatureQuery query)
    {
        // Query from local database
        return await _client.QueryFeaturesOfflineAsync(query);
    }
}
```

### Data Download

Download data for offline use:

```csharp
[RelayCommand]
public async Task DownloadAreaForOfflineAsync()
{
    try
    {
        // Define area of interest
        var boundingBox = new Envelope
        {
            MinX = -122.5,
            MinY = 37.7,
            MaxX = -122.3,
            MaxY = 37.9
        };

        var layerIds = new[] { 0, 1, 2 }; // Layers to download

        // Download with progress tracking
        var progress = new Progress<DownloadProgress>(p =>
        {
            DownloadProgressPercent = p.PercentComplete;
            DownloadStatusText = p.StatusMessage;
        });

        await _client.DownloadAreaAsync(
            serviceId: "inspection-service",
            boundingBox: boundingBox,
            layerIds: layerIds,
            progress: progress
        );

        await Shell.Current.DisplayAlert("Success", "Area downloaded for offline use", "OK");
    }
    catch (Exception ex)
    {
        await Shell.Current.DisplayAlert("Error", $"Download failed: {ex.Message}", "OK");
    }
}
```

## Synchronization

### Automatic Sync

Configure automatic synchronization:

```csharp
public class SyncService : ObservableObject
{
    private readonly IHonuaMobileClient _client;
    private readonly IConnectivity _connectivity;

    public SyncService(IHonuaMobileClient client, IConnectivity connectivity)
    {
        _client = client;
        _connectivity = connectivity;

        // Subscribe to connectivity changes
        _connectivity.ConnectivityChanged += OnConnectivityChanged;

        // Enable background sync
        _ = EnableBackgroundSyncAsync();
    }

    private async void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            await StartSyncAsync();
        }
    }

    private async Task EnableBackgroundSyncAsync()
    {
        await _client.EnableBackgroundSyncAsync(interval: TimeSpan.FromHours(4));
    }
}
```

### Manual Sync

Implement user-initiated sync:

```csharp
[RelayCommand]
public async Task SyncNowAsync()
{
    if (_connectivity.NetworkAccess != NetworkAccess.Internet)
    {
        await Shell.Current.DisplayAlert("No Connection",
            "Internet connection required for sync", "OK");
        return;
    }

    try
    {
        Issyncing = true;

        // Monitor sync progress
        _client.SyncProgress.Subscribe(progress =>
        {
            SyncProgressPercent = progress.PercentComplete;
            SyncStatusText = progress.StatusMessage;
        });

        var result = await _client.SyncAsync();

        await Shell.Current.DisplayAlert("Sync Complete",
            $"Uploaded: {result.UploadedFeatures}, Downloaded: {result.DownloadedFeatures}",
            "OK");
    }
    catch (Exception ex)
    {
        await Shell.Current.DisplayAlert("Sync Error", ex.Message, "OK");
    }
    finally
    {
        IsSyncing = false;
    }
}
```

### Selective Sync

Sync only specific data:

```csharp
public async Task SyncLayerAsync(int layerId)
{
    var context = new SyncContext
    {
        LayerIds = new[] { layerId },
        SyncDirection = SyncDirection.Both,
        ConflictResolution = ConflictResolution.ServerWins
    };

    await _client.SyncAsync(context);
}

public async Task UploadOnlyAsync()
{
    var context = new SyncContext
    {
        SyncDirection = SyncDirection.Upload,
        ConflictResolution = ConflictResolution.ClientWins
    };

    await _client.SyncAsync(context);
}
```

## Conflict Resolution

`OfflineSyncEngine` defaults to `ManualReview` so conflicting edits are not
silently overwritten. Apps can set a global v1 strategy and override it per
layer and operation type:

```csharp
var sync = new OfflineSyncEngine(
    store,
    uploader,
    new OfflineSyncEngineOptions
    {
        BatchSize = 100,
        ConflictStrategy = SyncConflictStrategy.ManualReview,
        ConflictPolicyRules =
        [
            new SyncConflictPolicyRule
            {
                LayerKey = "critical-assets",
                OperationType = OfflineOperationType.Update,
                Strategy = SyncConflictStrategy.ClientWins,
            },
            new SyncConflictPolicyRule
            {
                LayerKey = "reference-boundaries",
                Strategy = SyncConflictStrategy.ServerWins,
            },
        ],
    });
```

The mobile-owned v1 strategies are:

```csharp
public enum SyncConflictStrategy
{
    ClientWins,   // retry with forceWrite=true
    ServerWins,   // drop the local operation and accept the server version
    ManualReview, // mark failed for user/operator resolution
}
```

Rules with both `LayerKey` and `OperationType` are more specific than rules for
only one dimension. If no rule matches, `ConflictStrategy` is used.

## Pending Queue And Connectivity

`GeoPackageSyncStore` persists the local edit queue in the device GeoPackage, so
pending operations survive process restart. A sync cycle claims pending rows
with an in-progress lease, releases claims on cancellation, deletes succeeded
operations, and leaves retryable failures in the queue for the next run.

`BackgroundSyncOrchestrator` gates upload/download cycles through
`IConnectivityStateProvider`. When the provider reports offline,
`RunOnceIfOnlineAsync` returns without claiming queue rows. When connectivity is
restored, the next manual or scheduled cycle resumes from the persisted pending
queue.

```csharp
await using var orchestrator = new BackgroundSyncOrchestrator(
    sync,
    connectivityStateProvider,
    new BackgroundSyncOrchestratorOptions
    {
        SyncInterval = TimeSpan.FromMinutes(5),
    });

await orchestrator.StartAsync();
```

## Sync Problems And Telemetry

Sync upload failures are mapped through `MobileSyncProblemHelper` before they are
stored in queue state or returned in `SyncRunResult.Failures`. Raw provider
exception names such as gRPC or SQLite exception types are kept as inner
diagnostic details, not user-facing sync failure reasons.

The mobile sync layer emits:

- ActivitySource: `Honua.Mobile.Sync`
- Counter: `mobile_sync_runs_total{result}`
- Counter: `mobile_sync_conflicts_total{strategy}`
- Gauge: `mobile_pending_operations`

Apps can subscribe with standard .NET diagnostics:

```csharp
using var listener = new MeterListener();
listener.InstrumentPublished = (instrument, meterListener) =>
{
    if (instrument.Meter.Name == MobileSyncTelemetry.MeterName)
    {
        meterListener.EnableMeasurementEvents(instrument);
    }
};
listener.Start();
```

## Storage Management

### Cache Management

Manage local storage efficiently:

```csharp
public class CacheManager
{
    private readonly IHonuaMobileClient _client;

    public async Task<StorageInfo> GetStorageInfoAsync()
    {
        return await _client.GetOfflineStorageInfoAsync();
    }

    [RelayCommand]
    public async Task CleanupOldDataAsync()
    {
        var options = new CleanupOptions
        {
            RetentionDays = 30,
            MaxSizeMB = 500,
            PreserveUserData = true
        };

        var cleaned = await _client.CleanupOfflineDataAsync(options);

        await Shell.Current.DisplayAlert("Cleanup Complete",
            $"Freed {cleaned.FreedSpaceMB:F1} MB", "OK");
    }

    [RelayCommand]
    public async Task ClearAllOfflineDataAsync()
    {
        var result = await Shell.Current.DisplayAlert("Confirm",
            "This will delete all offline data. Continue?", "Yes", "No");

        if (result)
        {
            await _client.ClearOfflineDataAsync();
        }
    }
}
```

### Attachment Handling

Manage photos and files offline:

```csharp
public async Task SaveAttachmentOfflineAsync(string featureId, FileResult file)
{
    // Save file locally with reference
    var localPath = await _client.SaveAttachmentOfflineAsync(featureId, file);

    // Update feature with local attachment reference
    var feature = await _client.GetFeatureOfflineAsync(featureId);
    feature.Attributes["photo_path"] = localPath;
    feature.Attributes["photo_synced"] = false;

    await _client.SaveFeatureOfflineAsync(feature);
    await _client.QueueForSyncAsync(featureId);
}
```

## Progress Monitoring

### Sync Progress UI

Display sync progress to users:

```csharp
public class SyncProgressViewModel : ObservableObject
{
    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private bool isSyncing;

    public void SubscribeToSyncProgress(IHonuaMobileClient client)
    {
        client.SyncProgress.Subscribe(progress =>
        {
            ProgressPercent = progress.PercentComplete;
            StatusText = progress.StatusMessage;
            IsSyncing = progress.IsActive;
        });
    }
}
```

XAML for progress display:

```xml
<Grid IsVisible="{Binding IsSyncing}">
    <ProgressBar Progress="{Binding ProgressPercent}" />
    <Label Text="{Binding StatusText}" HorizontalOptions="Center" />
</Grid>
```

## Testing

### Offline Testing

Test offline scenarios:

```csharp
[Test]
public async Task OfflineMode_SaveAndQuery_WorksWithoutNetwork()
{
    // Arrange
    var client = CreateOfflineClient();
    var feature = CreateTestFeature();

    // Act - simulate no network
    await client.SetNetworkModeAsync(NetworkMode.Offline);
    await client.SaveFeatureOfflineAsync(feature);

    var results = await client.QueryFeaturesOfflineAsync(new FeatureQuery
    {
        Where = $"OBJECTID = {feature.Id}"
    });

    // Assert
    Assert.AreEqual(1, results.Count());
}
```

### Sync Testing

Test synchronization scenarios:

```csharp
[Test]
public async Task Sync_ConflictResolution_HandlesCorrectly()
{
    // Arrange
    var localFeature = CreateFeatureWithAttributes("local", "value1");
    var serverFeature = CreateFeatureWithAttributes("server", "value2");

    // Act
    var resolver = new CustomConflictResolver();
    var resolved = await resolver.ResolveConflictAsync(
        localFeature, serverFeature, new ConflictContext());

    // Assert
    Assert.AreEqual("value1", resolved.Attributes["local"]); // Local wins
}
```

## Performance Tips

1. **Batch Operations**: Group multiple saves/updates together
2. **Efficient Queries**: Use spatial and attribute indexing
3. **Background Processing**: Perform sync in background threads
4. **Smart Caching**: Cache frequently accessed data
5. **Connection Awareness**: Respect user's data plan and battery

## Troubleshooting

### Common Issues

**Sync failures**
- Check network connectivity
- Verify API credentials
- Review conflict resolution settings

**Storage issues**
- Monitor available storage space
- Implement proper cleanup routines
- Check file permissions

**Performance problems**
- Optimize query filters
- Reduce data transfer size
- Use background processing

## Best Practices

1. **Design for offline-first** from the start
2. **Provide clear sync status** to users
3. **Handle conflicts gracefully** with user input when needed
4. **Implement proper error handling** and retry logic
5. **Test extensively** in offline scenarios
6. **Monitor storage usage** and provide cleanup options

## Related Documentation

- [Mobile SDK Overview](../README.md)
- [Camera Integration](camera-integration.md)
- [Offline 3D Scene Packages](offline-3d-scene-packages.md)
- [Performance Guide](performance.md)
- [Troubleshooting](troubleshooting.md)
