using System.ComponentModel;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Field.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;

namespace Honua.Mobile.FieldCollection.Services;

// Basic service interfaces for the reference app

public interface ILocationService
{
    Task<Location?> GetCurrentLocationAsync();
    Task<Location?> GetLastKnownLocationAsync();
    Task StartLocationTracking();
    Task StopLocationTracking();
    bool IsLocationEnabled { get; }
}

public interface IStorageService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value);
    Task RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);
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
    public bool IsLocationEnabled => true;

    public async Task<Location?> GetCurrentLocationAsync()
    {
        try
        {
            var location = await Geolocation.GetLocationAsync();
            return location;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Location?> GetLastKnownLocationAsync()
    {
        try
        {
            var location = await Geolocation.GetLastKnownLocationAsync();
            return location ?? await GetCurrentLocationAsync();
        }
        catch
        {
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
}

public class StorageService : IStorageService
{
    private readonly Dictionary<string, object> _storage = new();

    public async Task<T?> GetAsync<T>(string key)
    {
        await Task.CompletedTask;
        return _storage.TryGetValue(key, out var value) && value is T ? (T)value : default;
    }

    public async Task SetAsync<T>(string key, T value)
    {
        await Task.CompletedTask;
        if (value != null)
            _storage[key] = value;
        else
            _storage.Remove(key);
    }

    public async Task RemoveAsync(string key)
    {
        await Task.CompletedTask;
        _storage.Remove(key);
    }

    public async Task<bool> ExistsAsync(string key)
    {
        await Task.CompletedTask;
        return _storage.ContainsKey(key);
    }
}

public class FeatureService : IFeatureService
{
    public Task<IReadOnlyList<LayerInfo>> GetLayersAsync()
    {
        return Task.FromResult<IReadOnlyList<LayerInfo>>(Array.Empty<LayerInfo>());
    }

    public Task<IEnumerable<Feature>> GetFeaturesAsync(int layerId, Polygon? spatialFilter = null)
    {
        return Task.FromResult<IEnumerable<Feature>>(Array.Empty<Feature>());
    }

    public Task<Feature?> GetFeatureAsync(int layerId, string featureId)
    {
        return Task.FromResult<Feature?>(null);
    }

    public Task<Feature> CreateFeatureAsync(int layerId, Feature feature)
    {
        throw new InvalidOperationException("Feature storage is not configured.");
    }

    public Task<Feature> UpdateFeatureAsync(int layerId, Feature feature)
    {
        throw new InvalidOperationException("Feature storage is not configured.");
    }

    public Task DeleteFeatureAsync(int layerId, string featureId)
    {
        throw new InvalidOperationException("Feature storage is not configured.");
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
        var result = FormValidator.Validate(validationDefinition, formData.ToSdkFieldRecord(validationDefinition));

        formData.ValidationErrors.Clear();
        foreach (var error in result.Errors)
        {
            formData.ValidationErrors[error.FieldId] = error.Message;
        }

        return Task.FromResult(result.IsValid);
    }

    private static FormDefinition EnsureValidationRules(FormDefinition definition)
    {
        return new FormDefinition
        {
            FormId = definition.FormId,
            Name = definition.Name,
            Description = definition.Description,
            Target = definition.Target,
            Sections = definition.Sections
                .Select(section => new FormSection
                {
                    SectionId = section.SectionId,
                    Label = section.Label,
                    Fields = section.Fields.Select(CloneField).ToList()
                })
                .ToList(),
            Metadata = new Dictionary<string, string>(definition.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static FormField CloneField(FormField field)
    {
        return new FormField
        {
            FieldId = field.FieldId,
            Label = field.Label,
            Type = field.Type,
            SourceFieldName = field.SourceFieldName,
            Required = field.Required,
            Choices = field.Choices,
            Validation = field.Validation ?? new FieldValidationRule(),
            VisibilityRule = field.VisibilityRule,
            CalculatedExpression = field.CalculatedExpression,
            HelpText = field.HelpText
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

    private readonly GeoPackageStorageService? _storage;
    private readonly string _rootDirectory;
    private readonly long _quotaBytes;
    private readonly ILogger<AttachmentService>? _logger;

    public AttachmentService()
        : this(storage: null)
    {
    }

    public AttachmentService(
        GeoPackageStorageService? storage,
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

    private GeoPackageStorageService EnsureStorageConfigured()
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

        return contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? AttachmentPayloadKind.Photo
            : AttachmentPayloadKind.File;
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
