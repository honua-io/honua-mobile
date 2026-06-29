using System.ComponentModel;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Ai;
using Honua.Mobile.FieldCollection.Services.Forms;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Forms;
using Honua.Sdk.Field.Records;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Honua.Mobile.FieldCollection.Services;

// Basic service interfaces for the reference app

public interface ILocationService
{
    Task<Location?> GetCurrentLocationAsync();
    Task<FieldLocationFix?> GetCurrentLocationFixAsync(CancellationToken cancellationToken = default);
    Task<Location?> GetLastKnownLocationAsync();
    Task<FieldLocationFix?> GetLastKnownLocationFixAsync(CancellationToken cancellationToken = default);
    Task StartLocationTracking();
    Task StopLocationTracking();
    bool IsLocationEnabled { get; }
}

public interface IHighAccuracyLocationMetadataProvider
{
    ValueTask<FieldLocationCaptureMetadata?> GetMetadataAsync(
        Location location,
        CancellationToken cancellationToken = default);
}

public interface IFeatureService
{
    Task<IReadOnlyList<LayerInfo>> GetLayersAsync();
    Task<IEnumerable<Feature>> GetFeaturesAsync(int layerId, Polygon? spatialFilter = null);
    Task<Feature?> GetFeatureAsync(int layerId, string featureId);
    Task<Feature> CreateFeatureAsync(int layerId, Feature feature);
    Task<Feature> UpdateFeatureAsync(int layerId, Feature feature);
    Task DeleteFeatureAsync(int layerId, string featureId);
}

public interface IFieldCollectionMetadataService
{
    Task<IReadOnlyList<FieldProjectInfo>> GetProjectsAsync(bool refresh = false, CancellationToken cancellationToken = default);
    Task<FieldProjectInfo?> GetSelectedProjectAsync(CancellationToken cancellationToken = default);
    Task SelectProjectAsync(string serviceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LayerInfo>> GetLayersAsync(bool refresh = false, CancellationToken cancellationToken = default);
}

public interface IFormService
{
    Task<FormDefinition?> GetFormDefinitionAsync(int layerId);
    Task<bool> ValidateFormAsync(FormData formData, FormDefinition definition);
    Task<FormData> CreateEmptyFormAsync(int layerId);
}

public interface IAttachmentService
{
    Task<string> SaveAttachmentAsync(Stream fileStream, string fileName, string contentType);
    Task<AttachmentInfo> SaveAttachmentAsync(
        int layerId,
        string featureId,
        Stream fileStream,
        string fileName,
        string contentType,
        AttachmentPayloadKind payloadKind = AttachmentPayloadKind.File,
        string? description = null,
        FieldLocationCaptureEvidence? captureLocation = null,
        CancellationToken cancellationToken = default);
    Task<AttachmentInfo> SaveDownloadedAttachmentAsync(
        int layerId,
        string featureId,
        FeatureAttachmentInfo remoteAttachment,
        Stream content,
        CancellationToken cancellationToken = default);
    Task<Stream> GetAttachmentAsync(string attachmentId);
    Task DeleteAttachmentAsync(string attachmentId);
    Task<IEnumerable<AttachmentInfo>> GetAttachmentsAsync(string featureId);
    Task<IReadOnlyList<AttachmentInfo>> GetPendingAttachmentsAsync(CancellationToken cancellationToken = default);
    Task<bool> AttachmentContentExistsAsync(string attachmentId);
    Task UpdateAttachmentAiStateAsync(
        string attachmentId,
        MobileAiMediaState? state,
        CancellationToken cancellationToken = default);
}

public interface ISettingsService
{
    Task<T> GetSettingAsync<T>(string key, T defaultValue = default!);
    Task SetSettingAsync<T>(string key, T value);
    Task RemoveSettingAsync(string key);
    Task<bool> HasSettingAsync(string key);
}

public interface IConnectivityService : INotifyPropertyChanged
{
    bool IsConnected { get; }
    NetworkAccess NetworkAccess { get; }
    ConnectionProfile ConnectionProfile { get; }
    event EventHandler<bool> ConnectivityChanged;
}

// Platform-backed/default implementations
public class LocationService : ILocationService
{
    private static readonly GeolocationRequest HighAccuracyRequest = new(
        GeolocationAccuracy.Best,
        TimeSpan.FromSeconds(20));

