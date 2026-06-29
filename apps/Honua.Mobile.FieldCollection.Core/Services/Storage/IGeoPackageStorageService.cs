using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Ai;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Metadata;
using Honua.Mobile.FieldCollection.Services.Storage.Models;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Projects;
using StorageConflictResolution = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictResolution;

namespace Honua.Mobile.FieldCollection.Services.Storage;

/// <summary>
/// Facade abstraction over <see cref="GeoPackageStorageService"/>, the offline GeoPackage store for
/// field data collection. Introduced for honua-mobile#315 so consumers depend on a role contract
/// rather than the concrete 2,700-line god class; this is the first seam toward the finer-grained
/// role interfaces (feature store, change log, attachment store, conflict store, sync-session store)
/// tracked in that issue. The surface intentionally mirrors the concrete type one-to-one so the
/// refactor is behavior-preserving.
/// </summary>
public interface IGeoPackageStorageService : IDisposable, IAsyncDisposable
{
    Task<bool> InitializeAsync();

    // Feature storage
    Task<string> StoreFeatureAsync(Feature feature);
    Task<IReadOnlyList<string>> StoreFeaturesAsync(IReadOnlyCollection<Feature> features);
    Task<Feature?> GetFeatureAsync(string featureId, int layerId);
    Task<List<Feature>> QueryFeaturesAsync(int layerId, SpatialQuery? spatialQuery = null);
    Task<bool> UpdateFeatureAsync(Feature feature);
    Task<bool> DeleteFeatureAsync(string featureId, int layerId);
    Task<string> ApplyRemoteFeatureAsync(Feature feature);
    Task<bool> ApplyRemoteDeleteAsync(string featureId, int layerId);

    // Change tracking
    Task<List<ChangeRecord>> GetPendingChangesAsync(int? layerId = null);
    Task MarkChangesAsSynced(List<string> changeIds);
    Task<int> GetPendingChangesCountAsync();

    // Attachment storage
    Task StoreAttachmentMetadataAsync(AttachmentInfo attachment);
    Task<AttachmentInfo?> GetAttachmentMetadataAsync(string attachmentId);
    Task<AttachmentInfo?> GetAttachmentByRemoteIdAsync(int layerId, string featureId, long remoteAttachmentId);
    Task<List<AttachmentInfo>> GetAttachmentsForFeatureAsync(string featureId, int? layerId = null, bool includeDeleted = false);
    Task<Dictionary<string, List<AttachmentInfo>>> GetAttachmentsForFeaturesAsync(IReadOnlyCollection<string> featureIds, int? layerId = null, bool includeDeleted = false);
    Task<List<AttachmentInfo>> GetPendingAttachmentChangesAsync();
    Task MarkAttachmentUploadedAsync(string attachmentId, long remoteAttachmentId, string? remoteGlobalId, DateTime uploadedAt);
    Task MarkAttachmentSyncedAsync(string attachmentId);
    Task MarkAttachmentPendingDeleteAsync(string attachmentId);
    Task MarkAttachmentDeletedSyncedAsync(string attachmentId);
    Task MarkAttachmentSyncFailedAsync(string attachmentId, AttachmentSyncStatus failedStatus, string errorMessage);
    Task UpdateAttachmentAiStateAsync(string attachmentId, MobileAiMediaState? state);
    Task<int> GetPendingAttachmentChangesCountAsync();

    // Sync session history
    Task StoreSyncSessionAsync(SyncSession session);
    Task UpdateSyncSessionAsync(SyncSession session);
    Task<IReadOnlyList<SyncSession>> GetSyncSessionsAsync(int limit = 50);

    // Conflict tracking
    Task StoreConflictAsync(ConflictRecord conflict);
    Task<List<ConflictRecord>> GetUnresolvedConflictsAsync();
    Task<ConflictRecord?> GetConflictAsync(string conflictId);
    Task MarkConflictResolvedAsync(string conflictId, StorageConflictResolution resolution, string? resolvedData);
    Task MarkConflictDeferredAsync(string conflictId, string? reason);

    // Project catalog
    Task UpsertProjectCatalogEntryAsync(FieldProjectCatalogEntry entry);
    Task<FieldProjectCatalogEntry?> GetProjectCatalogEntryAsync(string projectId);
    Task<IReadOnlyList<FieldProjectCatalogEntry>> GetProjectCatalogEntriesAsync(bool includeArchived = false);
    Task UpdateProjectCatalogStateAsync(string projectId, FieldProjectCatalogState state, DateTime? updatedAtUtc = null);
    Task MarkProjectCatalogEntryOpenedAsync(string projectId, DateTime? openedAtUtc = null);
    Task MarkProjectCatalogValidationAsync(string projectId, FieldProjectValidationStatus status, int issueCount, DateTime? validatedAtUtc = null);
    Task MarkProjectCatalogSimulationRunAsync(string projectId, DateTime? simulatedAtUtc = null);
    Task MarkProjectCatalogExportedAsync(string projectId, DateTime? exportedAtUtc = null);
    Task<bool> DeleteProjectCatalogEntryAsync(string projectId);

    // Field assignments
    Task UpsertFieldTaskPacketsAsync(string projectId, IReadOnlyList<FieldTaskPacket> taskPackets, IReadOnlyDictionary<string, string?> bindingSourceIds);
    Task<IReadOnlyList<LocalFieldAssignmentInfo>> GetFieldAssignmentsAsync(LocalFieldAssignmentFilter? filter = null);
    Task<bool> UpdateFieldAssignmentStatusAsync(string assignmentId, FieldAssignmentStatus status, DateTime? updatedAtUtc = null);
    Task<int> DeleteFieldAssignmentsForProjectAsync(string projectId);

    // Layer management
    Task<bool> CreateLayerAsync(LayerInfo layer);
    Task<List<LayerInfo>> GetLayersAsync();

    // Storage statistics
    Task<StorageStatistics> GetStorageStatisticsAsync();
    Task<OfflineCacheDiagnostics> GetOfflineCacheDiagnosticsAsync();
    Task CompactAsync();
}
