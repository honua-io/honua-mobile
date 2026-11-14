using Honua.Mobile.Maui.Diagnostics;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class MobileDiagnosticsServerIntegrationTests : IDisposable
{
    private readonly string _rootDirectory;

    public MobileDiagnosticsServerIntegrationTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"honua-fieldcollection-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task MobileExceptionReportingPipeline_PostsSanitizedExceptionReportToHonuaServer()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        using var http = new HttpClient();
        var options = new MobileExceptionReportingOptions
        {
            Mode = MobileExceptionReportingMode.ServerUpload,
            UploadEndpoint = server.Uri("/api/mobile/exceptions"),
            QueueDirectory = Path.Combine(_rootDirectory, "exception-queue"),
            UploadInitialBackoff = TimeSpan.Zero,
            UploadMaxBackoff = TimeSpan.Zero,
            Metadata = new MobileExceptionReportMetadata
            {
                AppId = "honua.fieldcollection",
                AppVersion = "1.2.3",
                BuildNumber = "456",
                CommitSha = "abc123",
                Branch = "beta",
                EnvironmentName = "IntegrationTest",
                Platform = "Android",
                OsVersion = "15",
                DeviceClass = "Tablet",
                Properties = new Dictionary<string, string?>
                {
                    ["tenant"] = "integration",
                },
            },
        };
        var queue = new FileMobileExceptionReportQueue(options);
        var reporter = new LocalMobileExceptionReporter(queue, options);
        http.DefaultRequestHeaders.Add("X-API-Key", "integration-api-key");
        var uploader = new HttpMobileExceptionReportUploader(
            http,
            options);
        var worker = new MobileExceptionReportUploadWorker(queue, uploader, options);

        await reporter.ReportAsync(
            new InvalidOperationException("integration failure token=must-not-leak"),
            new MobileExceptionReportContext
            {
                Source = "integration-test",
                Operation = "sync-push",
                CorrelationId = "correlation-123",
                RequestId = "request-456",
                Properties = new Dictionary<string, object?>
                {
                    ["layer"] = "assets",
                    ["api_key"] = "must-not-leak",
                },
            });
        await worker.FlushPendingAsync();

        Assert.True(server.Received("POST", "/api/mobile/exceptions"));
        var request = server.SingleRequest("POST", "/api/mobile/exceptions");
        Assert.Equal("integration-api-key", request.Header("X-API-Key"));
        Assert.Contains("integration failure", request.Body);
        Assert.Contains("integration-test", request.Body);
        Assert.Contains("assets", request.Body);
        Assert.DoesNotContain("must-not-leak", request.Body);

        var logEntry = Assert.Single(server.MobileExceptionLogEntries);
        Assert.True(logEntry.Authenticated);
        Assert.Equal("integration-test", logEntry.Source);
        Assert.Equal("sync-push", logEntry.Operation);
        Assert.Equal("correlation-123", logEntry.CorrelationId);
        Assert.Equal("request-456", logEntry.RequestId);
        Assert.Equal(MobileExceptionSeverity.Error, logEntry.Severity);
        Assert.Equal(typeof(InvalidOperationException).FullName, logEntry.ExceptionType);
        Assert.Equal("honua.fieldcollection", logEntry.AppId);
        Assert.Equal("1.2.3", logEntry.AppVersion);
        Assert.Equal("456", logEntry.BuildNumber);
        Assert.Equal("abc123", logEntry.CommitSha);
        Assert.Equal("beta", logEntry.Branch);
        Assert.Equal("IntegrationTest", logEntry.EnvironmentName);
        Assert.Equal("Android", logEntry.Platform);
        Assert.Equal("15", logEntry.OsVersion);
        Assert.Equal("Tablet", logEntry.DeviceClass);
        Assert.Equal("integration", logEntry.MetadataProperties["tenant"]);
        Assert.Equal("assets", logEntry.Context["layer"]);
        Assert.Equal(MobileExceptionRedactor.RedactedValue, logEntry.Context["api_key"]);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_rootDirectory, "exception-queue")));
    }

    [Fact]
    public async Task MobileExceptionReportingPipeline_RetainsQueuedReportWhenServerRejectsUnauthenticatedUpload()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        using var http = new HttpClient();
        var options = new MobileExceptionReportingOptions
        {
            Mode = MobileExceptionReportingMode.ServerUpload,
            UploadEndpoint = server.Uri("/api/mobile/exceptions"),
            QueueDirectory = Path.Combine(_rootDirectory, "unauthenticated-exception-queue"),
            UploadInitialBackoff = TimeSpan.Zero,
            UploadMaxBackoff = TimeSpan.Zero,
        };
        var queue = new FileMobileExceptionReportQueue(options);
        var reporter = new LocalMobileExceptionReporter(queue, options);
        var uploader = new HttpMobileExceptionReportUploader(http, options);
        var worker = new MobileExceptionReportUploadWorker(queue, uploader, options);

        await reporter.ReportAsync(
            new InvalidOperationException("missing auth"),
            new MobileExceptionReportContext { Source = "integration-test" });
        await worker.FlushPendingAsync();

        var request = server.SingleRequest("POST", "/api/mobile/exceptions");
        Assert.Null(request.Header("X-API-Key"));
        Assert.Empty(server.MobileExceptionLogEntries);
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(_rootDirectory, "unauthenticated-exception-queue")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }
}
