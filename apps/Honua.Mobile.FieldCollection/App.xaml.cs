using Honua.Mobile.Maui.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.FieldCollection;

public partial class App : Application
{
    private readonly MobileExceptionReportingExceptionHooks _exceptionHooks;
    private readonly IReadOnlyList<IMobileExceptionReportUploadWorker> _uploadWorkers;
    private readonly MobileExceptionReportingOptions _exceptionOptions;
    private readonly ILogger<App>? _logger;

    public App(
        MobileExceptionReportingExceptionHooks exceptionHooks,
        IEnumerable<IMobileExceptionReportUploadWorker> uploadWorkers,
        MobileExceptionReportingOptions exceptionOptions,
        ILogger<App>? logger = null)
    {
        _exceptionHooks = exceptionHooks ?? throw new ArgumentNullException(nameof(exceptionHooks));
        _uploadWorkers = uploadWorkers?.ToArray() ?? [];
        _exceptionOptions = exceptionOptions ?? throw new ArgumentNullException(nameof(exceptionOptions));
        _logger = logger;

        InitializeComponent();
        RegisterExceptionHandlers();
        StartExceptionReportUploadFlush();
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
        StartExceptionReportUploadFlush();
    }

    private void RegisterExceptionHandlers()
    {
        if (_exceptionOptions.Mode != MobileExceptionReportingMode.Disabled)
        {
            _exceptionHooks.Register();
        }
    }

    private void StartExceptionReportUploadFlush()
    {
        if (_exceptionOptions.Mode != MobileExceptionReportingMode.ServerUpload || _uploadWorkers.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            foreach (var worker in _uploadWorkers)
            {
                try
                {
                    await worker.FlushPendingAsync();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to flush pending mobile exception reports");
                }
            }
        });
    }
}
