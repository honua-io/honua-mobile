using Honua.Mobile.Maui;
using Honua.Mobile.Maui.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui.Tests.Diagnostics;

public sealed class LocalMobileExceptionReporterTests
{
    [Fact]
    public async Task DisabledRegistration_UsesNoOpReporterAndDoesNotCreateQueue()
    {
        var queueDirectory = CreateQueueDirectoryPath();
        try
        {
            using var provider = new ServiceCollection()
                .AddHonuaMobileExceptionReporting(new MobileExceptionReportingOptions
                {
                    Mode = MobileExceptionReportingMode.Disabled,
                    QueueDirectory = queueDirectory,
                })
                .BuildServiceProvider();

            var reporter = provider.GetRequiredService<IMobileExceptionReporter>();

            await reporter.ReportAsync(new InvalidOperationException("boom"));

            Assert.IsType<NoOpMobileExceptionReporter>(reporter);
            Assert.False(Directory.Exists(queueDirectory));
        }
        finally
        {
            DeleteDirectory(queueDirectory);
        }
    }

    [Fact]
    public void LocalRegistration_WiresReporterQueueAndExceptionHooks()
    {
        var queueDirectory = CreateQueueDirectoryPath();
        try
        {
            using var provider = new ServiceCollection()
                .AddLogging()
                .AddHonuaMobileExceptionReporting(new MobileExceptionReportingOptions
                {
                    Mode = MobileExceptionReportingMode.LocalOnly,
                    QueueDirectory = queueDirectory,
                })
                .BuildServiceProvider();

            Assert.IsType<LocalMobileExceptionReporter>(provider.GetRequiredService<IMobileExceptionReporter>());
            Assert.IsType<FileMobileExceptionReportQueue>(provider.GetRequiredService<IMobileExceptionReportQueue>());
            Assert.NotNull(provider.GetRequiredService<MobileExceptionReportingExceptionHooks>());
        }
        finally
        {
            DeleteDirectory(queueDirectory);
        }
    }

    [Fact]
    public async Task ReportAsync_QueuesSanitizedReport()
    {
        var queueDirectory = CreateQueueDirectoryPath();
        try
        {
            var options = new MobileExceptionReportingOptions
            {
                Mode = MobileExceptionReportingMode.LocalOnly,
                QueueDirectory = queueDirectory,
                Metadata = new MobileExceptionReportMetadata
                {
                    AppId = "honua.field",
                    AppVersion = "1.2.3",
                    BuildNumber = "456",
                    CommitSha = "abc123",
                    Branch = "codex/mobile-exception-reporting",
                    EnvironmentName = "beta",
                    Platform = "android",
                    OsVersion = "15",
                    DeviceClass = "phone",
                },
            };
            var queue = new FileMobileExceptionReportQueue(options);
            var reporter = new LocalMobileExceptionReporter(queue, options);

            await reporter.ReportAsync(
                new InvalidOperationException("failed token=raw-token at https://user:pass@example.test/sync?api_key=secret"),
                new MobileExceptionReportContext
                {
                    Source = "offline-sync",
                    Operation = "delta-upload",
                    CorrelationId = "corr-123",
                    Properties = new Dictionary<string, object?>
                    {
                        ["apiKey"] = "test-api-key",
                        ["latitude"] = 21.3069,
                        ["formPayload"] = "{\"owner\":\"Kai\"}",
                        ["layer"] = "hydrants",
                    },
                });

            var report = Assert.Single(await ReadReportsAsync(queue));

            Assert.Equal("offline-sync", report.Source);
            Assert.Equal("delta-upload", report.Operation);
            Assert.Equal("corr-123", report.CorrelationId);
            Assert.Equal("honua.field", report.Metadata.AppId);
            Assert.DoesNotContain("raw-token", report.Message);
            Assert.DoesNotContain("user:pass", report.Message);
            Assert.DoesNotContain("secret", report.Message);
            Assert.Equal(MobileExceptionRedactor.RedactedValue, report.Context["apiKey"]);
            Assert.Equal(MobileExceptionRedactor.PreciseLocationRedactedValue, report.Context["latitude"]);
            Assert.Equal(MobileExceptionRedactor.FormPayloadRedactedValue, report.Context["formPayload"]);
            Assert.Equal("hydrants", report.Context["layer"]);
        }
        finally
        {
            DeleteDirectory(queueDirectory);
        }
    }

    [Fact]
    public async Task ReportAsync_DeduplicatesSameExceptionWithinWindow()
    {
        var queueDirectory = CreateQueueDirectoryPath();
        try
        {
            var options = new MobileExceptionReportingOptions
            {
                Mode = MobileExceptionReportingMode.LocalOnly,
                QueueDirectory = queueDirectory,
                DuplicateWindow = TimeSpan.FromMinutes(10),
            };
            var queue = new FileMobileExceptionReportQueue(options);
            var reporter = new LocalMobileExceptionReporter(queue, options);
            var exception = new InvalidOperationException("same failure");
            var context = new MobileExceptionReportContext { Source = "auth-refresh" };

            await reporter.ReportAsync(exception, context);
            await reporter.ReportAsync(exception, context);

            Assert.Single(await ReadReportsAsync(queue));
        }
        finally
        {
            DeleteDirectory(queueDirectory);
        }
    }

    private static async Task<List<MobileExceptionReport>> ReadReportsAsync(IMobileExceptionReportQueue queue)
    {
        var reports = new List<MobileExceptionReport>();
        await foreach (var queued in queue.ReadPendingAsync())
        {
            reports.Add(queued.Report);
        }

        return reports;
    }

    private static string CreateQueueDirectoryPath()
    {
        return Path.Combine(Path.GetTempPath(), $"honua-mobile-exception-tests-{Guid.NewGuid():N}");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