    private readonly IHighAccuracyLocationMetadataProvider? _metadataProvider;
    private readonly ILogger<LocationService>? _logger;

    public LocationService()
    {
    }

    public LocationService(
        IHighAccuracyLocationMetadataProvider? metadataProvider = null,
        ILogger<LocationService>? logger = null)
    {
        _metadataProvider = metadataProvider;
        _logger = logger;
    }

    public bool IsLocationEnabled => true;

    public async Task<Location?> GetCurrentLocationAsync()
    {
        var fix = await GetCurrentLocationFixAsync().ConfigureAwait(false);
        return fix?.Location;
    }

    public async Task<FieldLocationFix?> GetCurrentLocationFixAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var location = await Geolocation.GetLocationAsync(HighAccuracyRequest, cancellationToken).ConfigureAwait(false);
            return await BuildLocationFixAsync(location, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _logger?.LogDebug("Current location acquisition failed.");
            return null;
        }
    }

    public async Task<Location?> GetLastKnownLocationAsync()
    {
        var fix = await GetLastKnownLocationFixAsync().ConfigureAwait(false);
        return fix?.Location;
    }

    public async Task<FieldLocationFix?> GetLastKnownLocationFixAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var location = await Geolocation.GetLastKnownLocationAsync().ConfigureAwait(false);
            return location == null
                ? await GetCurrentLocationFixAsync(cancellationToken).ConfigureAwait(false)
                : await BuildLocationFixAsync(location, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _logger?.LogDebug("Last known location acquisition failed.");
            return null;
        }
    }

    public async Task StartLocationTracking()
    {
        await Task.CompletedTask;
    }

    public async Task StopLocationTracking()
    {
        await Task.CompletedTask;
    }

    internal async Task<FieldLocationFix?> BuildLocationFixAsync(
        Location? location,
        CancellationToken cancellationToken)
    {
        if (location is null)
        {
            return null;
        }

        FieldLocationCaptureMetadata? metadata = null;
        if (_metadataProvider is not null)
        {
            try
            {
                metadata = await _metadataProvider.GetMetadataAsync(location, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                _logger?.LogDebug("High accuracy location metadata enrichment failed.");
            }
        }

        return FieldLocationMetadataMapper.FromMauiLocation(location, metadata);
    }
}

public class FormService : IFormService
{
    private readonly IFieldCollectionMetadataService? _metadataService;

    public FormService(IFieldCollectionMetadataService? metadataService = null)
    {
        _metadataService = metadataService;
    }

    public async Task<FormDefinition?> GetFormDefinitionAsync(int layerId)
    {
        if (_metadataService == null)
        {
            return null;
        }

        var layers = await _metadataService.GetLayersAsync().ConfigureAwait(false);
        return layers.FirstOrDefault(layer => layer.Id == layerId)?.Form;
    }

    public Task<bool> ValidateFormAsync(FormData formData, FormDefinition definition)
    {
        var validationDefinition = EnsureValidationRules(definition);
        formData.Values = MobileFormRuleRuntime.ApplyCalculatedValues(validationDefinition, formData.Values);

        formData.ValidationErrors.Clear();

        var nonRepeatDefinition = CreateNonRepeatDefinition(validationDefinition);
        if (nonRepeatDefinition.Sections.Count > 0)
        {
            var result = FormValidator.Validate(nonRepeatDefinition, formData.ToSdkFieldRecord(nonRepeatDefinition));
            foreach (var error in result.Errors)
            {
                formData.ValidationErrors[error.FieldId] = error.Message;
            }
        }

        foreach (var section in validationDefinition.Sections.Where(section => section.Repeatable))
        {
            var repeatDefinition = CreateRepeatEntryDefinition(validationDefinition, section);
            foreach (var repeatIndex in MobileFormRepeatKey.GetRepeatIndices(section, formData.Values))
            {
                var record = CreateRepeatEntryRecord(formData, section, repeatIndex, repeatDefinition.FormId);
                var result = FormValidator.Validate(repeatDefinition, record);
                foreach (var error in result.Errors)
                {
                    var errorKey = section.Fields.FirstOrDefault(field => string.Equals(field.FieldId, error.FieldId, StringComparison.Ordinal)) is { } errorField
                        ? MobileFormRepeatKey.ForField(section, repeatIndex, errorField)
                        : $"{section.SectionId}[{repeatIndex}].{error.FieldId}";
                    formData.ValidationErrors[errorKey] = error.Message;
                }
            }
        }

        return Task.FromResult(formData.ValidationErrors.Count == 0);
    }

