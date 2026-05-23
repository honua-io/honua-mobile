using System.Globalization;

namespace Honua.Mobile.FieldCollection.Services.Diagnostics;

public sealed class FieldDeviceDiagnosticsInput
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public string AppVersion { get; set; } = string.Empty;
    public string BuildNumber { get; set; } = string.Empty;
    public string BuildEnvironment { get; set; } = string.Empty;
    public string SourceDisplay { get; set; } = string.Empty;
    public string WorkflowRunDisplay { get; set; } = string.Empty;
    public string ServiceEndpointState { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public bool ServerReachable { get; set; }
    public bool IsSyncing { get; set; }
    public bool IsRemoteSyncConfigured { get; set; }
    public string SyncStatus { get; set; } = string.Empty;
    public DateTime? LastSyncTime { get; set; }
    public int PendingChangeCount { get; set; }
    public int ConflictCount { get; set; }
    public int FailedOperationCount { get; set; }
    public int RetryOperationCount { get; set; }
    public int PendingAttachmentCount { get; set; }
    public int FailedAttachmentCount { get; set; }
    public int AttachmentUploadFailedCount { get; set; }
    public int AttachmentDownloadFailedCount { get; set; }
    public int AttachmentDeleteFailedCount { get; set; }
    public string DatabaseSizeDisplay { get; set; } = string.Empty;
    public int TotalFeatureCount { get; set; }
    public int LayerCount { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string PackageFileName { get; set; } = string.Empty;
    public string PackageSizeDisplay { get; set; } = string.Empty;
    public string MetadataCacheStatus { get; set; } = string.Empty;
    public int MetadataSourceCount { get; set; }
    public string FeatureCacheStatus { get; set; } = string.Empty;
    public int FeatureSourceCount { get; set; }
    public int CachedFeatureCount { get; set; }
    public long? LocalGeneration { get; set; }
    public long? ServerGeneration { get; set; }
}

public sealed class FieldDeviceDiagnosticsSnapshot
{
    public DateTime GeneratedAtUtc { get; init; }
    public string HealthStatus { get; init; } = "Unknown";
    public string HealthReason { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
    public string BuildNumber { get; init; } = string.Empty;
    public string BuildEnvironment { get; init; } = string.Empty;
    public string SourceDisplay { get; init; } = string.Empty;
    public string WorkflowRunDisplay { get; init; } = string.Empty;
    public string ServiceEndpointState { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string DeviceModel { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string Manufacturer { get; init; } = string.Empty;
    public string ConnectivityState { get; init; } = string.Empty;
    public bool ServerReachable { get; init; }
    public bool IsSyncing { get; init; }
    public bool IsRemoteSyncConfigured { get; init; }
    public string SyncStatus { get; init; } = string.Empty;
    public DateTime? LastSyncTime { get; init; }
    public string LastSyncAge { get; init; } = string.Empty;
    public int PendingChangeCount { get; init; }
    public int PendingAttachmentCount { get; init; }
    public int FailedOperationCount { get; init; }
    public int FailedAttachmentCount { get; init; }
    public int RetryOperationCount { get; init; }
    public int ConflictCount { get; init; }
    public string DatabaseSizeDisplay { get; init; } = string.Empty;
    public int TotalFeatureCount { get; init; }
    public int LayerCount { get; init; }
    public string PackageId { get; init; } = string.Empty;
    public string PackageFileName { get; init; } = string.Empty;
    public string PackageSizeDisplay { get; init; } = string.Empty;
    public string PackageState { get; init; } = string.Empty;
    public int CachedFeatureCount { get; init; }
    public long? LocalGeneration { get; init; }
    public long? ServerGeneration { get; init; }
    public IReadOnlyList<FieldDiagnosticFailureCategory> FailureCategories { get; init; } = [];
    public IReadOnlyList<string> SupportActions { get; init; } = [];
    public FieldDeviceInventoryDescriptor DeviceInventory { get; init; } = new();

    public string SupportActionsText => string.Join(Environment.NewLine, SupportActions);

    public string SummaryText
    {
        get
        {
            var lines = new List<string>
            {
                $"Health {HealthStatus}: {HealthReason}",
                $"App {AppVersion} ({BuildNumber})",
                $"Build {BuildEnvironment}; {SourceDisplay}",
                $"Workflow {WorkflowRunDisplay}",
                $"Endpoint {ServiceEndpointState}",
                $"Device {Manufacturer} {DeviceModel} ({DeviceType}, {Architecture})",
                $"OS {OperatingSystem}; platform {Platform}",
                $"Connectivity {ConnectivityState}; server reachable {ServerReachable}",
                $"Sync {SyncStatus}; remote configured {IsRemoteSyncConfigured}; active {IsSyncing}",
                $"Last sync {FormatDateTime(LastSyncTime)} ({LastSyncAge})",
                $"Pending changes {PendingChangeCount}; pending attachments {PendingAttachmentCount}",
                $"Failures operations {FailedOperationCount}; attachments {FailedAttachmentCount}; retries {RetryOperationCount}",
                $"Conflicts {ConflictCount}",
                $"Storage database {DatabaseSizeDisplay}; package {PackageSizeDisplay}; cached features {CachedFeatureCount}",
                $"Package {PackageId}; state {PackageState}; generations local {FormatGeneration(LocalGeneration)} server {FormatGeneration(ServerGeneration)}",
                "Support actions:",
            };

            lines.AddRange(SupportActions.Select(action => $"- {action}"));
            return string.Join(Environment.NewLine, lines);
        }
    }

    public static FieldDeviceDiagnosticsSnapshot Empty { get; } = Create(new FieldDeviceDiagnosticsInput());

    public static FieldDeviceDiagnosticsSnapshot Create(FieldDeviceDiagnosticsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var failureCategories = BuildFailureCategories(input);
        var healthStatus = DetermineHealthStatus(failureCategories);
        var supportActions = BuildSupportActions(input, failureCategories);
        var packageFileName = DiagnosticRedactor.RedactPath(input.PackageFileName);
        var packageState = BuildPackageState(input);
        var lastSyncAge = BuildLastSyncAge(input.LastSyncTime, input.GeneratedAtUtc);

        return new FieldDeviceDiagnosticsSnapshot
        {
            GeneratedAtUtc = input.GeneratedAtUtc,
            HealthStatus = healthStatus,
            HealthReason = BuildHealthReason(healthStatus, failureCategories),
            AppVersion = input.AppVersion,
            BuildNumber = input.BuildNumber,
            BuildEnvironment = input.BuildEnvironment,
            SourceDisplay = input.SourceDisplay,
            WorkflowRunDisplay = input.WorkflowRunDisplay,
            ServiceEndpointState = input.ServiceEndpointState,
            Platform = input.Platform,
            OperatingSystem = input.OperatingSystem,
            DeviceModel = input.DeviceModel,
            DeviceType = input.DeviceType,
            Architecture = input.Architecture,
            Manufacturer = input.Manufacturer,
            ConnectivityState = input.IsConnected ? "Online" : "Offline",
            ServerReachable = input.ServerReachable,
            IsSyncing = input.IsSyncing,
            IsRemoteSyncConfigured = input.IsRemoteSyncConfigured,
            SyncStatus = input.SyncStatus,
            LastSyncTime = input.LastSyncTime,
            LastSyncAge = lastSyncAge,
            PendingChangeCount = input.PendingChangeCount,
            PendingAttachmentCount = input.PendingAttachmentCount,
            FailedOperationCount = input.FailedOperationCount,
            FailedAttachmentCount = input.FailedAttachmentCount,
            RetryOperationCount = input.RetryOperationCount,
            ConflictCount = input.ConflictCount,
            DatabaseSizeDisplay = input.DatabaseSizeDisplay,
            TotalFeatureCount = input.TotalFeatureCount,
            LayerCount = input.LayerCount,
            PackageId = input.PackageId,
            PackageFileName = packageFileName,
            PackageSizeDisplay = input.PackageSizeDisplay,
            PackageState = packageState,
            CachedFeatureCount = input.CachedFeatureCount,
            LocalGeneration = input.LocalGeneration,
            ServerGeneration = input.ServerGeneration,
            FailureCategories = failureCategories,
            SupportActions = supportActions,
            DeviceInventory = BuildDeviceInventory(input, healthStatus, packageState, packageFileName),
        };
    }

    public IReadOnlyDictionary<string, object?> ToReportProperties()
    {
        return new Dictionary<string, object?>
        {
            ["healthStatus"] = HealthStatus,
            ["healthReason"] = HealthReason,
            ["appVersion"] = AppVersion,
            ["buildNumber"] = BuildNumber,
            ["platform"] = Platform,
            ["operatingSystem"] = OperatingSystem,
            ["remoteSyncConfigured"] = IsRemoteSyncConfigured,
            ["syncStatus"] = SyncStatus,
            ["lastSyncAge"] = LastSyncAge,
            ["pendingChanges"] = PendingChangeCount,
            ["pendingAttachments"] = PendingAttachmentCount,
            ["failedOperations"] = FailedOperationCount,
            ["failedAttachments"] = FailedAttachmentCount,
            ["conflicts"] = ConflictCount,
            ["packageState"] = PackageState,
            ["failureCategories"] = string.Join(",", FailureCategories.Select(category => category.Category)),
            ["supportActions"] = string.Join(" | ", SupportActions),
        };
    }

    private static IReadOnlyList<FieldDiagnosticFailureCategory> BuildFailureCategories(FieldDeviceDiagnosticsInput input)
    {
        var categories = new List<FieldDiagnosticFailureCategory>();

        if (!input.IsConnected)
        {
            categories.Add(new FieldDiagnosticFailureCategory("Connectivity", "Warning", 1, "Device is offline."));
        }
        else if (input.IsRemoteSyncConfigured && !input.ServerReachable)
        {
            categories.Add(new FieldDiagnosticFailureCategory("Connectivity", "Warning", 1, "Configured server is not reachable."));
        }

        if (!input.IsRemoteSyncConfigured)
        {
            categories.Add(new FieldDiagnosticFailureCategory("RemoteSync", "Warning", 1, "Remote sync is not configured."));
        }

        if (input.FailedOperationCount > 0)
        {
            categories.Add(new FieldDiagnosticFailureCategory("SyncFailures", "Critical", input.FailedOperationCount, "Local feature changes failed to sync."));
        }

        if (input.FailedAttachmentCount > 0)
        {
            categories.Add(new FieldDiagnosticFailureCategory("AttachmentFailures", "Critical", input.FailedAttachmentCount, BuildAttachmentFailureDetail(input)));
        }

        if (input.ConflictCount > 0)
        {
            categories.Add(new FieldDiagnosticFailureCategory("Conflicts", "Warning", input.ConflictCount, "Sync conflicts require review."));
        }

        var pendingTotal = input.PendingChangeCount + input.PendingAttachmentCount;
        if (pendingTotal > 0)
        {
            categories.Add(new FieldDiagnosticFailureCategory("PendingQueue", "Info", pendingTotal, "Local work remains queued for upload."));
        }

        if (input.RetryOperationCount > 0)
        {
            categories.Add(new FieldDiagnosticFailureCategory("Retries", "Warning", input.RetryOperationCount, "Sync retries are recorded."));
        }

        if (IsUnavailable(input.MetadataCacheStatus) || IsUnavailable(input.FeatureCacheStatus))
        {
            categories.Add(new FieldDiagnosticFailureCategory("OfflineCache", "Warning", 1, "Offline cache metadata or feature cache is unavailable."));
        }

        return categories;
    }

    private static string BuildAttachmentFailureDetail(FieldDeviceDiagnosticsInput input)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "Attachment sync failures: upload {0}, download {1}, delete {2}.",
            input.AttachmentUploadFailedCount,
            input.AttachmentDownloadFailedCount,
            input.AttachmentDeleteFailedCount);
    }

    private static string DetermineHealthStatus(IReadOnlyList<FieldDiagnosticFailureCategory> categories)
    {
        if (categories.Any(category => string.Equals(category.Severity, "Critical", StringComparison.OrdinalIgnoreCase)))
        {
            return "Critical";
        }

        if (categories.Any(category => string.Equals(category.Severity, "Warning", StringComparison.OrdinalIgnoreCase)))
        {
            return "Attention";
        }

        if (categories.Any(category => string.Equals(category.Severity, "Info", StringComparison.OrdinalIgnoreCase)))
        {
            return "Pending";
        }

        return "Healthy";
    }

    private static string BuildHealthReason(string status, IReadOnlyList<FieldDiagnosticFailureCategory> categories)
    {
        if (categories.Count == 0)
        {
            return "No local sync or cache issues detected.";
        }

        var highest = categories
            .Where(category => status == "Critical"
                ? category.Severity == "Critical"
                : status == "Attention"
                    ? category.Severity == "Warning"
                    : category.Severity == "Info")
            .Select(category => category.Detail)
            .FirstOrDefault();

        return highest ?? categories[0].Detail;
    }

    private static IReadOnlyList<string> BuildSupportActions(
        FieldDeviceDiagnosticsInput input,
        IReadOnlyList<FieldDiagnosticFailureCategory> categories)
    {
        if (categories.Count == 0)
        {
            return ["No local action required."];
        }

        var actions = new List<string>();

        if (input.FailedOperationCount > 0)
        {
            actions.Add("Review sync error details and retry push when the server is reachable.");
        }

        if (input.FailedAttachmentCount > 0)
        {
            actions.Add("Resolve attachment upload/download failures before clearing device media.");
        }

        if (input.ConflictCount > 0)
        {
            actions.Add("Open conflict review and resolve queued records.");
        }

        if (input.PendingChangeCount > 0 || input.PendingAttachmentCount > 0)
        {
            actions.Add("Keep the app open on a stable connection until pending work is uploaded.");
        }

        if (!input.IsConnected)
        {
            actions.Add("Reconnect to Wi-Fi or cellular data and refresh diagnostics.");
        }
        else if (input.IsRemoteSyncConfigured && !input.ServerReachable)
        {
            actions.Add("Verify the configured server endpoint is reachable from this network.");
        }

        if (!input.IsRemoteSyncConfigured)
        {
            actions.Add("Configure remote sync before expecting queued edits to leave the device.");
        }

        if (IsUnavailable(input.MetadataCacheStatus) || IsUnavailable(input.FeatureCacheStatus))
        {
            actions.Add("Refresh offline project metadata and verify the package cache is present.");
        }

        return actions.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static FieldDeviceInventoryDescriptor BuildDeviceInventory(
        FieldDeviceDiagnosticsInput input,
        string healthStatus,
        string packageState,
        string packageFileName)
    {
        return new FieldDeviceInventoryDescriptor
        {
            AppVersion = input.AppVersion,
            BuildNumber = input.BuildNumber,
            BuildEnvironment = input.BuildEnvironment,
            Platform = input.Platform,
            OperatingSystem = input.OperatingSystem,
            DeviceModel = input.DeviceModel,
            DeviceType = input.DeviceType,
            Architecture = input.Architecture,
            HealthStatus = healthStatus,
            RemoteSyncConfigured = input.IsRemoteSyncConfigured,
            LastSyncTime = input.LastSyncTime,
            PendingChangeCount = input.PendingChangeCount,
            PendingAttachmentCount = input.PendingAttachmentCount,
            ConflictCount = input.ConflictCount,
            PackageId = input.PackageId,
            PackageFileName = packageFileName,
            PackageState = packageState,
            Capabilities =
            [
                "offline-cache",
                "sync-health",
                "sanitized-diagnostic-export",
                "exception-report-queue"
            ],
        };
    }

    private static string BuildPackageState(FieldDeviceDiagnosticsInput input)
    {
        return $"metadata {BlankToUnknown(input.MetadataCacheStatus)} ({input.MetadataSourceCount} sources); " +
            $"features {BlankToUnknown(input.FeatureCacheStatus)} ({input.FeatureSourceCount} sources)";
    }

    private static string BuildLastSyncAge(DateTime? lastSyncTime, DateTime generatedAtUtc)
    {
        if (!lastSyncTime.HasValue || lastSyncTime.Value == default)
        {
            return "never";
        }

        var lastSyncUtc = ToUtc(lastSyncTime.Value);
        var elapsed = generatedAtUtc - lastSyncUtc;
        if (elapsed < TimeSpan.Zero)
        {
            return "in the future";
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "less than 1 minute ago";
        }

        if (elapsed.TotalHours < 1)
        {
            return FormatAge((int)elapsed.TotalMinutes, "minute");
        }

        if (elapsed.TotalDays < 1)
        {
            return FormatAge((int)elapsed.TotalHours, "hour");
        }

        return FormatAge((int)elapsed.TotalDays, "day");
    }

    private static string FormatAge(int value, string unit)
    {
        return value == 1
            ? $"1 {unit} ago"
            : $"{value} {unit}s ago";
    }

    private static bool IsUnavailable(string? status)
    {
        return string.IsNullOrWhiteSpace(status) ||
            status.Equals("Missing", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Unavailable", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Error", StringComparison.OrdinalIgnoreCase);
    }

    private static string BlankToUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }

    private static string FormatDateTime(DateTime? value)
    {
        return value.HasValue && value.Value != default
            ? ToUtc(value.Value).ToString("u", CultureInfo.InvariantCulture)
            : "never";
    }

    private static string FormatGeneration(long? generation)
    {
        return generation?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
    }
}

public sealed record FieldDiagnosticFailureCategory(
    string Category,
    string Severity,
    int Count,
    string Detail);

public sealed class FieldDeviceInventoryDescriptor
{
    public string SchemaVersion { get; init; } = "honua.mobile.device-inventory.v1";
    public string AppVersion { get; init; } = string.Empty;
    public string BuildNumber { get; init; } = string.Empty;
    public string BuildEnvironment { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string DeviceModel { get; init; } = string.Empty;
    public string DeviceType { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string HealthStatus { get; init; } = string.Empty;
    public bool RemoteSyncConfigured { get; init; }
    public DateTime? LastSyncTime { get; init; }
    public int PendingChangeCount { get; init; }
    public int PendingAttachmentCount { get; init; }
    public int ConflictCount { get; init; }
    public string PackageId { get; init; } = string.Empty;
    public string PackageFileName { get; init; } = string.Empty;
    public string PackageState { get; init; } = string.Empty;
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}
