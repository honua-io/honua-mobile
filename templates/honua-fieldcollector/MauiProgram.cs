using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Offline.Abstractions;
using SdkOfflineSyncEngineOptions = Honua.Sdk.Offline.OfflineSyncEngineOptions;

namespace HonuaFieldCollector;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        var offlineDb = Path.Combine(FileSystem.Current.AppDataDirectory, "honua-fieldcollector.gpkg");

        builder.Services
            .AddSingleton<MainPage>()
            .AddHonuaMobileSdk(new HonuaMobileClientOptions
            {
                BaseUri = new Uri("HONUA_SERVER_ENDPOINT"),
                ApiKey = "YOUR_API_KEY_HERE",
            })
            .AddHonuaMobileFieldCollection()
            .AddHonuaSdkGeoPackageOfflineSync(
                new GeoPackageSyncStoreOptions
                {
                    DatabasePath = offlineDb,
                    DefaultFeatureCacheTtl = TimeSpan.FromDays(7),
                },
                CreateOfflinePackageManifest(),
                new SdkOfflineSyncEngineOptions
                {
                    BatchSize = 50,
                    MaxAttempts = 3,
                    ConflictStrategy = OfflineConflictStrategy.ManualReview,
                })
            .AddHonuaBackgroundSync(new BackgroundSyncOrchestratorOptions
            {
                SyncInterval = TimeSpan.FromMinutes(5),
                RunImmediately = false,
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static OfflinePackageManifest CreateOfflinePackageManifest()
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
                    OutFields =
                    [
                        "objectid",
                        "globalid",
                        "site_name",
                        "status",
                        "priority",
                        "assigned_to",
                        "inspection_date",
                        "sync_version",
                        "offline_action",
                        "notes"
                    ],
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
