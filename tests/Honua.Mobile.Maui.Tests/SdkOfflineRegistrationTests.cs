using Honua.Mobile.Field.Capture;
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Abstractions.Routing;
using Honua.Sdk.Field.Records;
using Honua.Sdk.Offline.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SdkFeatureClient = Honua.Mobile.Sdk.Features.HonuaMobileSdkFeatureClient;
using SdkRoutingClient = Honua.Sdk.GeoServices.Routing.HonuaRoutingClient;
using MobileOfflineSyncRunner = Honua.Mobile.Offline.Sync.IOfflineSyncRunner;
using SdkOfflineChangeJournal = Honua.Sdk.Offline.Abstractions.IOfflineChangeJournal;
using SdkOfflineCheckpointStore = Honua.Sdk.Offline.Abstractions.IOfflineSyncCheckpointStore;
using SdkOfflineFeatureStore = Honua.Sdk.Offline.Abstractions.IOfflineFeatureStore;
using SdkOfflineStateStore = Honua.Sdk.Offline.Abstractions.IOfflineSyncStateStore;
using SdkOfflineSyncEngine = Honua.Sdk.Offline.OfflineSyncEngine;

namespace Honua.Mobile.Maui.Tests;

public sealed class SdkOfflineRegistrationTests
{
    [Fact]
    public void AddHonuaRouting_RegistersSdkRoutingClientAndAbstraction()
    {
        using var provider = new ServiceCollection()
            .AddHonuaMobileSdk(new HonuaMobileClientOptions
            {
                BaseUri = new Uri("https://example.honua.test"),
            })
            .AddHonuaRouting()
            .BuildServiceProvider();

        var concrete = provider.GetRequiredService<SdkRoutingClient>();
        var abstraction = provider.GetRequiredService<IHonuaRoutingClient>();

        Assert.Same(concrete, abstraction);
    }

    [Fact]
    public void AddHonuaSdkGeoPackageOfflineSync_RegistersSdkBackedRunnerAndAdapters()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"honua-sdk-offline-di-{Guid.NewGuid():N}.gpkg");
        try
        {
            using var provider = new ServiceCollection()
                .AddLogging()
                .AddHonuaMobileSdk(new HonuaMobileClientOptions
                {
                    BaseUri = new Uri("https://example.honua.test"),
                })
                .AddHonuaSdkGeoPackageOfflineSync(
                    new GeoPackageSyncStoreOptions { DatabasePath = databasePath },
                    CreateManifest())
                .BuildServiceProvider();

            var runner = provider.GetRequiredService<MobileOfflineSyncRunner>();
            var adapter = provider.GetRequiredService<GeoPackageSdkOfflineStoreAdapter>();
            var manifest = provider.GetRequiredService<OfflinePackageManifest>();

            Assert.IsType<SdkOfflineSyncRunner>(runner);
            Assert.NotNull(provider.GetRequiredService<SdkOfflineSyncEngine>());
            Assert.Same(adapter, provider.GetRequiredService<SdkOfflineFeatureStore>());
            Assert.Same(adapter, provider.GetRequiredService<SdkOfflineChangeJournal>());
            Assert.Same(adapter, provider.GetRequiredService<SdkOfflineCheckpointStore>());
            Assert.Same(adapter, provider.GetRequiredService<SdkOfflineStateStore>());
            Assert.IsType<SdkFeatureClient>(provider.GetRequiredService<IHonuaFeatureQueryClient>());
            Assert.IsType<SdkFeatureClient>(provider.GetRequiredService<IHonuaFeatureEditClient>());
            Assert.IsType<SdkFeatureClient>(provider.GetRequiredService<IHonuaFeatureAttachmentClient>());
#pragma warning disable CS0618 // asserting back-compat shim registration
            Assert.IsType<HonuaMobileSdkFeatureClient>(provider.GetRequiredService<HonuaMobileSdkFeatureClient>());
#pragma warning restore CS0618
            Assert.Equal("mobile-offline-field-ops-v1", manifest.PackageId);
            Assert.Equal(
                ["mobile_offline_demo/FeatureServer/68910", "mobile_offline_demo/FeatureServer/68920"],
                manifest.Sources.Select(source => source.SourceId).ToArray());
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public void AddHonuaMobileFieldCollection_RegistersSdkBackedFieldWorkflow()
    {
        using var provider = new ServiceCollection()
            .AddHonuaMobileFieldCollection()
            .BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<DuplicateDetector>());
        Assert.NotNull(provider.GetRequiredService<MobileFieldCaptureWorkflow>());
    }

    private static OfflinePackageManifest CreateManifest()
        => new()
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
}
