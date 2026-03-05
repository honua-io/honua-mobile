// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using HonuaFieldCollector.Features.Authentication;
using HonuaFieldCollector.Features.DataCollection;
using HonuaFieldCollector.Features.Mapping;
using HonuaFieldCollector.Features.Sync;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HonuaFieldCollector;

/// <summary>
/// Honua Field Collector - Revolutionary mobile data collection platform.
///
/// Features:
/// - Dynamic form generation from server schemas
/// - AR visualization for infrastructure inspection
/// - IoT sensor integration for automated data collection
/// - Offline-first with intelligent sync
/// - Professional photo documentation with privacy controls
/// </summary>
public partial class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;

        // Set the main page
        MainPage = new AppShell();

        // Initialize application services
        _ = InitializeAppAsync();
    }

    /// <summary>
    /// Initializes the application asynchronously.
    /// Sets up authentication, sync services, and offline data.
    /// </summary>
    private async Task InitializeAppAsync()
    {
        try
        {
            var logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            logger.LogInformation("Initializing Honua Field Collector v{Version}",
                AppInfo.VersionString);

            // Initialize authentication
            var authService = _serviceProvider.GetRequiredService<IAuthenticationService>();
            var isAuthenticated = await authService.TryAutoLoginAsync();

            if (!isAuthenticated)
            {
                logger.LogInformation("User not authenticated, redirecting to login");
                await Shell.Current.GoToAsync("//login");
            }
            else
            {
                logger.LogInformation("User authenticated successfully");

                // Initialize data services
                await InitializeDataServicesAsync();
            }
        }
        catch (Exception ex)
        {
            var logger = _serviceProvider.GetService<ILogger<App>>();
            logger?.LogError(ex, "Failed to initialize application");

            // Show error to user
            await ShowErrorAsync("Initialization Error",
                "Failed to start the application. Please check your network connection and try again.");
        }
    }

    /// <summary>
    /// Initializes data services including sync, mapping, and IoT.
    /// </summary>
    private async Task InitializeDataServicesAsync()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<App>>();

        // Initialize sync service
        var syncService = _serviceProvider.GetRequiredService<ISyncService>();
        await syncService.InitializeAsync();

        // Initialize mapping service
        var mapService = _serviceProvider.GetRequiredService<IMapService>();
        await mapService.InitializeAsync();

        // Initialize IoT service if available
        var iotService = _serviceProvider.GetService<IIoTIntegrationService>();
        if (iotService != null)
        {
            logger.LogInformation("Initializing IoT sensor integration");
            await iotService.InitializeAsync();
        }

        // Start background sync
        _ = Task.Run(async () => await syncService.StartBackgroundSyncAsync());

        logger.LogInformation("All data services initialized successfully");
    }

    /// <summary>
    /// Shows an error dialog to the user.
    /// </summary>
    private static async Task ShowErrorAsync(string title, string message)
    {
        if (Current?.MainPage != null)
        {
            await Current.MainPage.DisplayAlert(title, message, "OK");
        }
    }

    /// <summary>
    /// Handles application lifecycle events.
    /// </summary>
    protected override void OnSleep()
    {
        // Called when the app is put to sleep
        var logger = _serviceProvider.GetService<ILogger<App>>();
        logger?.LogInformation("Application entering sleep mode");

        // Pause location tracking to save battery
        var locationService = _serviceProvider.GetService<ILocationService>();
        locationService?.PauseTracking();

        // Trigger sync before sleeping
        var syncService = _serviceProvider.GetService<ISyncService>();
        _ = Task.Run(async () => await syncService?.TriggerSyncAsync());
    }

    protected override void OnResume()
    {
        // Called when the app resumes from sleep
        var logger = _serviceProvider.GetService<ILogger<App>>();
        logger?.LogInformation("Application resuming from sleep");

        // Resume location tracking
        var locationService = _serviceProvider.GetService<ILocationService>();
        locationService?.ResumeTracking();

        // Check for updates
        var syncService = _serviceProvider.GetService<ISyncService>();
        _ = Task.Run(async () => await syncService?.CheckForUpdatesAsync());
    }

    protected override void OnStart()
    {
        // Called when the app starts
        var logger = _serviceProvider.GetService<ILogger<App>>();
        logger?.LogInformation("Application starting");
    }

    /// <summary>
    /// Gets a service from the dependency injection container.
    /// </summary>
    public static T GetService<T>() where T : notnull
    {
        var app = Current as App;
        return app!._serviceProvider.GetRequiredService<T>();
    }

    /// <summary>
    /// Gets a service from the dependency injection container (nullable).
    /// </summary>
    public static T? GetOptionalService<T>() where T : class
    {
        var app = Current as App;
        return app?._serviceProvider.GetService<T>();
    }
}