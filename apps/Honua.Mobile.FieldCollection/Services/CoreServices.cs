using System.ComponentModel;
using Honua.Mobile.FieldCollection.Models;
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
    Task<IEnumerable<Feature>> GetFeaturesAsync(int layerId, Polygon? spatialFilter = null);
    Task<Feature?> GetFeatureAsync(int layerId, string featureId);
    Task<Feature> CreateFeatureAsync(int layerId, Feature feature);
    Task<Feature> UpdateFeatureAsync(int layerId, Feature feature);
    Task DeleteFeatureAsync(int layerId, string featureId);
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
    Task<Stream> GetAttachmentAsync(string attachmentId);
    Task DeleteAttachmentAsync(string attachmentId);
    Task<IEnumerable<AttachmentInfo>> GetAttachmentsAsync(string featureId);
}

public interface ISettingsService
{
    Task<T> GetSettingAsync<T>(string key, T defaultValue = default!);
    Task SetSettingAsync<T>(string key, T value);
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
    public Task<FormDefinition?> GetFormDefinitionAsync(int layerId)
    {
        return Task.FromResult<FormDefinition?>(null);
    }

    public Task<bool> ValidateFormAsync(FormData formData, FormDefinition definition)
    {
        foreach (var field in definition.Fields.Where(f => f.Required))
        {
            if (!formData.Values.TryGetValue(field.Name, out var value) ||
                value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    public async Task<FormData> CreateEmptyFormAsync(int layerId)
    {
        await Task.CompletedTask;

        return new FormData
        {
            LayerId = layerId,
            Values = new Dictionary<string, object>()
        };
    }
}

public class AttachmentService : IAttachmentService
{
    public Task<string> SaveAttachmentAsync(Stream fileStream, string fileName, string contentType)
    {
        throw new InvalidOperationException("Attachment storage is not configured.");
    }

    public Task<Stream> GetAttachmentAsync(string attachmentId)
    {
        throw new InvalidOperationException("Attachment storage is not configured.");
    }

    public Task DeleteAttachmentAsync(string attachmentId)
    {
        throw new InvalidOperationException("Attachment storage is not configured.");
    }

    public Task<IEnumerable<AttachmentInfo>> GetAttachmentsAsync(string featureId)
    {
        return Task.FromResult<IEnumerable<AttachmentInfo>>(Array.Empty<AttachmentInfo>());
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
