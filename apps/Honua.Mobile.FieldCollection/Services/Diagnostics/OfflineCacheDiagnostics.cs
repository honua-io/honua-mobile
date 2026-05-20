namespace Honua.Mobile.FieldCollection.Services.Diagnostics;

public sealed class OfflineCacheDiagnostics
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageFileName { get; set; } = string.Empty;
    public long PackageSizeBytes { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public long? LocalGeneration { get; set; }
    public long? ServerGeneration { get; set; }
    public MetadataCacheDiagnostics MetadataCache { get; set; } = new();
    public FeatureCacheDiagnostics FeatureCache { get; set; } = new();
    public OfflineOperationDiagnostics Operations { get; set; } = new();
    public IReadOnlyList<OfflineConflictReviewItem> ConflictReview { get; set; } = Array.Empty<OfflineConflictReviewItem>();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string PackageSizeDisplay => FormatBytes(PackageSizeBytes);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}

public sealed class MetadataCacheDiagnostics
{
    public string Status { get; set; } = "Missing";
    public int SourceCount { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
    public IReadOnlyList<OfflineSourceDiagnostics> Sources { get; set; } = Array.Empty<OfflineSourceDiagnostics>();
}

public sealed class FeatureCacheDiagnostics
{
    public string Status { get; set; } = "Empty";
    public int SourceCount { get; set; }
    public int TotalFeatureCount { get; set; }
    public long SizeBytes { get; set; }
    public IReadOnlyList<OfflineSourceDiagnostics> Sources { get; set; } = Array.Empty<OfflineSourceDiagnostics>();
}

public sealed class OfflineSourceDiagnostics
{
    public string SourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int FeatureCount { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public string? SourceUrl { get; set; }
}

public sealed class OfflineOperationDiagnostics
{
    public int PendingCount { get; set; }
    public int ClaimedCount { get; set; }
    public int SucceededCount { get; set; }
    public int FailedCount { get; set; }
    public int RetryCount { get; set; }
    public int ConflictCount { get; set; }
}

public sealed class OfflineConflictReviewItem
{
    public string ConflictId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public string ConflictType { get; set; } = string.Empty;
    public string Status { get; set; } = "Needs review";
    public string Reason { get; set; } = string.Empty;
    public string LocalState { get; set; } = string.Empty;
    public string ServerState { get; set; } = string.Empty;
    public DateTime DetectedAtUtc { get; set; }
    public IReadOnlyList<string> ResolutionActions { get; set; } = Array.Empty<string>();
}
