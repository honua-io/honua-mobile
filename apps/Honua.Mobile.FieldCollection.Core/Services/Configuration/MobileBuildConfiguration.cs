using System.Reflection;

namespace Honua.Mobile.FieldCollection.Services.Configuration;

public sealed class MobileBuildConfiguration
{
    public static readonly MobileBuildConfiguration Empty = FromAttributes(
        new Dictionary<string, string?>(),
        "unknown",
        "unknown");

    public MobileBuildConfiguration(
        MobileBuildMetadata metadata,
        MobileServiceEndpointConfiguration serviceEndpoint)
    {
        Metadata = metadata;
        ServiceEndpoint = serviceEndpoint;
    }

    public MobileBuildMetadata Metadata { get; }

    public MobileServiceEndpointConfiguration ServiceEndpoint { get; }

    public static MobileBuildConfiguration FromAssembly(
        Assembly assembly,
        string applicationDisplayVersion,
        string applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var attributes = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(attribute => attribute.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase);

        return FromAttributes(attributes, applicationDisplayVersion, applicationVersion);
    }

    public static MobileBuildConfiguration FromAttributes(
        IReadOnlyDictionary<string, string?> attributes,
        string applicationDisplayVersion,
        string applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var displayVersion = FirstNonBlank(
            Get(attributes, "HonuaMobile.ApplicationDisplayVersion"),
            applicationDisplayVersion,
            "unknown");
        var buildNumber = FirstNonBlank(
            Get(attributes, "HonuaMobile.BuildNumber"),
            applicationVersion,
            "unknown");
        var buildEnvironment = FirstNonBlank(
            Get(attributes, "HonuaMobile.BuildEnvironment"),
            "unspecified");
        var configuration = FirstNonBlank(
            Get(attributes, "HonuaMobile.Configuration"),
            "unknown");

        var metadata = new MobileBuildMetadata(
            applicationDisplayVersion: displayVersion,
            applicationVersion: buildNumber,
            buildEnvironment: buildEnvironment,
            repository: FirstNonBlank(Get(attributes, "HonuaMobile.Repository")),
            branch: FirstNonBlank(Get(attributes, "HonuaMobile.Branch")),
            commitSha: FirstNonBlank(Get(attributes, "HonuaMobile.CommitSha")),
            workflowRunNumber: FirstNonBlank(Get(attributes, "HonuaMobile.WorkflowRunNumber")),
            workflowRunId: FirstNonBlank(Get(attributes, "HonuaMobile.WorkflowRunId")),
            workflowRunAttempt: FirstNonBlank(Get(attributes, "HonuaMobile.WorkflowRunAttempt")),
            configuration: configuration,
            targetFramework: FirstNonBlank(Get(attributes, "HonuaMobile.TargetFramework")));

        var serviceEndpoint = MobileServiceEndpointConfiguration.Create(
            buildEnvironment,
            Get(attributes, "HonuaMobile.ApiBaseUrl"),
            configuration);

        return new MobileBuildConfiguration(metadata, serviceEndpoint);
    }

    private static string? Get(IReadOnlyDictionary<string, string?> attributes, string key)
        => attributes.TryGetValue(key, out var value) ? value : null;

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

public sealed class MobileBuildMetadata
{
    public MobileBuildMetadata(
        string applicationDisplayVersion,
        string applicationVersion,
        string buildEnvironment,
        string repository,
        string branch,
        string commitSha,
        string workflowRunNumber,
        string workflowRunId,
        string workflowRunAttempt,
        string configuration,
        string targetFramework)
    {
        ApplicationDisplayVersion = applicationDisplayVersion;
        ApplicationVersion = applicationVersion;
        BuildEnvironment = buildEnvironment;
        Repository = repository;
        Branch = branch;
        CommitSha = commitSha;
        WorkflowRunNumber = workflowRunNumber;
        WorkflowRunId = workflowRunId;
        WorkflowRunAttempt = workflowRunAttempt;
        Configuration = configuration;
        TargetFramework = targetFramework;
    }

    public string ApplicationDisplayVersion { get; }

    public string ApplicationVersion { get; }

