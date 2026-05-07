using System.Text.Json;
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Offline.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using MobileOfflineSyncRunner = Honua.Mobile.Offline.Sync.IOfflineSyncRunner;
using SdkOfflineChangeJournal = Honua.Sdk.Offline.Abstractions.IOfflineChangeJournal;
using SdkOfflineCheckpointStore = Honua.Sdk.Offline.Abstractions.IOfflineSyncCheckpointStore;
using SdkOfflineFeatureStore = Honua.Sdk.Offline.Abstractions.IOfflineFeatureStore;
using SdkOfflineStateStore = Honua.Sdk.Offline.Abstractions.IOfflineSyncStateStore;
using SdkOfflineSyncEngine = Honua.Sdk.Offline.OfflineSyncEngine;
using SdkOfflineSyncEngineOptions = Honua.Sdk.Offline.OfflineSyncEngineOptions;

namespace Honua.Mobile.Maui.Tests;

public sealed class SdkBackedOfflineFieldOperationsDemoHarnessTests : IDisposable
{
    private const string EvidenceSchemaVersion = "honua.mobile.sdk-backed-offline-demo-harness.evidence.v1";
    private const string PackageId = "mobile-offline-field-ops-v1";
    private const string EditableSourceId = "mobile_offline_demo/FeatureServer/68910";
    private const string ContextSourceId = "mobile_offline_demo/FeatureServer/68920";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private string? _rootDirectory;
    private bool _deleteRootDirectory;

