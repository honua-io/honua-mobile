using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Honua.Mobile.Core;
using Honua.Mobile.Storage;
using Honua.Mobile.Maui;
<!--#if (enableIoT)-->
using Honua.Mobile.IoT;
<!--#endif-->

namespace namespace;

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

        // Configure Honua Mobile SDK
        builder.Services.AddHonuaMobileSDK(config =>
        {
            // 🔗 Server Connection
            config.ServerEndpoint = "serverEndpoint";
            config.ApiKey = "apiKey";

            // 📱 Mobile Features
            config.EnableOfflineStorage = true;
            config.EnableLocationServices = true;
            config.EnableCameraIntegration = true;
<!--#if (enableIoT)-->
            config.EnableIoTSensors = true;
<!--#endif-->
<!--#if (enableAR)-->
            config.EnableAugmentedReality = true;
<!--#endif-->

            // 🔄 Sync Configuration
            config.AutoSyncInterval = TimeSpan.FromMinutes(5);
            config.MaxRetryAttempts = 3;
            config.SyncConflictResolution = ConflictResolution.LastWriterWins;

            // 📊 Logging
            config.LogLevel = LogLevel.Information;
        });

        // Additional Services
        builder.Services.AddSingleton<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Initialize Honua services
        _ = Task.Run(async () =>
        {
            try
            {
                var honuaClient = app.Services.GetRequiredService<IHonuaClient>();
                await honuaClient.InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Honua initialization error: {ex.Message}");
            }
        });

        return app;
    }
}