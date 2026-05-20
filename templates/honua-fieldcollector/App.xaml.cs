using Microsoft.Extensions.Logging;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace HonuaFieldCollector;

public partial class App : Application
{
    private readonly ILogger<App> _logger;
    private readonly SemaphoreSlim _lifecycleSyncLock = new(1, 1);

    public App(ILogger<App> logger, MainPage mainPage)
    {
        InitializeComponent();
        _logger = logger;

        MainPage = mainPage;

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
            await TryRunLifecycleSyncAsync("sleep");
        });
    }

    protected override void OnResume()
    {
        base.OnResume();
        _logger.LogInformation("App resuming from background");

        // Resume operations
        try
        {
            _ = ResumeOperationsAsync();
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

            // Warm up Honua client registration
            var honuaClient = services.GetService<HonuaMobileClient>();
            if (honuaClient != null)
            {
                _logger.LogInformation("Honua mobile client registered for online sync");
            }

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

            await TryRunLifecycleSyncAsync("resume");

            _logger.LogDebug("Resume operations completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resume operations failed");
        }
    }

    private async Task TryRunLifecycleSyncAsync(string reason)
    {
        try
        {
            var services = Handler?.MauiContext?.Services;
            if (services == null)
            {
                return;
            }

            var orchestrator = services.GetService<BackgroundSyncOrchestrator>();
            if (orchestrator != null)
            {
                var result = await orchestrator.RunOnceIfOnlineAsync();
                if (result == null)
                {
                    _logger.LogDebug("Lifecycle sync skipped while offline during {Reason}", reason);
                    return;
                }

                _logger.LogInformation(
                    "Lifecycle sync during {Reason} inspected {Loaded} queued edit(s), succeeded {Succeeded}, failed {Failed}",
                    reason,
                    result.Loaded,
                    result.Succeeded,
                    result.Failed);
                return;
            }

            var syncRunner = services.GetService<IOfflineSyncRunner>();
            if (syncRunner == null)
            {
                return;
            }

            if (!await _lifecycleSyncLock.WaitAsync(0))
            {
                _logger.LogDebug("Lifecycle sync skipped during {Reason} because another sync is running", reason);
                return;
            }

            try
            {
                var result = await syncRunner.SyncAsync();
                _logger.LogInformation(
                    "Lifecycle sync during {Reason} inspected {Loaded} queued edit(s), succeeded {Succeeded}, failed {Failed}",
                    reason,
                    result.Loaded,
                    result.Succeeded,
                    result.Failed);
            }
            finally
            {
                _lifecycleSyncLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Lifecycle sync failed during {Reason}", reason);
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
