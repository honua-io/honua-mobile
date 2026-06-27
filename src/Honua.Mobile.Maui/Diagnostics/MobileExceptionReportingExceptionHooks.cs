// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using Microsoft.Extensions.Logging;

namespace Honua.Mobile.Maui.Diagnostics;

/// <summary>
/// Optional process-level hooks for unhandled AppDomain and unobserved task exceptions.
/// </summary>
public sealed class MobileExceptionReportingExceptionHooks : IDisposable
{
    private static readonly TimeSpan TerminatingUnhandledExceptionFlushTimeout = TimeSpan.FromSeconds(2);

    private readonly IMobileExceptionReporter _reporter;
    private readonly ILogger<MobileExceptionReportingExceptionHooks>? _logger;
    private int _registered;

    public MobileExceptionReportingExceptionHooks(
        IMobileExceptionReporter reporter,
        ILogger<MobileExceptionReportingExceptionHooks>? logger = null)
    {
        _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        _logger = logger;
    }

    public void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _registered, 0) == 0)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            var reportTask = ReportInBackground(exception, "AppDomain.UnhandledException", args.IsTerminating);
            if (args.IsTerminating)
            {
                WaitForTerminatingUnhandledExceptionReport(reportTask);
            }
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        ReportInBackground(args.Exception, "TaskScheduler.UnobservedTaskException", isTerminating: false);
        args.SetObserved();
    }

    private Task ReportInBackground(Exception exception, string source, bool isTerminating)
    {
        return Task.Run(async () =>
        {
            try
            {
                await _reporter.ReportAsync(
                    exception,
                    new MobileExceptionReportContext
                    {
                        Source = source,
                        Severity = isTerminating ? MobileExceptionSeverity.Critical : MobileExceptionSeverity.Error,
                        Properties = new Dictionary<string, object?>
                        {
                            ["isTerminating"] = isTerminating,
                        },
                    });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Mobile exception reporting hook failed");
            }
        });
    }

    private void WaitForTerminatingUnhandledExceptionReport(Task reportTask)
    {
        try
        {
            if (!reportTask.Wait(TerminatingUnhandledExceptionFlushTimeout))
            {
                _logger?.LogWarning(
                    "Timed out after {Timeout} waiting to queue terminating mobile exception report",
                    TerminatingUnhandledExceptionFlushTimeout);
            }
        }
        catch (AggregateException ex)
        {
            _logger?.LogWarning(ex, "Failed to flush terminating mobile exception report");
        }
    }
}
