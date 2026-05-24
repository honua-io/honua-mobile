using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Projects;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Models;

public sealed class LocalFieldProjectPackageImportRequest
{
    public required string ManifestPath { get; init; }
    public required string DestinationRootDirectory { get; init; }
    public string? ImportSource { get; init; }
    public bool OverwriteExisting { get; init; }
}

public sealed class LocalFieldProjectPackageImportResult
{
    public string? ProjectId { get; init; }
    public bool Imported { get; init; }
    public FieldProjectCatalogEntry? CatalogEntry { get; init; }
    public IReadOnlyList<LocalFieldProjectPackageDiagnostic> Diagnostics { get; init; } = [];
    public IReadOnlyList<LocalFieldProjectPackageImportedFile> ImportedFiles { get; init; } = [];
    public IReadOnlyList<LayerInfo> Layers { get; init; } = [];
    public long CopiedBytes { get; init; }
    public string? InstalledManifestPath { get; init; }
}

public sealed class LocalFieldProjectPackageDiagnostic
{
    public required string Code { get; init; }
    public required string Path { get; init; }
    public required string Message { get; init; }
    public LocalFieldProjectPackageDiagnosticSeverity Severity { get; init; } = LocalFieldProjectPackageDiagnosticSeverity.Error;
}

public enum LocalFieldProjectPackageDiagnosticSeverity
{
    Warning,
    Error
}

public sealed class LocalFieldProjectPackageImportedFile
{
    public required string PackageId { get; init; }
    public required string RelativePath { get; init; }
    public required string DestinationPath { get; init; }
    public long SizeBytes { get; init; }
    public string? Sha256 { get; init; }
}

public sealed class LocalFieldRecordLifecycleTransitionResult
{
    public bool Succeeded { get; init; }
    public string? ReasonCode { get; init; }
    public string? Reason { get; init; }
    public RecordStatus FromStatus { get; init; }
    public RecordStatus ToStatus { get; init; }
    public Feature? Feature { get; init; }
}

public sealed class LocalFieldAssignmentInfo
{
    public string AssignmentId { get; init; } = string.Empty;
    public string TaskPacketId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string BindingId { get; init; } = string.Empty;
    public string? SourceId { get; init; }
    public string? AssigneeUserId { get; init; }
    public string? CrewId { get; init; }
    public FieldAssignmentPriority Priority { get; init; } = FieldAssignmentPriority.Normal;
    public FieldAssignmentStatus Status { get; init; } = FieldAssignmentStatus.NotStarted;
    public DateTimeOffset? DueAtUtc { get; init; }
    public SourceQuery? WorkQuery { get; init; }
    public IReadOnlyList<string> RecordIds { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public DateTime ImportedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
}

public sealed class LocalFieldAssignmentFilter
{
    public string? ProjectId { get; init; }
    public string? BindingId { get; init; }
    public string? SourceId { get; init; }
    public string? AssigneeUserId { get; init; }
    public string? CrewId { get; init; }
    public FieldAssignmentStatus? Status { get; init; }
    public FieldAssignmentPriority? MinimumPriority { get; init; }
    public DateTimeOffset? DueBeforeUtc { get; init; }
    public FeatureBoundingBox? IntersectsExtent { get; init; }
}
