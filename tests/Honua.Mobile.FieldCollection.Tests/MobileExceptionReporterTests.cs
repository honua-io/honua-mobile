using System.Net;
using Honua.Mobile.FieldCollection.Services.Diagnostics;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class MobileExceptionReporterTests
{
    [Fact]
    public async Task ReportAsync_DoesNotForwardApiKeyToCrossOriginEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var pendingDirectory = CreatePendingDirectory();
        try
        {
            var reporter = CreateReporter(
                http,
                endpoint: new Uri("https://collector.example.test/mobile/exceptions"),
                pendingDirectory);

            await reporter.ReportAsync(new InvalidOperationException("boom"), "unit-test");

            Assert.NotNull(capturedRequest);
            Assert.False(capturedRequest.Headers.Contains("X-API-Key"));
            Assert.Empty(Directory.EnumerateFiles(pendingDirectory));
        }
        finally
        {
            DeleteDirectory(pendingDirectory);
        }
    }

    [Fact]
    public async Task ReportAsync_ForwardsApiKeyToSameOriginEndpoint()
    {
        HttpRequestMessage? capturedRequest = null;
        using var http = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var pendingDirectory = CreatePendingDirectory();
        try
        {
            var reporter = CreateReporter(
                http,
                endpoint: new Uri("https://api.honua.test/api/mobile/exceptions"),
                pendingDirectory);

            await reporter.ReportAsync(new InvalidOperationException("boom"), "unit-test");

            Assert.NotNull(capturedRequest);
            Assert.True(capturedRequest.Headers.TryGetValues("X-API-Key", out var values));
            Assert.Equal("test-api-key", Assert.Single(values));
        }
        finally
        {
            DeleteDirectory(pendingDirectory);
        }
    }

    [Fact]
    public async Task FlushPendingAsync_RetainsReportWhenSendFailsAndDeletesAfterSuccess()
    {
        var responses = new Queue<HttpStatusCode>([
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK
        ]);
        using var http = new HttpClient(new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(responses.Dequeue())));
        var pendingDirectory = CreatePendingDirectory();
        try
        {
            var reporter = CreateReporter(
                http,
                endpoint: new Uri("https://api.honua.test/api/mobile/exceptions"),
                pendingDirectory);

            await reporter.ReportAsync(new InvalidOperationException("boom"), "unit-test");
            Assert.Single(Directory.EnumerateFiles(pendingDirectory));

            await reporter.FlushPendingAsync();
            Assert.Empty(Directory.EnumerateFiles(pendingDirectory));
        }
        finally
        {
            DeleteDirectory(pendingDirectory);
        }
    }

    private static MobileExceptionReporter CreateReporter(
        HttpClient httpClient,
        Uri endpoint,
        string pendingDirectory)
    {
        return new MobileExceptionReporter(
            httpClient,
            new TestAuthenticationService
            {
                ApiKey = "test-api-key",
                ServerUrl = "https://api.honua.test"
            },
            new MobileExceptionReportingOptions
            {
                Mode = MobileExceptionReportingMode.Server,
                Endpoint = endpoint,
                PendingReportsDirectory = pendingDirectory
            });
    }

    private static string CreatePendingDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"honua-exception-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