    public string BuildEnvironment { get; }

    public string Repository { get; }

    public string Branch { get; }

    public string CommitSha { get; }

    public string WorkflowRunNumber { get; }

    public string WorkflowRunId { get; }

    public string WorkflowRunAttempt { get; }

    public string Configuration { get; }

    public string TargetFramework { get; }

    public string ShortCommitSha
        => string.IsNullOrWhiteSpace(CommitSha)
            ? "unknown"
            : CommitSha[..Math.Min(12, CommitSha.Length)];

    public bool IsGitHubActionsBuild
        => !string.IsNullOrWhiteSpace(WorkflowRunId) || !string.IsNullOrWhiteSpace(WorkflowRunNumber);

    public bool IsTraceable
        => !string.IsNullOrWhiteSpace(Repository)
            && !string.IsNullOrWhiteSpace(Branch)
            && !string.IsNullOrWhiteSpace(CommitSha)
            && !string.IsNullOrWhiteSpace(WorkflowRunId)
            && !string.IsNullOrWhiteSpace(BuildEnvironment);

    public string VersionDisplay => $"{ApplicationDisplayVersion} ({ApplicationVersion})";

    public string SourceDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Repository) && string.IsNullOrWhiteSpace(CommitSha))
            {
                return "Source not stamped";
            }

            var repository = string.IsNullOrWhiteSpace(Repository) ? "unknown repository" : Repository;
            var branch = string.IsNullOrWhiteSpace(Branch) ? "unknown branch" : Branch;
            return $"{repository}@{ShortCommitSha} on {branch}";
        }
    }

    public string WorkflowRunDisplay
    {
        get
        {
            if (!IsGitHubActionsBuild)
            {
                return "Local build";
            }

            var run = string.IsNullOrWhiteSpace(WorkflowRunNumber) ? WorkflowRunId : WorkflowRunNumber;
            return string.IsNullOrWhiteSpace(WorkflowRunAttempt)
                ? $"GitHub Actions run {run}"
                : $"GitHub Actions run {run}, attempt {WorkflowRunAttempt}";
        }
    }

    public Uri? WorkflowRunUri
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Repository) || string.IsNullOrWhiteSpace(WorkflowRunId))
            {
                return null;
            }

            return Uri.TryCreate(
                $"https://github.com/{Repository}/actions/runs/{WorkflowRunId}",
                UriKind.Absolute,
                out var uri)
                ? uri
                : null;
        }
    }
}

public sealed class MobileServiceEndpointConfiguration
{
    private MobileServiceEndpointConfiguration(
        string environment,
        MobileBuildEnvironmentKind environmentKind,
        Uri? apiBaseUrl,
        bool isValid,
        string validationMessage)
    {
        Environment = environment;
        EnvironmentKind = environmentKind;
        ApiBaseUrl = apiBaseUrl;
        IsValid = isValid;
        ValidationMessage = validationMessage;
    }

    public string Environment { get; }

    public MobileBuildEnvironmentKind EnvironmentKind { get; }

    public string EnvironmentDisplayName => EnvironmentKind switch
    {
        MobileBuildEnvironmentKind.Development => "Development",
        MobileBuildEnvironmentKind.Staging => "Staging",
        MobileBuildEnvironmentKind.Demo => "Demo",
        MobileBuildEnvironmentKind.Production => "Production",
        MobileBuildEnvironmentKind.CustomNonProduction => string.IsNullOrWhiteSpace(Environment)
            ? "Custom non-production"
            : Environment,
        _ => "Unspecified"
    };

    public Uri? ApiBaseUrl { get; }

    public bool IsConfigured => ApiBaseUrl != null;

    public bool IsProduction => EnvironmentKind == MobileBuildEnvironmentKind.Production;

    public bool IsValid { get; }

    public string ValidationMessage { get; }

    public string DisplayValue
    {
        get
        {
            if (!IsValid)
            {
                return $"{EnvironmentDisplayName}: invalid endpoint metadata";
            }

            return IsConfigured
                ? $"{EnvironmentDisplayName}: {ApiBaseUrl}"
                : $"{EnvironmentDisplayName}: no endpoint embedded";
        }
    }

