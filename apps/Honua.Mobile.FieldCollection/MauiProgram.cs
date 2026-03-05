using CommunityToolkit.Maui;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Features;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.Services.Sync;
using Honua.Mobile.FieldCollection.ViewModels;
using Honua.Mobile.FieldCollection.Views;
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

        // Register storage service factory
        services.AddSingleton<IStorageService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            return databaseService.GetStorageServiceAsync().Result;
        });

        // Register sync service factory
        services.AddSingleton<ISyncService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            var authService = provider.GetRequiredService<IAuthenticationService>();
            var connectivityService = provider.GetRequiredService<IConnectivityService>();
            return databaseService.GetSyncServiceAsync(authService, connectivityService).Result;
        });

        // Core services (keeping mock implementations for navigation, location, auth)
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocationService, LocationService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // Feature services - real GeoPackage implementation
        services.AddSingleton<IFeatureService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            var syncService = provider.GetRequiredService<ISyncService>();
            var storageService = databaseService.GetStorageServiceAsync().Result;
            return new GeoPackageFeatureService(storageService, syncService);
        });

        // Other feature services (keeping mock implementations for now)
        services.AddSingleton<IFormService, FormService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();

        // Configuration services (keeping mock implementations)
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IConnectivityService, ConnectivityService>();

        // Diagnostic service for database management and system monitoring
        services.AddSingleton<DiagnosticService>();

        // Platform-specific services will be registered by platform startup
    }

    private static void RegisterViewModels(IServiceCollection services)
    {
        services.AddTransient<MainViewModel>();
        services.AddTransient<MapViewModel>();
        services.AddTransient<RecordsViewModel>();
        services.AddTransient<RecordDetailViewModel>();
        services.AddTransient<RecordEditViewModel>();
        services.AddTransient<SyncCenterViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AuthenticationViewModel>();
    }

    private static void RegisterViews(IServiceCollection services)
    {
        services.AddTransient<MainPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<RecordsPage>();
        services.AddTransient<RecordDetailPage>();
        services.AddTransient<RecordEditPage>();
        services.AddTransient<SyncCenterPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AuthenticationPage>();
    }
}