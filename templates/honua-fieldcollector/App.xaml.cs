using Microsoft.Extensions.Logging;

namespace namespace;

public partial class App : Application
{
    private readonly ILogger<App> _logger;

    public App(ILogger<App> logger)
    {
        InitializeComponent();
        _logger = logger;

        MainPage = new AppShell();

        // Handle global exceptions
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _logger.LogInformation("YOUR_COMPANY_NAME Field Collection App started");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        // Configure window properties
        window.Title = "YOUR_COMPANY_NAME Field Collection";

        // Set window size for desktop platforms
#if WINDOWS
        window.Width = 1200;
        window.Height = 800;
        window.MinimumWidth = 800;
        window.MinimumHeight = 600;
#endif

        return window;
    }

    protected override async void OnStart()
    {
        base.OnStart();
        _logger.LogInformation("App started");

        // Perform startup tasks
        try
        {
            // Pre-warm services
            await WarmupServicesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during app startup");
        }
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _logger.LogInformation("App entering background");

        // Save any pending data
        _ = Task.Run(async () =>
        {
            try
            {
                // Auto-save drafts and sync if possible
                var services = Handler?.MauiContext?.Services;
                if (services != null)
                {
                    var client = services.GetService<Honua.Mobile.Core.IHonuaClient>();
                    await client?.SavePendingChangesAsync()!;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving data on sleep");
            }
        });
    }

    protected override async void OnResume()
    {
        base.OnResume();
        _logger.LogInformation("App resuming from background");

        // Resume operations
        try
        {
            await ResumeOperationsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during app resume");
        }
    }

    private async Task WarmupServicesAsync()
    {
        try
        {
            // Get services from DI container
            var services = Handler?.MauiContext?.Services;
            if (services == null) return;

            // Warm up Honua client
            var honuaClient = services.GetService<Honua.Mobile.Core.IHonuaClient>();
            if (honuaClient != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await honuaClient.TestConnectionAsync();
                        _logger.LogInformation("Honua client connection verified");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Honua client connection failed - will work offline");
                    }
                });
            }

<!--#if (enableIoT)-->
            // Start IoT sensor discovery
            var iotService = services.GetService<Honua.Mobile.IoT.IIoTSensorService>();
            if (iotService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await iotService.StartSensorDiscoveryAsync(Honua.Mobile.IoT.SensorType.Environmental);
                        _logger.LogInformation("IoT sensor discovery started");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "IoT sensor discovery failed");
                    }
                });
            }
<!--#endif-->

            _logger.LogDebug("Service warmup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service warmup failed");
        }
    }

    private async Task ResumeOperationsAsync()
    {
        try
        {
            var services = Handler?.MauiContext?.Services;
            if (services == null) return;

            // Check for pending sync
            var syncService = services.GetService<Honua.Mobile.Storage.ISyncManager>();
            if (syncService != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var pendingCount = await syncService.GetPendingChangesCountAsync();
                        if (pendingCount > 0)
                        {
                            _logger.LogInformation("Resuming sync for {PendingCount} changes", pendingCount);
                            await syncService.SyncAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background sync failed");
                    }
                });
            }

            _logger.LogDebug("Resume operations completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume operations failed");
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger.LogCritical(exception, "Unhandled exception occurred");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Unobserved task exception occurred");
        e.SetObserved(); // Prevent app crash
    }
}