    private static FormDefinition EnsureValidationRules(FormDefinition definition)
    {
        return new FormDefinition
        {
            FormId = definition.FormId,
            Name = definition.Name,
            Version = definition.Version,
            Description = definition.Description,
            Target = definition.Target,
            Sections = definition.Sections
                .Select(section => new FormSection
                {
                    SectionId = section.SectionId,
                    Label = section.Label,
                    Description = section.Description,
                    Repeatable = section.Repeatable,
                    Collapsible = section.Collapsible,
                    InitiallyCollapsed = section.InitiallyCollapsed,
                    Fields = section.Fields.Select(MobileFormRuleRuntime.CloneField).ToList()
                })
                .ToList(),
            Metadata = new Dictionary<string, string>(definition.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static FormDefinition CreateNonRepeatDefinition(FormDefinition definition)
    {
        return new FormDefinition
        {
            FormId = definition.FormId,
            Name = definition.Name,
            Version = definition.Version,
            Description = definition.Description,
            Target = definition.Target,
            Sections = definition.Sections
                .Where(section => !section.Repeatable)
                .Select(section => MobileFormRuleRuntime.CloneSection(section, repeatable: false))
                .ToList(),
            Metadata = new Dictionary<string, string>(definition.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static FormDefinition CreateRepeatEntryDefinition(FormDefinition definition, FormSection section)
    {
        return new FormDefinition
        {
            FormId = definition.FormId,
            Name = definition.Name,
            Version = definition.Version,
            Description = definition.Description,
            Target = definition.Target,
            Sections = [MobileFormRuleRuntime.CloneSection(section, repeatable: false)],
            Metadata = new Dictionary<string, string>(definition.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static FieldRecord CreateRepeatEntryRecord(
        FormData formData,
        FormSection section,
        int repeatIndex,
        string formId)
    {
        var values = MobileFormRuleRuntime.BuildRecordValuesForBinding(section, formData.Values, repeatIndex);
        var repeatMedia = formData.Media
            .Select(media => MobileFormRepeatKey.TryParse(media.FieldId, out var sectionId, out var mediaRepeatIndex, out var fieldId) &&
                mediaRepeatIndex == repeatIndex &&
                string.Equals(sectionId, section.SectionId, StringComparison.Ordinal)
                    ? new FieldMediaAttachment
                    {
                        AttachmentId = media.AttachmentId,
                        FieldId = fieldId,
                        MediaType = media.MediaType,
                        FileName = media.FileName,
                        ContentType = media.ContentType,
                        SizeBytes = media.SizeBytes,
                        CaptureLocation = media.CaptureLocation,
                        CapturedAtUtc = media.CapturedAtUtc,
                        RequiresFaceBlur = media.RequiresFaceBlur
                    }
                    : null)
            .Where(media => media != null)
            .Cast<FieldMediaAttachment>()
            .ToList();

        return new FieldRecord
        {
            RecordId = formData.FeatureId ?? string.Empty,
            FormId = formId,
            Values = values,
            Media = new System.Collections.ObjectModel.Collection<FieldMediaAttachment>(repeatMedia),
            Location = formData.Location,
            CreatedAtUtc = formData.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(DateTime.SpecifyKind(formData.CreatedAt, DateTimeKind.Utc))
        };
    }

    public async Task<FormData> CreateEmptyFormAsync(int layerId)
    {
        await Task.CompletedTask;

        return new FormData
        {
            LayerId = layerId,
            Values = new Dictionary<string, object?>()
        };
    }
}

public class AttachmentService : IAttachmentService
{
    private const long DefaultQuotaBytes = 250L * 1024L * 1024L;

    private readonly IGeoPackageStorageService? _storage;
    private readonly string _rootDirectory;
    private readonly long _quotaBytes;
    private readonly ILogger<AttachmentService>? _logger;

    public AttachmentService()
        : this(storage: null)
    {
    }

    public AttachmentService(
        IGeoPackageStorageService? storage,
        string? rootDirectory = null,
        long quotaBytes = DefaultQuotaBytes,
        ILogger<AttachmentService>? logger = null)
    {
        _storage = storage;
        _rootDirectory = rootDirectory ?? GetDefaultAttachmentRoot();
        _quotaBytes = quotaBytes;
        _logger = logger;
    }

    public async Task<string> SaveAttachmentAsync(Stream fileStream, string fileName, string contentType)
    {
        var attachment = await SaveAttachmentAsync(
            layerId: 0,
            featureId: string.Empty,
            fileStream,
            fileName,
            contentType,
            InferPayloadKind(fileName, contentType),
            captureLocation: null,
            cancellationToken: default).ConfigureAwait(false);
        return attachment.Id;
    }

    public async Task<AttachmentInfo> SaveAttachmentAsync(
        int layerId,
        string featureId,
        Stream fileStream,
        string fileName,
        string contentType,
        AttachmentPayloadKind payloadKind = AttachmentPayloadKind.File,
        string? description = null,
        FieldLocationCaptureEvidence? captureLocation = null,
        CancellationToken cancellationToken = default)
    {
        var storage = EnsureStorageConfigured();
        ArgumentNullException.ThrowIfNull(fileStream);
        cancellationToken.ThrowIfCancellationRequested();

        var attachmentId = Guid.NewGuid().ToString("N");
        var safeFileName = SanitizeFileName(fileName);
        var safeContentType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType.Trim();
        var kind = payloadKind == AttachmentPayloadKind.File
            ? InferPayloadKind(safeFileName, safeContentType)
            : payloadKind;
        var now = DateTime.UtcNow;
        var finalPath = BuildAttachmentPath(layerId, featureId, attachmentId, safeFileName);
        var tempPath = Path.Combine(_rootDirectory, ".tmp", $"{attachmentId}.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        var sizeBytes = await CopyToFileWithQuotaAsync(fileStream, tempPath, cancellationToken).ConfigureAwait(false);
        try
        {
            File.Move(tempPath, finalPath, overwrite: true);
            var attachment = new AttachmentInfo
            {
                Id = attachmentId,
                LayerId = layerId,
                FeatureId = featureId,
                FileName = safeFileName,
                ContentType = safeContentType,
                PayloadKind = kind,
                SizeBytes = sizeBytes,
                LocalPath = finalPath,
                CreatedAt = now,
                UpdatedAt = now,
                Description = description,
                CaptureLocation = captureLocation,
                SyncStatus = AttachmentSyncStatus.PendingUpload
            };

            await storage.StoreAttachmentMetadataAsync(attachment).ConfigureAwait(false);
            return attachment;
        }
        catch
        {
            DeleteFileIfExists(tempPath);
            DeleteFileIfExists(finalPath);
            throw;
        }
    }

    public async Task<AttachmentInfo> SaveDownloadedAttachmentAsync(
        int layerId,
        string featureId,
        FeatureAttachmentInfo remoteAttachment,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var storage = EnsureStorageConfigured();
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var remoteAttachmentId = remoteAttachment.AttachmentId.GetValueOrDefault();
        var existing = remoteAttachmentId > 0
            ? await storage.GetAttachmentByRemoteIdAsync(layerId, featureId, remoteAttachmentId).ConfigureAwait(false)
            : null;
        var attachmentId = existing?.Id ?? Guid.NewGuid().ToString("N");
        var fileName = SanitizeFileName(remoteAttachment.Name ?? "attachment.bin");
        var contentType = string.IsNullOrWhiteSpace(remoteAttachment.ContentType)
            ? "application/octet-stream"
            : remoteAttachment.ContentType;
        var finalPath = BuildAttachmentPath(layerId, featureId, attachmentId, fileName);
        var tempPath = Path.Combine(_rootDirectory, ".tmp", $"{attachmentId}.download");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        var sizeBytes = await CopyToFileWithQuotaAsync(content, tempPath, cancellationToken).ConfigureAwait(false);
        try
        {
            File.Move(tempPath, finalPath, overwrite: true);
            var now = DateTime.UtcNow;
            var attachment = new AttachmentInfo
            {
                Id = attachmentId,
                LayerId = layerId,
                FeatureId = featureId,
                RemoteAttachmentId = remoteAttachmentId <= 0 ? null : remoteAttachmentId,
                RemoteGlobalId = remoteAttachment.GlobalId,
                FileName = fileName,
                ContentType = contentType,
                PayloadKind = InferPayloadKind(fileName, contentType),
                SizeBytes = remoteAttachment.Size.GetValueOrDefault() > 0
                    ? remoteAttachment.Size.GetValueOrDefault()
                    : sizeBytes,
                LocalPath = finalPath,
                CreatedAt = existing?.CreatedAt == default ? now : existing?.CreatedAt ?? now,
                UpdatedAt = now,
                UploadedAt = now,
                LastSyncedAt = now,
                Description = remoteAttachment.Keywords,
                ThumbnailUrl = remoteAttachment.Url?.ToString(),
                SyncStatus = AttachmentSyncStatus.Synced,
                RetryCount = 0,
                LastError = null,
                IsDeleted = false,
                DeletedAt = null
            };

            await storage.StoreAttachmentMetadataAsync(attachment).ConfigureAwait(false);
            return attachment;
        }
        catch
        {
            DeleteFileIfExists(tempPath);
            DeleteFileIfExists(finalPath);
            throw;
        }
    }

    public async Task<Stream> GetAttachmentAsync(string attachmentId)
    {
        var storage = EnsureStorageConfigured();
        var attachment = await storage.GetAttachmentMetadataAsync(attachmentId).ConfigureAwait(false)
            ?? throw new FileNotFoundException("Attachment metadata was not found.", attachmentId);
        if (attachment.IsDeleted || string.IsNullOrWhiteSpace(attachment.LocalPath) || !File.Exists(attachment.LocalPath))
        {
            throw new FileNotFoundException("Attachment content was not found.", attachment.LocalPath);
        }

        return File.OpenRead(attachment.LocalPath);
    }

    public async Task DeleteAttachmentAsync(string attachmentId)
    {
        var storage = EnsureStorageConfigured();
        var attachment = await storage.GetAttachmentMetadataAsync(attachmentId).ConfigureAwait(false);
        if (attachment == null)
        {
            return;
        }

        DeleteFileIfExists(attachment.LocalPath);
        if (attachment.RemoteAttachmentId.HasValue)
        {
            await storage.MarkAttachmentPendingDeleteAsync(attachmentId).ConfigureAwait(false);
        }
        else
        {
            await storage.MarkAttachmentDeletedSyncedAsync(attachmentId).ConfigureAwait(false);
        }
    }

    public async Task<IEnumerable<AttachmentInfo>> GetAttachmentsAsync(string featureId)
    {
        var storage = EnsureStorageConfigured();
        return await storage.GetAttachmentsForFeatureAsync(featureId).ConfigureAwait(false);
    }

    public async Task UpdateAttachmentAiStateAsync(
        string attachmentId,
        MobileAiMediaState? state,
        CancellationToken cancellationToken = default)
    {
        var storage = EnsureStorageConfigured();
        cancellationToken.ThrowIfCancellationRequested();
        await storage.UpdateAttachmentAiStateAsync(attachmentId, state).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AttachmentInfo>> GetPendingAttachmentsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var storage = EnsureStorageConfigured();
        return await storage.GetPendingAttachmentChangesAsync().ConfigureAwait(false);
    }

    public async Task<bool> AttachmentContentExistsAsync(string attachmentId)
    {
        var storage = EnsureStorageConfigured();
        var attachment = await storage.GetAttachmentMetadataAsync(attachmentId).ConfigureAwait(false);
        return attachment is { IsDeleted: false } &&
            !string.IsNullOrWhiteSpace(attachment.LocalPath) &&
            File.Exists(attachment.LocalPath);
    }

    private IGeoPackageStorageService EnsureStorageConfigured()
    {
        return _storage ?? throw new InvalidOperationException("Attachment storage is not configured.");
    }

    private string BuildAttachmentPath(int layerId, string featureId, string attachmentId, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".bin";
        }

        return Path.Combine(
            _rootDirectory,
            "features",
            SanitizePathSegment(layerId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            SanitizePathSegment(string.IsNullOrWhiteSpace(featureId) ? "unassigned" : featureId),
            $"{attachmentId}{extension}");
    }

    private async Task<long> CopyToFileWithQuotaAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var existingBytes = GetDirectorySizeBytes(_rootDirectory);
        var writtenBytes = 0L;
        try
        {
            await using var output = File.Create(destinationPath);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                writtenBytes += read;
                if (_quotaBytes > 0 && existingBytes + writtenBytes > _quotaBytes)
                {
                    _logger?.LogWarning(
                        "Attachment quota exceeded while writing {DestinationPath}; quota {QuotaBytes} bytes",
                        destinationPath,
                        _quotaBytes);
                    throw new InvalidOperationException("Attachment storage quota exceeded.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            DeleteFileIfExists(destinationPath);
            throw;
        }

        return writtenBytes;
    }

    private static long GetDirectorySizeBytes(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(path => new FileInfo(path).Length);
    }

    private static string GetDefaultAttachmentRoot()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "Honua", "FieldCollection", "attachments");
    }

    private static string SanitizeFileName(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "attachment.bin";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        return safe;
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "blank";
        }

        var safe = value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct())
        {
            safe = safe.Replace(invalid, '_');
        }

        return safe.Replace(Path.DirectorySeparatorChar, '_').Replace(Path.AltDirectorySeparatorChar, '_');
    }

    private static AttachmentPayloadKind InferPayloadKind(string fileName, string contentType)
    {
        if (fileName.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
            contentType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentPayloadKind.Signature;
        }

        if (fileName.Contains("sketch", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("markup", StringComparison.OrdinalIgnoreCase) ||
            contentType.Equals("application/vnd.honua.sketch+json", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentPayloadKind.Sketch;
        }

        if (fileName.Contains("barcode", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("qr", StringComparison.OrdinalIgnoreCase) ||
            contentType.Equals("application/vnd.honua.barcode+json", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentPayloadKind.Barcode;
        }

        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentPayloadKind.Video;
        }

        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentPayloadKind.Audio;
        }

        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return AttachmentPayloadKind.Photo;
        }

        return AttachmentPayloadKind.File;
    }

    private static void DeleteFileIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Local cleanup is best effort; sync state keeps the operation retryable.
        }
    }
}

public class SettingsService : ISettingsService
{
    public async Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
    {
        var value = await SecureStorage.GetAsync(key);
        if (string.IsNullOrEmpty(value))
            return defaultValue;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(value) ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    public async Task SetSettingAsync<T>(string key, T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        await SecureStorage.SetAsync(key, json);
    }

    public Task RemoveSettingAsync(string key)
    {
        SecureStorage.Remove(key);
        return Task.CompletedTask;
    }

    public async Task<bool> HasSettingAsync(string key)
    {
        var value = await SecureStorage.GetAsync(key);
        return !string.IsNullOrEmpty(value);
    }
}

public class ConnectivityService : IConnectivityService
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<bool>? ConnectivityChanged;

    public bool IsConnected => Connectivity.NetworkAccess == NetworkAccess.Internet;
    public NetworkAccess NetworkAccess => Connectivity.NetworkAccess;
    public ConnectionProfile ConnectionProfile => Connectivity.ConnectionProfiles
        .DefaultIfEmpty(ConnectionProfile.Unknown)
        .First();

    public ConnectivityService()
    {
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsConnected)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NetworkAccess)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectionProfile)));
        ConnectivityChanged?.Invoke(this, IsConnected);
    }
}
