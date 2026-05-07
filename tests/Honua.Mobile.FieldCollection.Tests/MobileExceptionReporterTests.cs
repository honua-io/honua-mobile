using System.Globalization;
using Honua.Mobile.FieldCollection.Services.Configuration;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.Maui.Diagnostics;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class MobileExceptionReporterTests
{
    [Fact]
    public void CreateOptions_MapsLegacyServerPreferenceToSharedServerUploadMode()
    {
        var build = CreateBuildConfiguration();
        var queueDirectory = Path.Combine(Path.GetTempPath(), $"honua-exception-options-{Guid.NewGuid():N}");

        var options = FieldCollectionExceptionReporting.CreateOptions(
            "Server",
            "https://api.honua.test/mobile/exception-reports",
            queueDirectory,
            build,
            "io.honua.mobile.fieldcollection",
            "Android",
            "15",
            "Phone");

        Assert.Equal(MobileExceptionReportingMode.ServerUpload, options.Mode);
        Assert.Equal(new Uri("https://api.honua.test/mobile/exception-reports"), options.UploadEndpoint);
        Assert.Equal(queueDirectory, options.QueueDirectory);
        Assert.Equal("io.honua.mobile.fieldcollection", options.Metadata.AppId);
        Assert.Equal("1.2.3", options.Metadata.AppVersion);
        Assert.Equal("456", options.Metadata.BuildNumber);
        Assert.Equal("abc123", options.Metadata.CommitSha);
        Assert.Equal("main", options.Metadata.Branch);
        Assert.Equal("beta", options.Metadata.EnvironmentName);
        Assert.Equal("Android", options.Metadata.Platform);
        Assert.Equal("15", options.Metadata.OsVersion);
        Assert.Equal("Phone", options.Metadata.DeviceClass);
        Assert.Equal("honua-io/honua-mobile", options.Metadata.Properties["repository"]);
    }

    [Fact]
    public void CreateOptions_LeavesUploadEndpointUnsetWhenPreferenceIsBlank()
    {
        var options = FieldCollectionExceptionReporting.CreateOptions(
            "ServerUpload",
            string.Empty,
            string.Empty,
            CreateBuildConfiguration(),
            "io.honua.mobile.fieldcollection",
            "Android",
            "15",
            "Phone");

        Assert.Equal(MobileExceptionReportingMode.ServerUpload, options.Mode);
        Assert.Null(options.UploadEndpoint);
        Assert.Null(options.QueueDirectory);
    }

    [Fact]
    public void AuthHeaderCustomizer_DoesNotForwardApiKeyToCrossOriginEndpoint()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://collector.example.test/mobile/exceptions");
        var customizer = CreateAuthHeaderCustomizer();

        customizer.Customize(request, CreateReport());

        Assert.False(request.Headers.Contains("X-API-Key"));
    }

    [Fact]
    public void AuthHeaderCustomizer_ForwardsApiKeyToSameOriginEndpoint()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.honua.test/mobile/exceptions");
        var customizer = CreateAuthHeaderCustomizer();

        customizer.Customize(request, CreateReport());

        Assert.True(request.Headers.TryGetValues("X-API-Key", out var values));
        Assert.Equal("test-api-key", Assert.Single(values));
    }

    private static FieldCollectionExceptionReportAuthHeader CreateAuthHeaderCustomizer()
    {
        return new FieldCollectionExceptionReportAuthHeader(
            new TestAuthenticationService
            {
                ApiKey = "test-api-key",
                ServerUrl = "https://api.honua.test",
            });
    }

    private static MobileExceptionReport CreateReport()
    {
        return new MobileExceptionReport
        {
            Id = Guid.NewGuid().ToString("N"),
            Fingerprint = "fingerprint",
            OccurredAtUtc = DateTimeOffset.Parse("2026-05-05T00:00:00Z", CultureInfo.InvariantCulture),
            Source = "unit-test",
            ExceptionType = typeof(InvalidOperationException).FullName!,
            Message = "boom",
        };
    }

    private static MobileBuildConfiguration CreateBuildConfiguration()
    {
        return MobileBuildConfiguration.FromAttributes(
            new Dictionary<string, string?>
            {
                ["HonuaMobile.BuildEnvironment"] = "beta",
                ["HonuaMobile.Repository"] = "honua-io/honua-mobile",
                ["HonuaMobile.Branch"] = "main",
                ["HonuaMobile.CommitSha"] = "abc123",
                ["HonuaMobile.WorkflowRunId"] = "789",
                ["HonuaMobile.WorkflowRunAttempt"] = "2",
                ["HonuaMobile.Configuration"] = "Release",
                ["HonuaMobile.TargetFramework"] = "net10.0-android",
            },
            "1.2.3",
            "456");
    }
}
