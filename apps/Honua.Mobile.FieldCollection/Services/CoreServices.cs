using System.ComponentModel;

namespace Honua.Mobile.FieldCollection.Services;

// Basic service interfaces with mock implementations for the reference app

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
    Task<Feature?> GetFeatureAsync(int layerId, long featureId);
    Task<Feature> CreateFeatureAsync(int layerId, Feature feature);
    Task<Feature> UpdateFeatureAsync(int layerId, Feature feature);
    Task DeleteFeatureAsync(int layerId, long featureId);
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
    Task<IEnumerable<AttachmentInfo>> GetAttachmentsAsync(long featureId);
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

// Mock implementations
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
            // Return demo location for testing
            return new Location(37.7749, -122.4194); // San Francisco
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
            return new Location(37.7749, -122.4194);
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
    public async Task<IEnumerable<Feature>> GetFeaturesAsync(int layerId, Polygon? spatialFilter = null)
    {
        await Task.Delay(500); // Simulate network call

        // Return demo features
        return new[]
        {
            new Feature
            {
                Id = 1,
                LayerId = layerId,
                Geometry = new Point(37.7749, -122.4194),
                Attributes = new Dictionary<string, object>
                {
                    { "name", "Sample Point 1" },
                    { "description", "This is a demo feature" },
                    { "created_at", DateTime.UtcNow.AddDays(-1) }
                }
            },
            new Feature
            {
                Id = 2,
                LayerId = layerId,
                Geometry = new Point(37.7849, -122.4094),
                Attributes = new Dictionary<string, object>
                {
                    { "name", "Sample Point 2" },
                    { "description", "Another demo feature" },
                    { "created_at", DateTime.UtcNow.AddHours(-2) }
                }
            }
        };
    }

    public async Task<Feature?> GetFeatureAsync(int layerId, long featureId)
    {
        await Task.Delay(200);
        var features = await GetFeaturesAsync(layerId);
        return features.FirstOrDefault(f => f.Id == featureId);
    }

    public async Task<Feature> CreateFeatureAsync(int layerId, Feature feature)
    {
        await Task.Delay(300);
        feature.Id = Random.Shared.NextInt64(1000, 9999);
        feature.LayerId = layerId;
        return feature;
    }

    public async Task<Feature> UpdateFeatureAsync(int layerId, Feature feature)
    {
        await Task.Delay(300);
        return feature;
    }

    public async Task DeleteFeatureAsync(int layerId, long featureId)
    {
        await Task.Delay(200);
    }
}

public class FormService : IFormService
{
    public async Task<FormDefinition?> GetFormDefinitionAsync(int layerId)
    {
        await Task.Delay(100);

        return new FormDefinition
        {
            LayerId = layerId,
            Name = "Sample Form",
            Fields = new[]
            {
                new FieldDefinition { Name = "name", Type = "text", Label = "Name", Required = true },
                new FieldDefinition { Name = "description", Type = "textarea", Label = "Description", Required = false },
                new FieldDefinition { Name = "category", Type = "select", Label = "Category", Options = new[] { "Type A", "Type B", "Type C" } },
                new FieldDefinition { Name = "priority", Type = "number", Label = "Priority", Min = 1, Max = 10 }
            }
        };
    }

    public async Task<bool> ValidateFormAsync(FormData formData, FormDefinition definition)
    {
        await Task.Delay(50);

        // Basic validation
        foreach (var field in definition.Fields.Where(f => f.Required))
        {
            if (!formData.Values.TryGetValue(field.Name, out var value) ||
                value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return false;
            }
        }

        return true;
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
    public async Task<string> SaveAttachmentAsync(Stream fileStream, string fileName, string contentType)
    {
        await Task.Delay(500); // Simulate upload
        return Guid.NewGuid().ToString();
    }

    public async Task<Stream> GetAttachmentAsync(string attachmentId)
    {
        await Task.Delay(200);
        return new MemoryStream();
    }

    public async Task DeleteAttachmentAsync(string attachmentId)
    {
        await Task.Delay(100);
    }

    public async Task<IEnumerable<AttachmentInfo>> GetAttachmentsAsync(long featureId)
    {
        await Task.Delay(100);
        return Array.Empty<AttachmentInfo>();
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
    public ConnectionProfile ConnectionProfile => Connectivity.ConnectionProfiles.FirstOrDefault() ?? ConnectionProfile.Unknown;

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