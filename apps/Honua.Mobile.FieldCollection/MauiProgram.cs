using CommunityToolkit.Maui;
using Honua.Mobile.FieldCollection.Services;
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
        // Core services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocationService, LocationService>();
        services.AddSingleton<IStorageService, StorageService>();
        services.AddSingleton<ISyncService, SyncService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // Feature services
        services.AddSingleton<IFeatureService, FeatureService>();
        services.AddSingleton<IFormService, FormService>();
        services.AddSingleton<IAttachmentService, AttachmentService>();

        // Configuration services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IConnectivityService, ConnectivityService>();

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