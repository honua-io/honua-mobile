using Honua.Mobile.FieldCollection.Services.Configuration;
using Honua.Mobile.Maui.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace Honua.Mobile.FieldCollection.Services.Diagnostics;

public static class FieldCollectionExceptionReporting
{
    internal const string ModePreferenceKey = "honua_exception_reporting_mode";
    internal const string EndpointPreferenceKey = "honua_exception_reporting_endpoint";
    internal const string QueuePathPreferenceKey = "honua_exception_reporting_queue_path";
    internal const string TesterConsentPreferenceKey = "honua_exception_reporting_tester_consent";
    internal const string EnvironmentEnabledPreferenceKey = "honua_exception_reporting_environment_enabled";

    internal readonly record struct PreferenceUpdate(
        bool ShouldWriteModeAndConsent,
        bool TesterConsent,
        MobileExceptionReportingMode Mode);

    public static MobileExceptionReportingOptions FromPreferences(MobileBuildConfiguration buildConfiguration)
    {
        ArgumentNullException.ThrowIfNull(buildConfiguration);

        return CreateOptions(
            Preferences.Default.Get(ModePreferenceKey, "Disabled"),
            Preferences.Default.Get(EndpointPreferenceKey, string.Empty),
            Preferences.Default.Get(QueuePathPreferenceKey, string.Empty),
            buildConfiguration,
            SafeAppId(),
            SafePlatform(),
            SafeOsVersion(),
            SafeDeviceClass(),
            Preferences.Default.Get(TesterConsentPreferenceKey, false),
            Preferences.Default.Get(EnvironmentEnabledPreferenceKey, true));
    }

    internal static PreferenceUpdate CreatePreferenceUpdate(
        bool enableExceptionReporting,
        bool environmentAllowsReporting,
        string? endpointValue)
    {
        if (!environmentAllowsReporting)
        {
            return new PreferenceUpdate(false, false, MobileExceptionReportingMode.Disabled);
        }

        var mode = enableExceptionReporting
            ? string.IsNullOrWhiteSpace(endpointValue)
                ? MobileExceptionReportingMode.LocalOnly
                : MobileExceptionReportingMode.ServerUpload
            : MobileExceptionReportingMode.Disabled;

        return new PreferenceUpdate(true, enableExceptionReporting, mode);
    }

    internal static MobileExceptionReportingOptions CreateOptions(
        string? modeValue,
        string? endpointValue,
        string? queueDirectory,
        MobileBuildConfiguration buildConfiguration,
        string? appId,
        string? platform,
        string? osVersion,
        string? deviceClass,
        bool hasTesterConsent = true,
        bool environmentAllowsReporting = true)
    {
        ArgumentNullException.ThrowIfNull(buildConfiguration);
        var requestedMode = ParseMode(modeValue);
        var mode = hasTesterConsent && environmentAllowsReporting
            ? requestedMode
            : MobileExceptionReportingMode.Disabled;

        return new MobileExceptionReportingOptions
        {
            Mode = mode,
            UploadEndpoint = Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ? endpoint : null,
            QueueDirectory = string.IsNullOrWhiteSpace(queueDirectory) ? null : queueDirectory,
            Metadata = new MobileExceptionReportMetadata
            {
                AppId = appId,
                AppVersion = buildConfiguration.Metadata.ApplicationDisplayVersion,
                BuildNumber = buildConfiguration.Metadata.ApplicationVersion,
                CommitSha = buildConfiguration.Metadata.CommitSha,
                Branch = buildConfiguration.Metadata.Branch,
                EnvironmentName = buildConfiguration.Metadata.BuildEnvironment,
                Platform = platform,
                OsVersion = osVersion,
                DeviceClass = deviceClass,
                Properties = new Dictionary<string, string?>
                {
                    ["repository"] = buildConfiguration.Metadata.Repository,
                    ["workflowRunId"] = buildConfiguration.Metadata.WorkflowRunId,
                    ["workflowRunAttempt"] = buildConfiguration.Metadata.WorkflowRunAttempt,
                    ["configuration"] = buildConfiguration.Metadata.Configuration,
                    ["targetFramework"] = buildConfiguration.Metadata.TargetFramework,
                },
            },
        };
    }

    private static MobileExceptionReportingMode ParseMode(string? modeValue)
    {
        if (string.Equals(modeValue, "Server", StringComparison.OrdinalIgnoreCase))
        {
            return MobileExceptionReportingMode.ServerUpload;
        }

        return Enum.TryParse<MobileExceptionReportingMode>(modeValue, ignoreCase: true, out var mode)
            ? mode
            : MobileExceptionReportingMode.Disabled;
    }

    private static string SafeAppId()
    {
        try
        {
            return AppInfo.Current.PackageName;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string SafePlatform()
    {
        try
        {
            return DeviceInfo.Current.Platform.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private static string SafeOsVersion()
    {
        try
        {
            return DeviceInfo.Current.VersionString;
        }
        catch
        {
            return "unknown";
        }
    }

    private static string SafeDeviceClass()
    {
        try
        {
            return DeviceInfo.Current.Idiom.ToString();
        }
        catch
        {
            return "unknown";
        }
    }
}

internal sealed class FieldCollectionExceptionReportAuthHeader : IMobileExceptionReportUploadRequestCustomizer
{
    private readonly IAuthenticationService _authService;

    public FieldCollectionExceptionReportAuthHeader(IAuthenticationService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public void Customize(HttpRequestMessage request, MobileExceptionReport report)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(report);

        if (ShouldAttachCredentials(request.RequestUri))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _authService.ApiKey);
        }
    }

    private bool ShouldAttachCredentials(Uri? endpoint)
    {
        return endpoint is not null &&
            !string.IsNullOrWhiteSpace(_authService.ApiKey) &&
            Uri.TryCreate(_authService.ServerUrl, UriKind.Absolute, out var serverUri) &&
            Uri.Compare(
                endpoint,
                serverUri,
                UriComponents.SchemeAndServer,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0;
    }
}
