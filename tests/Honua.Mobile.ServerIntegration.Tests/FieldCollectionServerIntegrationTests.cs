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
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_rootDirectory, "exception-queue")));
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
