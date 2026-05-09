using CommunityToolkit.Maui;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Configuration;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Features;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.Services.Sync;
using Honua.Mobile.Maui;
using Honua.Mobile.Maui.Diagnostics;
using Honua.Mobile.FieldCollection.ViewModels;
using Honua.Mobile.FieldCollection.Views;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.FieldCollection;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiMaps()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Configure logging
#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
        builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

        // Register core services
        RegisterServices(builder.Services);

        // Register view models
        RegisterViewModels(builder.Services);

        // Register views/pages
        RegisterViews(builder.Services);

        return builder.Build();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Database and Storage Services
        services.AddSingleton<DatabaseService>();

        services.AddSingleton<IStorageService, StorageService>();

        // Register sync service factory
        services.AddSingleton<ISyncService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            var authService = provider.GetRequiredService<IAuthenticationService>();
            var connectivityService = provider.GetRequiredService<IConnectivityService>();
            var logger = provider.GetRequiredService<ILogger<GeoPackageSyncService>>();
            var exceptionReporter = provider.GetRequiredService<IMobileExceptionReporter>();
            return databaseService.GetSyncService(
                authService,
                connectivityService,
                logger: logger,
                exceptionReporter: exceptionReporter);
        });

        // Core services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocationService, LocationService>();
        services.AddHttpClient("HonuaFieldAuthentication");
        services.AddSingleton<IAuthenticationService>(provider =>
        {
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var logger = provider.GetService<ILogger<AuthenticationService>>();
            return new AuthenticationService(
                httpClientFactory.CreateClient("HonuaFieldAuthentication"),
                logger);
        });

        // Feature services - real GeoPackage implementation
        services.AddSingleton<IFeatureService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            var syncService = provider.GetRequiredService<ISyncService>();
            var storageService = databaseService.GetStorageService();
            var logger = provider.GetRequiredService<ILogger<GeoPackageFeatureService>>();
            return new GeoPackageFeatureService(storageService, syncService, logger);
        });

        // Other feature services
        services.AddSingleton<IFormService, FormService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();

        // Configuration services
        var buildConfiguration = MobileBuildConfiguration.FromAssembly(
            typeof(App).Assembly,
            GetAppInfoValue(() => AppInfo.Current.VersionString, "unknown"),
            GetAppInfoValue(() => AppInfo.Current.BuildString, "unknown"));
        services.AddSingleton(buildConfiguration);
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IConnectivityService, ConnectivityService>();

        // Diagnostic service for database management and system monitoring
        services.AddSingleton<DiagnosticService>();
        services.AddSingleton<IMobileExceptionReportUploadRequestCustomizer, FieldCollectionExceptionReportAuthHeader>();
        services.AddHonuaMobileExceptionReporting(FieldCollectionExceptionReporting.FromPreferences(buildConfiguration));

        // Platform-specific services will be registered by platform startup
    }

    private static string GetAppInfoValue(Func<string> valueFactory, string fallback)
    {
        try
        {
            return valueFactory();
        }
        catch
        {
            return fallback;
        }
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        services.AddTransient<MainViewModel>();
        services.AddTransient<MapViewModel>();
        services.AddTransient<RecordsViewModel>();
        services.AddTransient<SyncCenterViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    private static void RegisterViews(IServiceCollection services)
    {
        services.AddTransient<MainPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<RecordsPage>();
        services.AddTransient<SyncCenterPage>();
        services.AddTransient<SettingsPage>();
    }
}
