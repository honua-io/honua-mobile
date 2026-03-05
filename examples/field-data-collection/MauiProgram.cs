// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using CommunityToolkit.Maui;
using HonuaFieldCollector.Features.Authentication;
using HonuaFieldCollector.Features.Camera;
using HonuaFieldCollector.Features.DataCollection;
using HonuaFieldCollector.Features.Forms;
using HonuaFieldCollector.Features.Mapping;
using HonuaFieldCollector.Features.OfflineStorage;
using HonuaFieldCollector.Features.Sync;
using HonuaFieldCollector.Platforms;
using HonuaFieldCollector.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace HonuaFieldCollector;

/// <summary>
/// MAUI program configuration for Honua Field Collector.
/// Configures all services, dependencies, and platform-specific handlers.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMaps()
            .UseMauiCommunityToolkitCamera()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
            });

        // Add configuration
        AddConfiguration(builder);

        // Add logging
        AddLogging(builder);

        // Add core services
        AddCoreServices(builder);

        // Add feature services
        AddFeatureServices(builder);

        // Add platform-specific services
        AddPlatformServices(builder);

        // Add Honua SDK services
        AddHonuaSdkServices(builder);

        // Configure handlers
        ConfigureHandlers(builder);

        return builder.Build();
    }

    /// <summary>
    /// Adds configuration from embedded resources and platform-specific sources.
    /// </summary>
    private static void AddConfiguration(MauiAppBuilder builder)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("HonuaFieldCollector.appsettings.json");

        if (stream != null)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .AddEnvironmentVariables()
                .Build();

            builder.Configuration.AddConfiguration(configuration);
        }

        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
    }

    /// <summary>
    /// Configures logging for development and production scenarios.
    /// </summary>
    private static void AddLogging(MauiAppBuilder builder)
    {
#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
        builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

        // Add file logging for production debugging
        builder.Logging.AddProvider(new FileLoggerProvider());

        builder.Services.AddLogging();
    }

    /// <summary>
    /// Adds core application services.
    /// </summary>
    private static void AddCoreServices(MauiAppBuilder builder)
    {
        // HTTP clients
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient("HonuaApi", client =>
        {
            var baseUrl = builder.Configuration["Honua:ServerUrl"] ?? "https://api.honua.com";
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("User-Agent", $"HonuaFieldCollector/{AppInfo.VersionString}");
        });

        // Essential services
        builder.Services.AddSingleton<IConnectivity>(Connectivity.Current);
        builder.Services.AddSingleton<IGeolocation>(Geolocation.Default);
        builder.Services.AddSingleton<IMediaPicker>(MediaPicker.Default);
        builder.Services.AddSingleton<IPreferences>(Preferences.Default);
        builder.Services.AddSingleton<ISecureStorage>(SecureStorage.Default);

        // Application services
        builder.Services.AddSingleton<IApplicationConfigurationService, ApplicationConfigurationService>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<IPermissionService, PermissionService>();
    }

    /// <summary>
    /// Adds feature-specific services for field data collection.
    /// </summary>
    private static void AddFeatureServices(MauiAppBuilder builder)
    {
        // Authentication
        builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
        builder.Services.AddSingleton<ITokenService, TokenService>();

        // Data collection
        builder.Services.AddScoped<IDataCollectionService, DataCollectionService>();
        builder.Services.AddScoped<IFormService, FormService>();
        builder.Services.AddScoped<IValidationService, ValidationService>();

        // Forms and schema
        builder.Services.AddSingleton<IFormSchemaService, FormSchemaService>();
        builder.Services.AddScoped<IFormBuilderService, FormBuilderService>();

        // Offline storage
        builder.Services.AddSingleton<IOfflineStorageService, GeoPackageStorageService>();
        builder.Services.AddSingleton<IDataCacheService, DataCacheService>();

        // Sync services
        builder.Services.AddSingleton<ISyncService, SyncService>();
        builder.Services.AddScoped<IConflictResolutionService, ConflictResolutionService>();

        // Camera and photos
        builder.Services.AddSingleton<ICameraService, CameraService>();
        builder.Services.AddSingleton<IPhotoService, PhotoService>();
        builder.Services.AddSingleton<IPrivacyService, PrivacyService>();

        // Mapping and location
        builder.Services.AddSingleton<IMapService, MapService>();
        builder.Services.AddSingleton<ILocationService, LocationService>();
        builder.Services.AddSingleton<ISpatialService, SpatialService>();

        // Background services
        builder.Services.AddSingleton<IBackgroundSyncService, BackgroundSyncService>();
        builder.Services.AddSingleton<ILocationTrackingService, LocationTrackingService>();
    }

    /// <summary>
    /// Adds platform-specific services and handlers.
    /// </summary>
    private static void AddPlatformServices(MauiAppBuilder builder)
    {
#if ANDROID
        builder.Services.AddSingleton<IPlatformLocationService, Platforms.Android.AndroidLocationService>();
        builder.Services.AddSingleton<IPlatformCameraService, Platforms.Android.AndroidCameraService>();
        builder.Services.AddSingleton<IPlatformMapService, Platforms.Android.AndroidMapService>();
#elif IOS
        builder.Services.AddSingleton<IPlatformLocationService, Platforms.iOS.IOSLocationService>();
        builder.Services.AddSingleton<IPlatformCameraService, Platforms.iOS.IOSCameraService>();
        builder.Services.AddSingleton<IPlatformMapService, Platforms.iOS.IOSMapService>();
#elif WINDOWS
        builder.Services.AddSingleton<IPlatformLocationService, Platforms.Windows.WindowsLocationService>();
        builder.Services.AddSingleton<IPlatformCameraService, Platforms.Windows.WindowsCameraService>();
        builder.Services.AddSingleton<IPlatformMapService, Platforms.Windows.WindowsMapService>();
#endif
    }

    /// <summary>
    /// Adds Honua SDK services for gRPC, AR, and IoT integration.
    /// </summary>
    private static void AddHonuaSdkServices(MauiAppBuilder builder)
    {
        // gRPC client services
        builder.Services.AddGrpcClient<Proto.FeatureService.FeatureServiceClient>(options =>
        {
            var serverUrl = builder.Configuration["Honua:GrpcUrl"] ?? "https://grpc.honua.com";
            options.Address = new Uri(serverUrl);
        });

        // Honua SDK services (if available)
        try
        {
            // AR services
            builder.Services.AddSingleton<IARVisualizationService, ARVisualizationService>();
            builder.Services.AddSingleton<IUtilityVisualizationService, UtilityVisualizationService>();

            // IoT services
            builder.Services.AddSingleton<IIoTIntegrationService, IoTIntegrationService>();
            builder.Services.AddSingleton<ISensorDataService, SensorDataService>();
        }
        catch (Exception ex)
        {
            // SDK services not available - log but continue
            var logger = new ConsoleLoggerProvider().CreateLogger("MauiProgram");
            logger.LogWarning(ex, "Some Honua SDK services are not available");
        }
    }

    /// <summary>
    /// Configures custom handlers for enhanced UI components.
    /// </summary>
    private static void ConfigureHandlers(MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID || IOS
            // Custom map handler for enhanced mapping features
            handlers.AddHandler<IMapView, MapViewHandler>();

            // Custom camera handler for professional photo capture
            handlers.AddHandler<ICameraView, CameraViewHandler>();

            // Custom form handlers for dynamic forms
            handlers.AddHandler<IDynamicFormView, DynamicFormViewHandler>();
#endif
        });
    }
}

/// <summary>
/// Simple file logger for production debugging.
/// </summary>
public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;

    public FileLoggerProvider()
    {
        _logDirectory = Path.Combine(FileSystem.AppDataDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _logDirectory);
    }

    public void Dispose() { }
}

/// <summary>
/// File logger implementation.
/// </summary>
public class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _logFilePath;

    public FileLogger(string categoryName, string logDirectory)
    {
        _categoryName = categoryName;
        var fileName = $"honua-field-{DateTime.Now:yyyy-MM-dd}.log";
        _logFilePath = Path.Combine(logDirectory, fileName);
    }

    public IDisposable BeginScope<TState>(TState state) => null!;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] [{_categoryName}] {formatter(state, exception)}";
        if (exception != null)
        {
            message += Environment.NewLine + exception.ToString();
        }

        try
        {
            File.AppendAllText(_logFilePath, message + Environment.NewLine);
        }
        catch
        {
            // Ignore file write errors
        }
    }
}