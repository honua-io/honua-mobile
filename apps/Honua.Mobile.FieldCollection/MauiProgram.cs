using CommunityToolkit.Maui;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Configuration;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Features;
using Honua.Mobile.FieldCollection.Services.Forms;
using Honua.Mobile.FieldCollection.Services.Metadata;
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
        services.AddHttpClient("HonuaFieldSync");
        services.AddSingleton<HonuaSdkFieldCollectionFeatureSyncClient>();
        services.AddSingleton<IFieldCollectionFeatureSyncClient>(provider =>
            provider.GetRequiredService<HonuaSdkFieldCollectionFeatureSyncClient>());
        services.AddSingleton<IFieldCollectionAttachmentSyncClient>(provider =>
            provider.GetRequiredService<HonuaSdkFieldCollectionFeatureSyncClient>());
        services.AddSingleton<IFieldCollectionChangeUploader>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            return new HonuaFieldCollectionChangeUploader(
                databaseService.GetStorageService(),
                provider.GetRequiredService<IFieldCollectionMetadataService>(),
                provider.GetRequiredService<IFieldCollectionFeatureSyncClient>(),
                provider.GetService<ILogger<HonuaFieldCollectionChangeUploader>>());
        });
        services.AddSingleton<IFieldCollectionChangePuller, HonuaFieldCollectionChangePuller>();
        services.AddSingleton<IFieldCollectionAttachmentSynchronizer>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            return new HonuaFieldCollectionAttachmentSynchronizer(
                databaseService.GetStorageService(),
                provider.GetRequiredService<IAttachmentService>(),
                provider.GetRequiredService<IFieldCollectionMetadataService>(),
                provider.GetRequiredService<IFieldCollectionAttachmentSyncClient>(),
                provider.GetService<ILogger<HonuaFieldCollectionAttachmentSynchronizer>>());
        });
        services.AddSingleton<ISyncService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            var authService = provider.GetRequiredService<IAuthenticationService>();
            var connectivityService = provider.GetRequiredService<IConnectivityService>();
            var changeUploader = provider.GetRequiredService<IFieldCollectionChangeUploader>();
            var changePuller = provider.GetRequiredService<IFieldCollectionChangePuller>();
            var attachmentSynchronizer = provider.GetRequiredService<IFieldCollectionAttachmentSynchronizer>();
            var logger = provider.GetRequiredService<ILogger<GeoPackageSyncService>>();
            var exceptionReporter = provider.GetRequiredService<IMobileExceptionReporter>();
            return databaseService.GetSyncService(
                authService,
                connectivityService,
                changeUploader,
                changePuller,
                attachmentSynchronizer,
                logger: logger,
                exceptionReporter: exceptionReporter);
        });

        // Core services
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ILocationService, LocationService>();
        services.AddHttpClient("HonuaFieldAuthentication");
        services.AddHttpClient("HonuaFieldMetadata");
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
        services.AddSingleton<IFieldCollectionMetadataService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var logger = provider.GetService<ILogger<FieldCollectionMetadataService>>();
            return new FieldCollectionMetadataService(
                provider.GetRequiredService<IAuthenticationService>(),
                provider.GetRequiredService<ISettingsService>(),
                databaseService.GetStorageService(),
                httpClientFactory.CreateClient("HonuaFieldMetadata"),
                logger);
        });

        // Other feature services
        services.AddSingleton<IFormService>(provider =>
            new FormService(provider.GetRequiredService<IFieldCollectionMetadataService>()));
        services.AddSingleton<IFormDraftService, SettingsFormDraftService>();
        services.AddSingleton<IAttachmentService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            return new AttachmentService(
                databaseService.GetStorageService(),
                logger: provider.GetService<ILogger<AttachmentService>>());
        });
        services.AddSingleton<ILocalRecordExportService>(provider =>
        {
            var databaseService = provider.GetRequiredService<DatabaseService>();
            return new LocalRecordExportService(
                databaseService.GetStorageService(),
                logger: provider.GetService<ILogger<LocalRecordExportService>>());
        });

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
        services.AddTransient<RecordDetailViewModel>();
        services.AddTransient<RecordEditViewModel>();
        services.AddTransient<AuthenticationViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddTransient<LayerSettingsViewModel>();
        services.AddTransient<FeatureDetailViewModel>();
        services.AddTransient<ConflictResolutionViewModel>();
        services.AddTransient<SyncHistoryViewModel>();
        services.AddTransient<ServerConfigViewModel>();
        services.AddTransient<UserProfileViewModel>();
        services.AddTransient<AboutViewModel>();
    }

    private static void RegisterViews(IServiceCollection services)
    {
        services.AddTransient<MainPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<RecordsPage>();
        services.AddTransient<SyncCenterPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<RecordDetailPage>();
        services.AddTransient<RecordEditPage>();
        services.AddTransient<AuthenticationPage>();
        services.AddTransient<DiagnosticsPage>();
        services.AddTransient<LayerSettingsPage>();
        services.AddTransient<FeatureDetailPage>();
        services.AddTransient<ConflictResolutionPage>();
        services.AddTransient<SyncHistoryPage>();
        services.AddTransient<ServerConfigPage>();
        services.AddTransient<UserProfilePage>();
        services.AddTransient<AboutPage>();
    }
}
