using Honua.Mobile.Maui;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var offlineDb = Path.Combine(FileSystem.Current.AppDataDirectory, "honua-sync-store.gpkg");

        builder.Services
            .AddSingleton<MainPage>()
            .AddHonuaMobileSdk(new HonuaMobileClientOptions
            {
                BaseUri = new Uri("https://api.honua.io"),
            })
            .AddHonuaApiOfflineUploader()
            .AddHonuaMobileFieldCollection()
            .AddHonuaGeoPackageOfflineSync(
                new GeoPackageSyncStoreOptions { DatabasePath = offlineDb },
                new OfflineSyncEngineOptions
                {
                    BatchSize = 50,
                    ConflictStrategy = SyncConflictStrategy.ManualReview,
                })
            .AddHonuaMapAreaDownload()
            .AddHonuaBackgroundSync(new BackgroundSyncOrchestratorOptions
            {
                SyncInterval = TimeSpan.FromMinutes(2),
                RunImmediately = false,
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