    public static MobileServiceEndpointConfiguration Create(
        string? environment,
        string? apiBaseUrl,
        string? buildConfiguration)
    {
        var normalizedEnvironment = string.IsNullOrWhiteSpace(environment)
            ? "unspecified"
            : environment.Trim();
        var kind = MobileBuildEnvironmentKindExtensions.FromBuildEnvironment(normalizedEnvironment);
        var isDebugBuild = string.Equals(buildConfiguration, "Debug", StringComparison.OrdinalIgnoreCase);

        if (isDebugBuild && kind == MobileBuildEnvironmentKind.Production)
        {
            return new MobileServiceEndpointConfiguration(
                normalizedEnvironment,
                kind,
                null,
                false,
                "Debug mobile builds cannot target production.");
        }

        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            var message = kind == MobileBuildEnvironmentKind.Unspecified
                ? "No build environment or service endpoint is embedded."
                : "No service endpoint is embedded for this build.";

            return new MobileServiceEndpointConfiguration(normalizedEnvironment, kind, null, true, message);
        }

        if (!Uri.TryCreate(apiBaseUrl.Trim(), UriKind.Absolute, out var endpoint))
        {
            return new MobileServiceEndpointConfiguration(
                normalizedEnvironment,
                kind,
                null,
                false,
                "HonuaMobileApiBaseUrl must be an absolute URI.");
        }

        var loopbackDevelopment = kind == MobileBuildEnvironmentKind.Development
            && endpoint.Scheme == Uri.UriSchemeHttp
            && endpoint.IsLoopback;
        if (endpoint.Scheme != Uri.UriSchemeHttps && !loopbackDevelopment)
        {
            return new MobileServiceEndpointConfiguration(
                normalizedEnvironment,
                kind,
                endpoint,
                false,
                "Mobile service endpoints must use HTTPS unless they are development loopback URLs.");
        }

        if (kind != MobileBuildEnvironmentKind.Production && LooksLikeProductionHost(endpoint.Host))
        {
            return new MobileServiceEndpointConfiguration(
                normalizedEnvironment,
                kind,
                endpoint,
                false,
                "Non-production mobile builds cannot embed a production Honua endpoint.");
        }

        return new MobileServiceEndpointConfiguration(normalizedEnvironment, kind, endpoint, true, "Endpoint metadata is valid.");
    }

    private static bool LooksLikeProductionHost(string host)
    {
        var normalizedHost = host.TrimEnd('.').ToLowerInvariant();
        return normalizedHost is "api.honua.io" or "honua.io" or "www.honua.io"
            || normalizedHost.StartsWith("prod.", StringComparison.Ordinal)
            || normalizedHost.StartsWith("production.", StringComparison.Ordinal);
    }
}

public enum MobileBuildEnvironmentKind
{
    Unspecified,
    Development,
    Staging,
    Demo,
    Production,
    CustomNonProduction
}

public static class MobileBuildEnvironmentKindExtensions
{
    public static MobileBuildEnvironmentKind FromBuildEnvironment(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment))
        {
            return MobileBuildEnvironmentKind.Unspecified;
        }

        var normalized = environment.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized switch
        {
            "dev" or "development" or "local" or "dev-local" => MobileBuildEnvironmentKind.Development,
            "stage" or "staging" => MobileBuildEnvironmentKind.Staging,
            "demo" or "ios-demo" or "android-demo" => MobileBuildEnvironmentKind.Demo,
            "prod" or "production" or "ios-production" => MobileBuildEnvironmentKind.Production,
            "custom-nonprod" or "custom-non-production" or "nonprod" or "non-production" or "ios-testflight" => MobileBuildEnvironmentKind.CustomNonProduction,
            "unspecified" => MobileBuildEnvironmentKind.Unspecified,
            _ when normalized.Contains("production", StringComparison.Ordinal)
                || normalized.StartsWith("prod-", StringComparison.Ordinal) => MobileBuildEnvironmentKind.Production,
            _ => MobileBuildEnvironmentKind.CustomNonProduction
        };
    }
}