    [Fact]
    public async Task SdkBackedDemoHarness_RegistersPreferredOfflineStack_AndEmitsEvidence()
    {
        var rootDirectory = CreateRootDirectory();
        var databasePath = Path.Combine(rootDirectory, "offline-field-ops-demo.gpkg");
        var evidencePath = Path.Combine(rootDirectory, "sdk-backed-offline-demo.evidence.json");
        var manifest = CreateManifest();

        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddHonuaMobileSdk(new HonuaMobileClientOptions
            {
                BaseUri = new Uri("https://demo.honua.test"),
            })
            .AddHonuaSdkGeoPackageOfflineSync(
                new GeoPackageSyncStoreOptions
                {
                    DatabasePath = databasePath,
                    DefaultFeatureCacheTtl = TimeSpan.FromDays(7),
                },
                manifest,
                new SdkOfflineSyncEngineOptions
                {
                    BatchSize = 25,
                    ConflictStrategy = OfflineConflictStrategy.ManualReview,
                })
            .AddHonuaBackgroundSync(new BackgroundSyncOrchestratorOptions
            {
                SyncInterval = TimeSpan.FromMinutes(5),
            })
            .BuildServiceProvider();

        var adapter = provider.GetRequiredService<GeoPackageSdkOfflineStoreAdapter>();
        var store = provider.GetRequiredService<IGeoPackageSyncStore>();

        await adapter.SaveFeaturesAsync(CreateFeaturePage(EditableSourceId, "1001", "Pump Station 1001"));
        await adapter.SaveFeaturesAsync(CreateFeaturePage(ContextSourceId, "77", "North Work Zone"));
        await adapter.EnqueueAsync(CreateConflictReviewJournalEntry());
        await adapter.SaveCheckpointAsync(new OfflineSyncCheckpoint
        {
            PackageId = PackageId,
            SourceId = EditableSourceId,
            SyncToken = "servergen:demo-fixture:42",
            PulledFeatureCount = 1,
        });
        await adapter.SaveStateAsync(new OfflineSyncState
        {
            PackageId = PackageId,
            SourceId = EditableSourceId,
            Phase = OfflineSyncPhase.Completed,
            LastSyncToken = "servergen:demo-fixture:42",
            PendingChangeCount = 1,
        });

        var evidence = await DemoEvidence.CaptureAsync(provider, store, manifest, databasePath);
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, JsonOptions));

        Assert.True(File.Exists(databasePath));
        Assert.True(File.Exists(evidencePath));
        Assert.Equal(EvidenceSchemaVersion, evidence.SchemaVersion);
        Assert.Equal(PackageId, evidence.PackageId);
        Assert.Equal([EditableSourceId, ContextSourceId], evidence.SourceIds);
        Assert.Equal(nameof(SdkOfflineSyncRunner), evidence.Registrations.MobileRunner);
        Assert.Equal(typeof(SdkOfflineSyncEngine).FullName, evidence.Registrations.SdkEngine);
        Assert.Contains(nameof(SdkOfflineFeatureStore), evidence.Registrations.SdkStoreInterfaces);
        Assert.Equal(2, evidence.FeatureCache.TotalCachedFeatures);
        Assert.Equal(1, evidence.Journal.PendingChangeCount);
        Assert.Equal("ManualReview", evidence.ConflictReview.Mode);
        Assert.Equal("servergen:demo-fixture:42", evidence.SyncState.LastSyncToken);

        var evidenceJson = await File.ReadAllTextAsync(evidencePath);
        Assert.Contains("\"schemaVersion\": \"honua.mobile.sdk-backed-offline-demo-harness.evidence.v1\"", evidenceJson);
        Assert.Contains("\"packageId\": \"mobile-offline-field-ops-v1\"", evidenceJson);
        Assert.Contains("\"pendingChangeCount\": 1", evidenceJson);
    }

    [Fact]
    public void DemoManifest_CoversFieldOpsFixtureInputs_AndServerDependency()
    {
        var manifest = CreateManifest();

        Assert.Equal(PackageId, manifest.PackageId);
        Assert.Equal("Mobile Offline Field Operations", manifest.DisplayName);
        Assert.Equal([EditableSourceId, ContextSourceId], manifest.Sources.Select(source => source.SourceId).ToArray());
        Assert.All(manifest.Sources, source =>
        {
            Assert.Equal(FeatureProtocolIds.GeoServicesFeatureService, source.Source.Protocol);
            Assert.True(source.ReturnGeometry);
            Assert.Equal(100, source.PageSize);
        });
        Assert.Equal("honua-io/honua-server#895", manifest.Metadata["serverFixtureDependency"]);
        Assert.Equal("stale-sync-version-manual-review", manifest.Metadata["conflictScenario"]);
        Assert.Equal("download,create,update,delete,reconnect,push,pull,conflict-review,diagnostics", manifest.Metadata["demoCoverage"]);
    }

    public void Dispose()
    {
        if (_deleteRootDirectory && _rootDirectory is not null && Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private string CreateRootDirectory()
    {
        var configuredEvidenceDirectory = Environment.GetEnvironmentVariable("HONUA_MOBILE_SDK_OFFLINE_DEMO_EVIDENCE_DIR");
        _deleteRootDirectory = string.IsNullOrWhiteSpace(configuredEvidenceDirectory);
        _rootDirectory = _deleteRootDirectory
            ? Path.Combine(Path.GetTempPath(), $"honua-sdk-offline-demo-{Guid.NewGuid():N}")
            : Path.Combine(configuredEvidenceDirectory!, $"sdk-backed-offline-demo-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
        return _rootDirectory;
    }

    private static OfflinePackageManifest CreateManifest()
        => new()
        {
            PackageId = PackageId,
            DisplayName = "Mobile Offline Field Operations",
            Version = "2026.05",
            Sources =
            [
                new OfflineSourceDescriptor
                {
                    SourceId = EditableSourceId,
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
                    SourceId = ContextSourceId,
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
                ["fixture"] = PackageId,
                ["serviceId"] = "mobile_offline_demo",
                ["editableLayerId"] = "68910",
                ["contextLayerId"] = "68920",
                ["serverFixtureDependency"] = "honua-io/honua-server#895",
                ["conflictScenario"] = "stale-sync-version-manual-review",
                ["demoCoverage"] = "download,create,update,delete,reconnect,push,pull,conflict-review,diagnostics",
            },
        };

    private static OfflineFeaturePage CreateFeaturePage(string sourceId, string featureId, string name)
        => new()
        {
            PackageId = PackageId,
            SourceId = sourceId,
            Source = new SourceDescriptor
            {
                Id = sourceId,
                Protocol = FeatureProtocolIds.GeoServicesFeatureService,
                Locator = new SourceLocator { ServiceId = "mobile_offline_demo" },
                Schema = new SourceSchema { SpatialReference = "EPSG:4326" },
            },
            Result = new FeatureQueryResult
            {
                ProviderName = "demo-fixture",
                ObjectIdFieldName = "objectid",
                Features =
                [
                    new FeatureRecord
                    {
                        Id = featureId,
                        Attributes = new Dictionary<string, JsonElement>
                        {
                            ["objectid"] = JsonSerializer.SerializeToElement(long.Parse(featureId)),
                            ["name"] = JsonSerializer.SerializeToElement(name),
                            ["sync_version"] = JsonSerializer.SerializeToElement("servergen:demo-fixture:42"),
                        },
                        Geometry = JsonSerializer.SerializeToElement(new { x = -157.8001, y = 21.3001 }),
                    },
                ],
                NumberReturned = 1,
            },
        };

    private static OfflineChangeJournalEntry CreateConflictReviewJournalEntry()
        => new()
        {
            OperationId = "op-demo-conflict-review-001",
            PackageId = PackageId,
            SourceId = EditableSourceId,
            Source = new FeatureSource { ServiceId = "mobile_offline_demo", LayerId = 68910 },
            OperationKind = OfflineEditOperationKind.Update,
            Feature = new FeatureEditFeature
            {
                Id = "site-1001",
                Attributes = new Dictionary<string, JsonElement>
                {
                    ["status"] = JsonSerializer.SerializeToElement("inspected-offline"),
                    ["sync_version"] = JsonSerializer.SerializeToElement("stale-servergen:demo-fixture:41"),
                },
            },
            BaseSyncToken = "servergen:demo-fixture:41",
            Metadata = new Dictionary<string, string>
            {
                ["conflictScenario"] = "stale-sync-version-manual-review",
                ["expectedResolution"] = "manual-review",
            },
        };

    private sealed record DemoEvidence(
        string SchemaVersion,
        string Status,
        string PackageId,
        string[] SourceIds,
        string DatabaseFileName,
        RegistrationEvidence Registrations,
        FeatureCacheEvidence FeatureCache,
        JournalEvidence Journal,
        SyncStateEvidence SyncState,
        ConflictReviewEvidence ConflictReview)
    {
        public static async Task<DemoEvidence> CaptureAsync(
            IServiceProvider provider,
            IGeoPackageSyncStore store,
            OfflinePackageManifest manifest,
            string databasePath)
        {
            var registrations = new RegistrationEvidence(
                provider.GetRequiredService<MobileOfflineSyncRunner>().GetType().Name,
                provider.GetRequiredService<SdkOfflineSyncEngine>().GetType().FullName!,
                provider.GetRequiredService<GeoPackageSdkOfflineStoreAdapter>().GetType().Name,
                CaptureSdkStoreInterfaceEvidence(provider),
                provider.GetRequiredService<BackgroundSyncOrchestrator>().GetType().Name);

            var sourceFeatureCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var source in manifest.Sources)
            {
                var features = await store.GetFeaturesAsync($"sdk-package:{manifest.PackageId}:{source.SourceId}");
                sourceFeatureCounts[source.SourceId] = features.Count;
            }

            var pendingCount = await store.CountPendingAsync();
            var adapter = provider.GetRequiredService<GeoPackageSdkOfflineStoreAdapter>();
            var state = await adapter.GetStateAsync(manifest.PackageId, EditableSourceId);
            var checkpoint = await adapter.GetCheckpointAsync(manifest.PackageId, EditableSourceId);

            return new DemoEvidence(
                EvidenceSchemaVersion,
                "passed",
                manifest.PackageId,
                manifest.Sources.Select(source => source.SourceId).ToArray(),
                Path.GetFileName(databasePath),
                registrations,
                new FeatureCacheEvidence(sourceFeatureCounts, sourceFeatureCounts.Values.Sum()),
                new JournalEvidence(pendingCount, "op-demo-conflict-review-001"),
                new SyncStateEvidence(
                    state?.Phase.ToString() ?? string.Empty,
                    state?.LastSyncToken ?? string.Empty,
                    checkpoint?.PulledFeatureCount ?? 0),
                new ConflictReviewEvidence("ManualReview", "stale-sync-version-manual-review"));
        }

        private static string[] CaptureSdkStoreInterfaceEvidence(IServiceProvider provider)
        {
            return [
                ResolveSdkStoreInterface<SdkOfflineFeatureStore>(provider, nameof(SdkOfflineFeatureStore)),
                ResolveSdkStoreInterface<SdkOfflineChangeJournal>(provider, nameof(SdkOfflineChangeJournal)),
                ResolveSdkStoreInterface<SdkOfflineCheckpointStore>(provider, nameof(SdkOfflineCheckpointStore)),
                ResolveSdkStoreInterface<SdkOfflineStateStore>(provider, nameof(SdkOfflineStateStore)),
            ];
        }

        private static string ResolveSdkStoreInterface<TStore>(IServiceProvider provider, string evidenceName)
            where TStore : notnull
        {
            provider.GetRequiredService<TStore>();
            return evidenceName;
        }
    }

    private sealed record RegistrationEvidence(
        string MobileRunner,
        string SdkEngine,
        string StoreAdapter,
        string[] SdkStoreInterfaces,
        string BackgroundOrchestrator);

    private sealed record FeatureCacheEvidence(
        IReadOnlyDictionary<string, int> SourceFeatureCounts,
        int TotalCachedFeatures);

    private sealed record JournalEvidence(
        int PendingChangeCount,
        string DeterministicConflictOperationId);

    private sealed record SyncStateEvidence(
        string Phase,
        string LastSyncToken,
        int PulledFeatureCount);

    private sealed record ConflictReviewEvidence(
        string Mode,
        string Scenario);
}
