using System.Globalization;
using System.Text;
using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.FieldCollection.Services.Storage.Models;
using Microsoft.Extensions.Logging;

namespace Honua.Mobile.FieldCollection.Services;

public interface ILocalRecordExportService
{
    Task<LocalRecordExportResult> ExportLayerAsync(
        LayerInfo layer,
        string? exportRootDirectory = null,
        CancellationToken cancellationToken = default);
}

public sealed class LocalRecordExportResult
{
    public string ExportDirectory { get; init; } = string.Empty;
    public string CsvPath { get; init; } = string.Empty;
    public string GeoJsonPath { get; init; } = string.Empty;
    public string AttachmentManifestPath { get; init; } = string.Empty;
    public string EvidenceManifestPath { get; init; } = string.Empty;
    public DateTime ExportedAtUtc { get; init; }
    public int LayerId { get; init; }
    public string LayerName { get; init; } = string.Empty;
    public int RecordCount { get; init; }
    public int AttachmentCount { get; init; }
    public int MediaFileCount { get; init; }
    public bool IsEmpty => RecordCount == 0;
}

public sealed class LocalRecordExportService : ILocalRecordExportService
{
    private const string Redacted = "[redacted]";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string[] BaseCsvColumns =
    [
        "feature_id",
        "layer_id",
        "layer_name",
        "geometry_type",
        "created_at_utc",
        "modified_at_utc",
        "updated_at_utc",
        "version",
        "pending_sync",
        "pending_status",
        "pending_operations",
        "last_pending_change_at_utc",
        "attachment_count",
        "pending_attachment_count"
    ];

    private readonly GeoPackageStorageService _storage;
    private readonly string? _defaultExportRootDirectory;
    private readonly ILogger<LocalRecordExportService>? _logger;

    public LocalRecordExportService(
        GeoPackageStorageService storage,
        string? defaultExportRootDirectory = null,
        ILogger<LocalRecordExportService>? logger = null)
    {
        _storage = storage;
        _defaultExportRootDirectory = defaultExportRootDirectory;
        _logger = logger;
    }

    public async Task<LocalRecordExportResult> ExportLayerAsync(
        LayerInfo layer,
        string? exportRootDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layer);

        var exportedAtUtc = DateTime.UtcNow;
        var rootDirectory = ResolveExportRoot(exportRootDirectory ?? _defaultExportRootDirectory);
        var exportDirectory = Path.Combine(rootDirectory, BuildExportDirectoryName(layer, exportedAtUtc));

