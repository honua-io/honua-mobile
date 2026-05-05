using Honua.Mobile.FieldCollection.Services.Configuration;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class MobileBuildConfigurationTests
{
    [Theory]
    [InlineData("dev", MobileBuildEnvironmentKind.Development)]
    [InlineData("staging", MobileBuildEnvironmentKind.Staging)]
    [InlineData("production", MobileBuildEnvironmentKind.Production)]
    [InlineData("ios-production", MobileBuildEnvironmentKind.Production)]
    [InlineData("ios-testflight", MobileBuildEnvironmentKind.CustomNonProduction)]
    public void EnvironmentKind_NormalizesWorkflowEnvironmentNames(
        string environment,
        MobileBuildEnvironmentKind expectedKind)
    {
        var kind = MobileBuildEnvironmentKindExtensions.FromBuildEnvironment(environment);

        Assert.Equal(expectedKind, kind);
    }

    [Fact]
    public void FromAttributes_UsesStampedGitHubBuildMetadata()
    {
        var attributes = new Dictionary<string, string?>
        {
            ["HonuaMobile.ApplicationDisplayVersion"] = "0.0.0-debug.42",
            ["HonuaMobile.BuildNumber"] = "42",
            ["HonuaMobile.BuildEnvironment"] = "staging",
            ["HonuaMobile.Repository"] = "honua-io/honua-mobile",
            ["HonuaMobile.Branch"] = "feature/mobile-builds",
            ["HonuaMobile.CommitSha"] = "0123456789abcdef0123456789abcdef01234567",
            ["HonuaMobile.WorkflowRunNumber"] = "42",
            ["HonuaMobile.WorkflowRunId"] = "100200300",
            ["HonuaMobile.WorkflowRunAttempt"] = "2",
            ["HonuaMobile.Configuration"] = "Debug",
            ["HonuaMobile.TargetFramework"] = "net10.0-android"
        };

        var configuration = MobileBuildConfiguration.FromAttributes(attributes, "1.0", "1");

        Assert.Equal("0.0.0-debug.42 (42)", configuration.Metadata.VersionDisplay);
        Assert.Equal("0123456789ab", configuration.Metadata.ShortCommitSha);
        Assert.Equal("honua-io/honua-mobile@0123456789ab on feature/mobile-builds", configuration.Metadata.SourceDisplay);
        Assert.Equal("GitHub Actions run 42, attempt 2", configuration.Metadata.WorkflowRunDisplay);
        Assert.Equal(new Uri("https://github.com/honua-io/honua-mobile/actions/runs/100200300"), configuration.Metadata.WorkflowRunUri);
        Assert.True(configuration.Metadata.IsTraceable);
        Assert.Equal(MobileBuildEnvironmentKind.Staging, configuration.ServiceEndpoint.EnvironmentKind);
    }

    [Fact]
    public void FromAttributes_DoesNotDefaultBlankEndpointToProduction()
    {
        var attributes = new Dictionary<string, string?>
        {
            ["HonuaMobile.BuildEnvironment"] = "dev",
            ["HonuaMobile.Configuration"] = "Debug"
        };

        var configuration = MobileBuildConfiguration.FromAttributes(attributes, "1.0", "1");

        Assert.True(configuration.ServiceEndpoint.IsValid);
        Assert.False(configuration.ServiceEndpoint.IsConfigured);
        Assert.Equal(MobileBuildEnvironmentKind.Development, configuration.ServiceEndpoint.EnvironmentKind);
        Assert.Equal("Development: no endpoint embedded", configuration.ServiceEndpoint.DisplayValue);
    }

    [Fact]
    public void ServiceEndpoint_RejectsProductionHostForNonProductionEnvironment()
    {
        var endpoint = MobileServiceEndpointConfiguration.Create(
            "dev",
            "https://api.honua.io",
            "Debug");

        Assert.False(endpoint.IsValid);
        Assert.Contains("Non-production", endpoint.ValidationMessage);
    }

    [Fact]
    public void ServiceEndpoint_RejectsProductionDebugBuild()
    {
        var endpoint = MobileServiceEndpointConfiguration.Create(
            "production",
            "https://api.honua.io",
            "Debug");

        Assert.False(endpoint.IsValid);
        Assert.Contains("Debug", endpoint.ValidationMessage);
        Assert.False(endpoint.IsConfigured);
    }

    [Fact]
    public void ServiceEndpoint_DisplayValueSurfacesInvalidUnparseableEndpointMetadata()
    {
        var endpoint = MobileServiceEndpointConfiguration.Create(
            "staging",
            "not a uri",
            "Release");

        Assert.False(endpoint.IsValid);
        Assert.False(endpoint.IsConfigured);
        Assert.Contains("absolute URI", endpoint.ValidationMessage);
        Assert.Equal("Staging: invalid endpoint metadata", endpoint.DisplayValue);
    }

    [Fact]
    public void ServiceEndpoint_AllowsLoopbackHttpOnlyForDevelopment()
    {
        var development = MobileServiceEndpointConfiguration.Create(
            "development",
            "http://localhost:5000",
            "Debug");
        var staging = MobileServiceEndpointConfiguration.Create(
            "staging",
            "http://staging.honua.test",
            "Debug");

        Assert.True(development.IsValid);
        Assert.Equal(new Uri("http://localhost:5000"), development.ApiBaseUrl);
        Assert.False(staging.IsValid);
        Assert.Contains("HTTPS", staging.ValidationMessage);
    }
}
