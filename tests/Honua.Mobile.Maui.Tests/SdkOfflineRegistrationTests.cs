using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Abstractions.Routing;
using Honua.Sdk.Offline.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SdkFeatureClient = Honua.Mobile.Sdk.Features.HonuaMobileSdkFeatureClient;
using SdkRoutingClient = Honua.Sdk.GeoServices.Routing.HonuaRoutingClient;
using MobileOfflineSyncRunner = Honua.Mobile.Offline.Sync.IOfflineSyncRunner;
using SdkOfflineFeatureStore = Honua.Sdk.Offline.Abstractions.IOfflineFeatureStore;

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

            Assert.IsType<SdkOfflineSyncRunner>(runner);
            Assert.IsType<GeoPackageSdkOfflineStoreAdapter>(provider.GetRequiredService<SdkOfflineFeatureStore>());
            Assert.IsType<SdkFeatureClient>(provider.GetRequiredService<IHonuaFeatureQueryClient>());
            Assert.IsType<SdkFeatureClient>(provider.GetRequiredService<IHonuaFeatureEditClient>());
            Assert.IsType<SdkFeatureClient>(provider.GetRequiredService<IHonuaFeatureAttachmentClient>());
            Assert.IsType<HonuaMobileSdkFeatureClient>(provider.GetRequiredService<HonuaMobileSdkFeatureClient>());
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static OfflinePackageManifest CreateManifest()
        => new()
        {
            PackageId = "area-1",
            Sources =
            [
                new OfflineSourceDescriptor
                {
                    SourceId = "parks",
                    Source = new SourceDescriptor
                    {
                        Id = "parks",
                        Protocol = FeatureProtocolIds.OgcFeatures,
                        Locator = new SourceLocator { CollectionId = "parks" },
                    },
                },
            ],
        };
}