        try
        {
            Directory.CreateDirectory(exportDirectory);

            var features = (await _storage.QueryFeaturesAsync(layer.Id).ConfigureAwait(false))
                .OrderBy(feature => feature.CreatedAt)
                .ThenBy(feature => feature.Id, StringComparer.Ordinal)
                .ToList();
            cancellationToken.ThrowIfCancellationRequested();

            var pendingChanges = await _storage.GetPendingChangesAsync(layer.Id).ConfigureAwait(false);
            var pendingByFeature = pendingChanges
                .GroupBy(change => change.FeatureId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            var attachmentsByFeature = new Dictionary<string, IReadOnlyList<AttachmentInfo>>(StringComparer.Ordinal);
            foreach (var feature in features)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attachmentsByFeature[feature.Id] = await _storage
                    .GetAttachmentsForFeatureAsync(feature.Id, layer.Id, includeDeleted: true)
                    .ConfigureAwait(false);
            }

            var attributeColumns = features
                .SelectMany(feature => feature.Attributes.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(column => column, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var csvPath = Path.Combine(exportDirectory, "records.csv");
            var geoJsonPath = Path.Combine(exportDirectory, "records.geojson");
            var attachmentManifestPath = Path.Combine(exportDirectory, "attachments-manifest.json");
            var evidenceManifestPath = Path.Combine(exportDirectory, "honua-evidence.json");
            var mediaDirectory = Path.Combine(exportDirectory, "media");

            await WriteCsvAsync(
                csvPath,
                layer,
                features,
                attributeColumns,
                pendingByFeature,
                attachmentsByFeature,
                cancellationToken).ConfigureAwait(false);

            await WriteGeoJsonAsync(
                geoJsonPath,
                layer,
                exportedAtUtc,
                features,
                pendingByFeature,
                attachmentsByFeature,
                cancellationToken).ConfigureAwait(false);

            var attachmentSummary = await WriteAttachmentManifestAsync(
                attachmentManifestPath,
                mediaDirectory,
                layer,
                exportedAtUtc,
                features,
                attachmentsByFeature,
                cancellationToken).ConfigureAwait(false);

            var catalogEntries = await _storage.GetProjectCatalogEntriesAsync(includeArchived: true).ConfigureAwait(false);
            var diagnostics = await _storage.GetOfflineCacheDiagnosticsAsync().ConfigureAwait(false);
            await WriteEvidenceManifestAsync(
                evidenceManifestPath,
                layer,
                exportedAtUtc,
                features.Count,
                attachmentSummary,
                catalogEntries,
                diagnostics,
                cancellationToken).ConfigureAwait(false);

            var matchingCatalogEntry = FindMatchingCatalogEntry(catalogEntries, layer);
            if (matchingCatalogEntry is not null)
            {
                await _storage.MarkProjectCatalogExportedAsync(
                    matchingCatalogEntry.ProjectId,
                    exportedAtUtc).ConfigureAwait(false);
            }

            return new LocalRecordExportResult
            {
                ExportDirectory = exportDirectory,
                CsvPath = csvPath,
                GeoJsonPath = geoJsonPath,
                AttachmentManifestPath = attachmentManifestPath,
                EvidenceManifestPath = evidenceManifestPath,
                ExportedAtUtc = exportedAtUtc,
                LayerId = layer.Id,
                LayerName = layer.Name,
                RecordCount = features.Count,
                AttachmentCount = attachmentSummary.AttachmentCount,
                MediaFileCount = attachmentSummary.CopiedFileCount
            };
        }
        catch (OperationCanceledException)
        {
            TryDeletePartialExport(exportDirectory);
            throw;
        }
        catch (Exception ex)
        {
            TryDeletePartialExport(exportDirectory);
            _logger?.LogError(ex, "Failed to export records for layer {LayerId}", layer.Id);
            throw new InvalidOperationException($"Failed to export records for layer {layer.Name}.", ex);
        }
    }

    private static async Task WriteCsvAsync(
        string csvPath,
        LayerInfo layer,
        IReadOnlyList<Feature> features,
        IReadOnlyList<string> attributeColumns,
        IReadOnlyDictionary<string, List<ChangeRecord>> pendingByFeature,
        IReadOnlyDictionary<string, IReadOnlyList<AttachmentInfo>> attachmentsByFeature,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var columns = BaseCsvColumns
            .Concat(attributeColumns.Select(column => $"attribute_{column}"))
            .ToList();

        await writer.WriteLineAsync(string.Join(",", columns.Select(EscapeCsv))).ConfigureAwait(false);

        foreach (var feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pendingChanges = GetPendingChanges(pendingByFeature, feature.Id);
            var attachments = GetAttachments(attachmentsByFeature, feature.Id);
            var pendingAttachmentCount = attachments.Count(IsPendingAttachment);
            var isPending = feature.IsPendingSync || pendingChanges.Count > 0 || pendingAttachmentCount > 0;

            var values = new List<string>
            {
                feature.Id,
                layer.Id.ToString(CultureInfo.InvariantCulture),
                layer.Name,
                feature.Geometry?.Type ?? string.Empty,
                FormatDateTime(feature.CreatedAt),
                FormatDateTime(feature.ModifiedAt),
                FormatDateTime(feature.UpdatedAt),
                feature.Version.ToString(CultureInfo.InvariantCulture),
                isPending ? "true" : "false",
                isPending ? "pending" : "synced",
                FormatPendingOperations(pendingChanges),
                FormatDateTime(GetLatestPendingTimestamp(pendingChanges)),
                attachments.Count.ToString(CultureInfo.InvariantCulture),
                pendingAttachmentCount.ToString(CultureInfo.InvariantCulture)
            };

            foreach (var attributeColumn in attributeColumns)
            {
                TryGetAttributeValue(feature.Attributes, attributeColumn, out var value);
                values.Add(FormatAttributeValue(SanitizeAttributeValue(attributeColumn, value)));
            }

            await writer.WriteLineAsync(string.Join(",", values.Select(EscapeCsv))).ConfigureAwait(false);
        }
    }

    private static async Task WriteGeoJsonAsync(
        string geoJsonPath,
        LayerInfo layer,
        DateTime exportedAtUtc,
        IReadOnlyList<Feature> features,
        IReadOnlyDictionary<string, List<ChangeRecord>> pendingByFeature,
        IReadOnlyDictionary<string, IReadOnlyList<AttachmentInfo>> attachmentsByFeature,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(geoJsonPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("type", "FeatureCollection");
        writer.WriteString("name", layer.Name);

        writer.WritePropertyName("honua");
        writer.WriteStartObject();
        writer.WriteNumber("layerId", layer.Id);
        writer.WriteString("layerName", layer.Name);
        writer.WriteString("exportedAtUtc", FormatDateTime(exportedAtUtc));
        writer.WriteNumber("recordCount", features.Count);
        writer.WriteString("formatVersion", "1.0");
        writer.WriteEndObject();

        writer.WritePropertyName("features");
        writer.WriteStartArray();
        foreach (var feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pendingChanges = GetPendingChanges(pendingByFeature, feature.Id);
            var attachments = GetAttachments(attachmentsByFeature, feature.Id);
            var pendingAttachmentCount = attachments.Count(IsPendingAttachment);
            var isPending = feature.IsPendingSync || pendingChanges.Count > 0 || pendingAttachmentCount > 0;

            writer.WriteStartObject();
            writer.WriteString("type", "Feature");
            writer.WriteString("id", feature.Id);

            writer.WritePropertyName("geometry");
            if (feature.Geometry == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                GeometryJson.ToJsonElement(feature.Geometry).WriteTo(writer);
            }

            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            writer.WriteString("feature_id", feature.Id);
            writer.WriteNumber("layer_id", layer.Id);
            writer.WriteString("layer_name", layer.Name);
            writer.WriteString("geometry_type", feature.Geometry?.Type ?? string.Empty);
            writer.WriteString("created_at_utc", FormatDateTime(feature.CreatedAt));
            writer.WriteString("modified_at_utc", FormatDateTime(feature.ModifiedAt));
            writer.WriteString("updated_at_utc", FormatDateTime(feature.UpdatedAt));
            writer.WriteNumber("version", feature.Version);
            writer.WriteBoolean("pending_sync", isPending);
            writer.WriteString("pending_status", isPending ? "pending" : "synced");
            writer.WriteString("pending_operations", FormatPendingOperations(pendingChanges));
            writer.WriteString("last_pending_change_at_utc", FormatDateTime(GetLatestPendingTimestamp(pendingChanges)));
            writer.WriteNumber("attachment_count", attachments.Count);
            writer.WriteNumber("pending_attachment_count", pendingAttachmentCount);
            writer.WritePropertyName("attributes");
            JsonSerializer.Serialize(writer, SanitizeAttributes(feature.Attributes), JsonOptions);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AttachmentExportSummary> WriteAttachmentManifestAsync(
        string manifestPath,
        string mediaDirectory,
        LayerInfo layer,
        DateTime exportedAtUtc,
        IReadOnlyList<Feature> features,
        IReadOnlyDictionary<string, IReadOnlyList<AttachmentInfo>> attachmentsByFeature,
        CancellationToken cancellationToken)
    {
        var attachments = features
            .SelectMany(feature => GetAttachments(attachmentsByFeature, feature.Id))
            .OrderBy(attachment => attachment.FeatureId, StringComparer.Ordinal)
            .ThenBy(attachment => attachment.CreatedAt)
            .ToList();

        var copiedFileCount = 0;
        var manifestAttachments = new List<object>(attachments.Count);

        foreach (var attachment in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var exportedRelativePath = CopyLocalAttachmentContent(
                attachment,
                mediaDirectory,
                copiedFileCount + 1);

            if (!string.IsNullOrWhiteSpace(exportedRelativePath))
            {
                copiedFileCount++;
            }

            manifestAttachments.Add(SanitizeAttachment(
                attachment,
                exportedRelativePath,
                hasLocalContent: !string.IsNullOrWhiteSpace(exportedRelativePath)));
        }

        var manifest = new
        {
            generatedAtUtc = FormatDateTime(exportedAtUtc),
            layerId = layer.Id,
            layerName = layer.Name,
            recordCount = features.Count,
            attachmentCount = attachments.Count,
            copiedFileCount,
            localPathsRedacted = true,
            contentIncluded = copiedFileCount > 0,
            attachments = manifestAttachments
        };

        await using var stream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        return new AttachmentExportSummary(attachments.Count, copiedFileCount);
    }

    private static object SanitizeAttachment(
        AttachmentInfo attachment,
        string? exportedRelativePath,
        bool hasLocalContent)
    {
        return new
        {
            id = attachment.Id,
            featureId = attachment.FeatureId,
            layerId = attachment.LayerId,
            remoteAttachmentId = attachment.RemoteAttachmentId,
            remoteGlobalId = DiagnosticRedactor.RedactSensitiveText(attachment.RemoteGlobalId),
            fileName = RedactFileName(attachment.FileName),
            contentType = attachment.ContentType,
            payloadKind = attachment.PayloadKind.ToString(),
            sizeBytes = attachment.SizeBytes,
            localFileName = string.IsNullOrWhiteSpace(attachment.LocalPath) ? null : RedactFileName(attachment.LocalPath),
            hasLocalContent,
            exportedRelativePath,
            createdAtUtc = FormatDateTime(attachment.CreatedAt),
            updatedAtUtc = FormatDateTime(attachment.UpdatedAt),
            uploadedAtUtc = FormatDateTime(attachment.UploadedAt),
            lastSyncedAtUtc = FormatDateTime(attachment.LastSyncedAt),
            description = DiagnosticRedactor.RedactSensitiveText(attachment.Description),
            captureLocation = SanitizeCaptureLocation(attachment.CaptureLocation),
            thumbnailUrl = DiagnosticRedactor.RedactUrl(attachment.ThumbnailUrl),
            syncStatus = attachment.SyncStatus.ToString(),
            retryCount = attachment.RetryCount,
            lastError = DiagnosticRedactor.RedactSensitiveText(attachment.LastError),
            isDeleted = attachment.IsDeleted,
            deletedAtUtc = FormatDateTime(attachment.DeletedAt)
        };
    }

    private static string? CopyLocalAttachmentContent(
        AttachmentInfo attachment,
        string mediaDirectory,
        int ordinal)
    {
        if (attachment.IsDeleted ||
            string.IsNullOrWhiteSpace(attachment.LocalPath) ||
            !File.Exists(attachment.LocalPath))
        {
            return null;
        }

        Directory.CreateDirectory(mediaDirectory);
        var fileName = BuildExportedMediaFileName(attachment, ordinal);
        var destinationPath = Path.Combine(mediaDirectory, fileName);
        File.Copy(attachment.LocalPath, destinationPath, overwrite: false);

        return ToPortableRelativePath(Path.Combine("media", fileName));
    }

    private static string BuildExportedMediaFileName(AttachmentInfo attachment, int ordinal)
    {
        var attachmentId = SanitizePathSegment(string.IsNullOrWhiteSpace(attachment.Id)
            ? "attachment"
            : attachment.Id);
        var extension = GetSafeExtension(attachment);

        return $"{ordinal:0000}_{attachmentId}{extension}";
    }

    private static string GetSafeExtension(AttachmentInfo attachment)
    {
        var sourceFileName = !string.IsNullOrWhiteSpace(attachment.LocalPath)
            ? attachment.LocalPath
            : attachment.FileName;
        var extension = Path.GetExtension(RedactFileName(sourceFileName));
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var sanitized = SanitizePathSegment(extension.TrimStart('.'));
        return string.IsNullOrWhiteSpace(sanitized) ? string.Empty : $".{sanitized}";
    }

    private static string ToPortableRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static async Task WriteEvidenceManifestAsync(
        string evidenceManifestPath,
        LayerInfo layer,
        DateTime exportedAtUtc,
        int recordCount,
        AttachmentExportSummary attachmentSummary,
        IReadOnlyList<FieldProjectCatalogEntry> catalogEntries,
        OfflineCacheDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var matchingCatalogEntry = FindMatchingCatalogEntry(catalogEntries, layer);
        var manifest = new
        {
            formatVersion = "honua.local-export-evidence.v1",
            generatedAtUtc = FormatDateTime(exportedAtUtc),
            noCloud = true,
            cloudUploadIncluded = false,
            layer = new
            {
                id = layer.Id,
                serviceId = layer.ServiceId,
                sourceId = layer.SourceId,
                name = layer.Name,
                geometryType = layer.GeometryType.ToString(),
                isEditable = layer.IsEditable
            },
            files = new
            {
                recordsCsv = "records.csv",
                recordsGeoJson = "records.geojson",
                attachmentsManifest = "attachments-manifest.json",
                evidenceManifest = "honua-evidence.json",
                mediaDirectory = attachmentSummary.CopiedFileCount > 0 ? "media" : null
            },
            counts = new
            {
                records = recordCount,
                attachments = attachmentSummary.AttachmentCount,
                copiedMediaFiles = attachmentSummary.CopiedFileCount,
                pendingOperations = diagnostics.Operations.PendingCount,
                conflicts = diagnostics.Operations.ConflictCount,
                conflictReviewItems = diagnostics.ConflictReview.Count
            },
            validationSummary = new
            {
                status = "not-run",
                issueCount = 0,
                source = "local-export"
            },
            redaction = new
            {
                localPathsRedacted = true,
                urlsRedacted = true,
                sensitiveValuesRedacted = true
            },
            projectCatalog = new
            {
                matchedProjectId = matchingCatalogEntry?.ProjectId,
                entries = catalogEntries
                    .Select(entry => SanitizeCatalogEntry(entry, matchingCatalogEntry?.ProjectId))
                    .ToList()
            },
            diagnostics = SanitizeDiagnostics(diagnostics),
            conflicts = diagnostics.ConflictReview.Select(SanitizeConflictReviewItem).ToList()
        };

        await using var stream = new FileStream(evidenceManifestPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static object SanitizeCatalogEntry(FieldProjectCatalogEntry entry, string? matchedProjectId)
    {
        return new
        {
            projectId = entry.ProjectId,
            serviceId = entry.ServiceId,
            packageId = entry.PackageId,
            version = DiagnosticRedactor.RedactSensitiveText(entry.Version),
            name = DiagnosticRedactor.RedactSensitiveText(entry.Name),
            description = DiagnosticRedactor.RedactSensitiveText(entry.Description),
            state = entry.State.ToString(),
            validationStatus = entry.ValidationStatus.ToString(),
            validationIssueCount = entry.ValidationIssueCount,
            layerCount = entry.LayerCount,
            packageSizeBytes = entry.PackageSizeBytes,
            mediaSizeBytes = entry.MediaSizeBytes,
            localStorageFileName = string.IsNullOrWhiteSpace(entry.LocalStoragePath)
                ? null
                : DiagnosticRedactor.RedactPath(entry.LocalStoragePath),
            manifestFileName = string.IsNullOrWhiteSpace(entry.ManifestPath)
                ? null
                : DiagnosticRedactor.RedactPath(entry.ManifestPath),
            importSource = RedactImportSource(entry.ImportSource),
            packageDigest = entry.PackageDigest,
            importedAtUtc = FormatDateTime(entry.ImportedAtUtc),
            updatedAtUtc = FormatDateTime(entry.UpdatedAtUtc),
            lastOpenedAtUtc = FormatDateTime(entry.LastOpenedAtUtc),
            lastValidationAtUtc = FormatDateTime(entry.LastValidationAtUtc),
            lastSimulationRunAtUtc = FormatDateTime(entry.LastSimulationRunAtUtc),
            lastExportAtUtc = FormatDateTime(entry.LastExportAtUtc),
            isMatchedLayerProject = string.Equals(entry.ProjectId, matchedProjectId, StringComparison.Ordinal)
        };
    }

    private static object SanitizeDiagnostics(OfflineCacheDiagnostics diagnostics)
    {
        return new
        {
            packageId = DiagnosticRedactor.RedactSensitiveText(diagnostics.PackageId),
            packageFileName = DiagnosticRedactor.RedactPath(diagnostics.PackageFileName),
            packageSizeBytes = diagnostics.PackageSizeBytes,
            lastSyncTimeUtc = FormatDateTime(diagnostics.LastSyncTime),
            localGeneration = diagnostics.LocalGeneration,
            serverGeneration = diagnostics.ServerGeneration,
            timestampUtc = FormatDateTime(diagnostics.Timestamp),
            metadataCache = new
            {
                status = diagnostics.MetadataCache.Status,
                sourceCount = diagnostics.MetadataCache.SourceCount,
                lastUpdatedUtc = FormatDateTime(diagnostics.MetadataCache.LastUpdatedUtc),
                sources = diagnostics.MetadataCache.Sources.Select(SanitizeSourceDiagnostics).ToList()
            },
            featureCache = new
            {
                status = diagnostics.FeatureCache.Status,
                sourceCount = diagnostics.FeatureCache.SourceCount,
                totalFeatureCount = diagnostics.FeatureCache.TotalFeatureCount,
                sizeBytes = diagnostics.FeatureCache.SizeBytes,
                sources = diagnostics.FeatureCache.Sources.Select(SanitizeSourceDiagnostics).ToList()
            },
            operations = new
            {
                pendingCount = diagnostics.Operations.PendingCount,
                claimedCount = diagnostics.Operations.ClaimedCount,
                succeededCount = diagnostics.Operations.SucceededCount,
                failedCount = diagnostics.Operations.FailedCount,
                retryCount = diagnostics.Operations.RetryCount,
                conflictCount = diagnostics.Operations.ConflictCount,
                attachmentPendingCount = diagnostics.Operations.AttachmentPendingCount,
                attachmentSucceededCount = diagnostics.Operations.AttachmentSucceededCount,
                attachmentFailedCount = diagnostics.Operations.AttachmentFailedCount,
                attachmentUploadFailedCount = diagnostics.Operations.AttachmentUploadFailedCount,
                attachmentDownloadFailedCount = diagnostics.Operations.AttachmentDownloadFailedCount,
                attachmentDeleteFailedCount = diagnostics.Operations.AttachmentDeleteFailedCount
            }
        };
    }

    private static object SanitizeSourceDiagnostics(OfflineSourceDiagnostics source)
    {
        return new
        {
            sourceId = source.SourceId,
            displayName = DiagnosticRedactor.RedactSensitiveText(source.DisplayName),
            featureCount = source.FeatureCount,
            lastSyncTimeUtc = FormatDateTime(source.LastSyncTime),
            sourceUrl = DiagnosticRedactor.RedactUrl(source.SourceUrl)
        };
    }

    private static object SanitizeConflictReviewItem(OfflineConflictReviewItem conflict)
    {
        return new
        {
            conflictId = conflict.ConflictId,
            operationId = conflict.OperationId,
            sourceId = conflict.SourceId,
            featureId = conflict.FeatureId,
            conflictType = conflict.ConflictType,
            status = conflict.Status,
            reason = DiagnosticRedactor.RedactSensitiveText(conflict.Reason),
            localState = DiagnosticRedactor.RedactSensitiveText(conflict.LocalState),
            serverState = DiagnosticRedactor.RedactSensitiveText(conflict.ServerState),
            detectedAtUtc = FormatDateTime(conflict.DetectedAtUtc),
            resolutionActions = conflict.ResolutionActions
        };
    }

    private static string? RedactImportSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return DiagnosticRedactor.RedactUrl(value);
        }

        return DiagnosticRedactor.RedactSensitiveText(DiagnosticRedactor.RedactPath(value));
    }

    private static FieldProjectCatalogEntry? FindMatchingCatalogEntry(
        IReadOnlyList<FieldProjectCatalogEntry> catalogEntries,
        LayerInfo layer)
    {
        if (catalogEntries.Count == 0)
        {
            return null;
        }

        var serviceId = layer.ServiceId?.Trim();
        if (!string.IsNullOrWhiteSpace(serviceId))
        {
            var exactMatch = catalogEntries.FirstOrDefault(entry =>
                string.Equals(entry.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.ProjectId, serviceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.PackageId, serviceId, StringComparison.OrdinalIgnoreCase));

            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        var sourceId = layer.SourceId?.Trim();
        if (!string.IsNullOrWhiteSpace(sourceId))
        {
            return catalogEntries.FirstOrDefault(entry =>
                !string.IsNullOrWhiteSpace(entry.ServiceId) &&
                sourceId.StartsWith($"{entry.ServiceId}/", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static object? SanitizeCaptureLocation(FieldLocationCaptureEvidence? captureLocation)
    {
        return captureLocation is null
            ? null
            : new
            {
                latitude = captureLocation.Latitude,
                longitude = captureLocation.Longitude,
                altitudeMeters = captureLocation.AltitudeMeters,
                horizontalAccuracyMeters = captureLocation.HorizontalAccuracyMeters,
                verticalAccuracyMeters = captureLocation.VerticalAccuracyMeters,
                speedMetersPerSecond = captureLocation.SpeedMetersPerSecond,
                headingDegrees = captureLocation.HeadingDegrees,
                capturedAtUtc = FormatDateTime(captureLocation.CapturedAtUtc.UtcDateTime),
                sourceKind = captureLocation.SourceKind.ToString(),
                provider = DiagnosticRedactor.RedactSensitiveText(captureLocation.Provider),
                isMockProvider = captureLocation.IsMockProvider,
                reducedAccuracy = captureLocation.ReducedAccuracy,
                receiver = captureLocation.Receiver is null
                    ? null
                    : new
                    {
                        name = DiagnosticRedactor.RedactSensitiveText(captureLocation.Receiver.Name),
                        manufacturer = DiagnosticRedactor.RedactSensitiveText(captureLocation.Receiver.Manufacturer),
                        model = DiagnosticRedactor.RedactSensitiveText(captureLocation.Receiver.Model),
                        firmwareVersion = DiagnosticRedactor.RedactSensitiveText(captureLocation.Receiver.FirmwareVersion),
                        serialNumber = DiagnosticRedactor.RedactSensitiveText(captureLocation.Receiver.SerialNumber),
                        isExternal = captureLocation.Receiver.IsExternal
                    }
            };
    }

    private static IReadOnlyList<ChangeRecord> GetPendingChanges(
        IReadOnlyDictionary<string, List<ChangeRecord>> pendingByFeature,
        string featureId)
    {
        return pendingByFeature.TryGetValue(featureId, out var changes)
            ? changes
            : [];
    }

    private static IReadOnlyList<AttachmentInfo> GetAttachments(
        IReadOnlyDictionary<string, IReadOnlyList<AttachmentInfo>> attachmentsByFeature,
        string featureId)
    {
        return attachmentsByFeature.TryGetValue(featureId, out var attachments)
            ? attachments
            : [];
    }

    private static string FormatPendingOperations(IReadOnlyList<ChangeRecord> pendingChanges)
    {
        return string.Join("|", pendingChanges
            .OrderBy(change => change.Timestamp)
            .Select(change => change.Operation.ToString())
            .Distinct(StringComparer.Ordinal));
    }

    private static DateTime? GetLatestPendingTimestamp(IReadOnlyList<ChangeRecord> pendingChanges)
    {
        return pendingChanges.Count == 0
            ? null
            : pendingChanges.Max(change => change.Timestamp);
    }

    private static bool IsPendingAttachment(AttachmentInfo attachment)
    {
        return attachment.SyncStatus is AttachmentSyncStatus.PendingUpload
            or AttachmentSyncStatus.UploadFailed
            or AttachmentSyncStatus.PendingDownload
            or AttachmentSyncStatus.DownloadFailed
            or AttachmentSyncStatus.PendingDelete
            or AttachmentSyncStatus.DeleteFailed;
    }

    private static Dictionary<string, object?> SanitizeAttributes(IReadOnlyDictionary<string, object?> attributes)
    {
        return attributes.ToDictionary(
            attribute => attribute.Key,
            attribute => SanitizeAttributeValue(attribute.Key, attribute.Value),
            StringComparer.Ordinal);
    }

    private static bool TryGetAttributeValue(
        IReadOnlyDictionary<string, object?> attributes,
        string attributeColumn,
        out object? value)
    {
        if (attributes.TryGetValue(attributeColumn, out value))
        {
            return true;
        }

        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Key, attributeColumn, StringComparison.OrdinalIgnoreCase))
            {
                value = attribute.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static object? SanitizeAttributeValue(string name, object? value)
    {
        return IsSensitiveName(name)
            ? Redacted
            : SanitizeValue(value);
    }

    private static object? SanitizeValue(object? value)
    {
        return value switch
        {
            null => null,
            string text => DiagnosticRedactor.RedactSensitiveText(text),
            JsonElement element => SanitizeJsonElement(element),
            IReadOnlyDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => SanitizeAttributeValue(pair.Key, pair.Value),
                StringComparer.Ordinal),
            IDictionary<string, object?> dictionary => dictionary.ToDictionary(
                pair => pair.Key,
                pair => SanitizeAttributeValue(pair.Key, pair.Value),
                StringComparer.Ordinal),
            IEnumerable<object?> values => values.Select(SanitizeValue).ToArray(),
            _ => value
        };
    }

    private static object? SanitizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => SanitizeAttributeValue(property.Name, property.Value),
                    StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(SanitizeJsonElement).ToArray(),
            JsonValueKind.String => DiagnosticRedactor.RedactSensitiveText(element.GetString()),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string FormatAttributeValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            DateTime dateTime => FormatDateTime(dateTime),
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            JsonElement element => FormatJsonElement(element),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => JsonSerializer.Serialize(value, JsonOptions)
        };
    }

    private static string FormatJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => DiagnosticRedactor.RedactSensitiveText(element.GetString()),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => JsonSerializer.Serialize(SanitizeJsonElement(element), JsonOptions)
        };
    }

    private static string EscapeCsv(string value)
    {
        return value.Contains('"', StringComparison.Ordinal) ||
            value.Contains(',', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string FormatDateTime(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
        {
            return string.Empty;
        }

        var dateTime = value.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value.Value.ToUniversalTime();

        return dateTime.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string ResolveExportRoot(string? exportRootDirectory)
    {
        if (!string.IsNullOrWhiteSpace(exportRootDirectory))
        {
            Directory.CreateDirectory(exportRootDirectory);
            return exportRootDirectory;
        }

        var documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documentsDirectory))
        {
            documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(documentsDirectory))
        {
            documentsDirectory = Path.GetTempPath();
        }

        var exportRoot = Path.Combine(documentsDirectory, "Honua", "RecordExports");
        Directory.CreateDirectory(exportRoot);
        return exportRoot;
    }

    private static string BuildExportDirectoryName(LayerInfo layer, DateTime exportedAtUtc)
    {
        var layerName = SanitizePathSegment(string.IsNullOrWhiteSpace(layer.Name) ? "layer" : layer.Name);
        return $"honua_records_layer_{layer.Id}_{layerName}_{exportedAtUtc:yyyyMMdd_HHmmss_fff}";
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray())
            .Trim();

        sanitized = string.Join("_", sanitized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "item";
        }

        return sanitized.Length > 64
            ? sanitized[..64]
            : sanitized;
    }

    private static string RedactFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var slashIndex = Math.Max(value.LastIndexOf('/'), value.LastIndexOf('\\'));
        var fileName = slashIndex >= 0 ? value[(slashIndex + 1)..] : value;
        return DiagnosticRedactor.RedactSensitiveText(fileName);
    }

    private static bool IsSensitiveName(string name)
    {
        var normalized = string.Concat(name
            .Where(char.IsLetterOrDigit))
            .ToLowerInvariant();

        return normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("apikey", StringComparison.Ordinal) ||
            normalized.Contains("accesskey", StringComparison.Ordinal) ||
            normalized.Contains("authorization", StringComparison.Ordinal);
    }

    private void TryDeletePartialExport(string exportDirectory)
    {
        try
        {
            if (Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
        catch (Exception cleanupException)
        {
            _logger?.LogDebug(cleanupException, "Failed to delete partial record export at {ExportDirectory}", exportDirectory);
        }
    }

    private sealed record AttachmentExportSummary(int AttachmentCount, int CopiedFileCount);
}
