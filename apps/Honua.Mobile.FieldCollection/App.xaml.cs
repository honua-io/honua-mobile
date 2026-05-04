using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.FieldCollection;

public partial class App : Application
{
    private readonly IMobileExceptionReporter _exceptionReporter;
    private readonly ILogger<App>? _logger;

    public App(IMobileExceptionReporter exceptionReporter, ILogger<App>? logger = null)
    {
        _exceptionReporter = exceptionReporter;
        _logger = logger;

        InitializeComponent();
        RegisterExceptionHandlers();
        _ = Task.Run(() => _exceptionReporter.FlushPendingAsync());
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
            Title = "Honua Field Collection"
        };

#if WINDOWS || MACCATALYST
        window.MinimumHeight = 600;
        window.MinimumWidth = 800;
#endif

        return window;
    }

    protected override void OnStart()
    {
        // App started
    }

    protected override void OnSleep()
    {
        // App went to sleep
        // Save any pending data
    }

    protected override void OnResume()
    {
        // App resumed from sleep
        // Check for pending sync operations
    }

    private void RegisterExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ReportUnhandledException(exception, "AppDomain.UnhandledException");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportUnhandledException(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };
    }

    private void ReportUnhandledException(Exception exception, string source)
    {
        _logger?.LogError(exception, "Unhandled mobile exception from {Source}", source);
        var reportTask = Task.Run(() => _exceptionReporter.ReportAsync(exception, source));
        if (source == "AppDomain.UnhandledException")
        {
            try
            {
                reportTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException ex)
            {
                _logger?.LogWarning(ex, "Failed to flush unhandled mobile exception report");
            }
        }
    }
}
