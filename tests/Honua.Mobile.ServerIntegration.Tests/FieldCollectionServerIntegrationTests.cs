using System.ComponentModel;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.Maui.Diagnostics;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class FieldCollectionServerIntegrationTests : IDisposable
{
    private readonly string _rootDirectory;

    public FieldCollectionServerIntegrationTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"honua-fieldcollection-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task AuthenticationService_ValidatesApiKeyAgainstAuthenticatedHonuaEndpoint()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        using var http = new HttpClient();
        var auth = new AuthenticationService(http);

        var withoutApiKey = await auth.ValidateConnectionAsync(server.BaseUri.ToString());
        var withApiKey = await auth.ValidateConnectionAsync(server.BaseUri.ToString(), "integration-api-key");

        Assert.True(withoutApiKey);
        Assert.True(withApiKey);
        Assert.True(server.Received("GET", "/health"));
        Assert.True(server.Received("GET", "/api/scenes"));

        var scenesRequest = server.SingleRequest("GET", "/api/scenes");
        Assert.Equal("integration-api-key", scenesRequest.Header("X-API-Key"));
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
        var auth = new FixedAuthenticationService(server.BaseUri.ToString(), "integration-api-key");
        var uploader = new HttpMobileExceptionReportUploader(
            http,
            options,
            [new FieldCollectionExceptionReportAuthHeader(auth)]);
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

    private sealed class FixedAuthenticationService : IAuthenticationService
    {
        public FixedAuthenticationService(string serverUrl, string apiKey)
        {
            ServerUrl = serverUrl.TrimEnd('/');
            ApiKey = apiKey;
        }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public bool IsAuthenticated => true;

        public string? CurrentUserId => "integration-user";

        public string? CurrentUserName => "Integration User";

        public string? ApiKey { get; }

        public string? ServerUrl { get; }

        public Task<AuthenticationResult> AuthenticateAsync(string serverUrl, string apiKey)
            => Task.FromResult(AuthenticationResult.Success("integration-user", "Integration User", apiKey));

        public Task<AuthenticationResult> AuthenticateWithCredentialsAsync(string serverUrl, string username, string password)
            => Task.FromResult(AuthenticationResult.Failure("Username/password authentication is not configured."));

        public Task<bool> RefreshTokenAsync() => Task.FromResult(true);

        public Task LogoutAsync() => Task.CompletedTask;

        public Task<bool> ValidateConnectionAsync(string serverUrl, string? apiKey = null)
            => Task.FromResult(true);
    }
}
