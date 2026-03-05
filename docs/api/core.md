# Honua Mobile Core API Reference

The Honua Mobile Core library provides the fundamental gRPC client, authentication, and query capabilities for connecting to Honua servers.

## Overview

| Namespace | Purpose |
|-----------|---------|
| `Honua.Mobile.Core` | Main client interfaces and configuration |
| `Honua.Mobile.Core.Auth` | Authentication and security |
| `Honua.Mobile.Core.Queries` | Query builders and spatial operations |
| `Honua.Mobile.Core.Models` | Data models and domain objects |
| `Honua.Mobile.Core.Events` | Event handling and notifications |

---

## IHonuaClient

The main client interface for connecting to Honua servers.

### Interface Definition

```csharp
public interface IHonuaClient
{
    Task<bool> TestConnectionAsync();
    Task InitializeAsync();
    Task<QueryResult<Feature>> QueryFeaturesAsync(string serviceId, int layerId, FeatureQuery query);
    IAsyncEnumerable<Feature> QueryFeaturesStreamAsync(string serviceId, int layerId, FeatureQuery query);
    Task<EditResult> ApplyEditsAsync(string serviceId, int layerId, FeatureEditBatch edits);
    Task SavePendingChangesAsync();
    Task<ServerInfo> GetServerInfoAsync();
}
```

### Usage Examples

#### Basic Connectivity Test

```csharp
var client = serviceProvider.GetRequiredService<IHonuaClient>();

// Test if server is reachable
var isConnected = await client.TestConnectionAsync();
if (isConnected)
{
    Console.WriteLine("✅ Connected to Honua server");
}
else
{
    Console.WriteLine("❌ Server unreachable - working offline");
}
```

#### Query Features

```csharp
// Build a spatial query
var query = new FeatureQueryBuilder()
    .WithinDistance(latitude: 37.7749, longitude: -122.4194, distance: 1000) // 1km buffer
    .Where("status", "active")
    .Where("created_date", ">", DateTime.Now.AddDays(-30)) // Last 30 days
    .OrderBy("created_date", descending: true)
    .Take(100)
    .IncludeGeometry()
    .Build();

// Execute query
var result = await client.QueryFeaturesAsync("field_service", layerId: 1, query);

Console.WriteLine($"Found {result.Features.Count} features");
foreach (var feature in result.Features)
{
    Console.WriteLine($"Feature {feature.Id}: {feature.Attributes["name"]}");
}
```

#### Streaming Large Datasets

```csharp
var query = new FeatureQueryBuilder()
    .Where("category", "infrastructure")
    .IncludeGeometry()
    .Build();

// Stream features for large datasets (memory efficient)
await foreach (var feature in client.QueryFeaturesStreamAsync("utilities", 2, query))
{
    ProcessFeature(feature);

    // Process in chunks to avoid memory issues
    if (processedCount % 1000 == 0)
    {
        await Task.Delay(100); // Brief pause
        GC.Collect(); // Optional: force garbage collection
    }
}
```

#### Create and Update Features

```csharp
var editBatch = new FeatureEditBatch();

// Add new feature
var newFeature = new Feature
{
    Geometry = new Point(longitude: -122.4194, latitude: 37.7749),
    Attributes = new Dictionary<string, object>
    {
        ["name"] = "New Survey Point",
        ["status"] = "active",
        ["created_by"] = "field_user_123",
        ["created_date"] = DateTimeOffset.UtcNow
    }
};
editBatch.Adds.Add(newFeature);

// Update existing feature
var existingFeature = await GetFeatureByIdAsync("existing_feature_id");
existingFeature.Attributes["status"] = "completed";
existingFeature.Attributes["completed_date"] = DateTimeOffset.UtcNow;
editBatch.Updates.Add(existingFeature);

// Apply all changes in a single transaction
var result = await client.ApplyEditsAsync("field_service", 1, editBatch);

if (result.Success)
{
    Console.WriteLine($"✅ Applied {result.AddResults.Count} adds, {result.UpdateResults.Count} updates");
}
else
{
    Console.WriteLine($"❌ Edit failed: {result.ErrorMessage}");
}
```

---

## FeatureQueryBuilder

Fluent query builder for creating complex spatial and attribute queries.

### Basic Usage

```csharp
var query = new FeatureQueryBuilder()
    .Where("field_name", "value")
    .Build();
```

### Spatial Queries

```csharp
// Point buffer query
var bufferQuery = new FeatureQueryBuilder()
    .WithinDistance(lat: 37.7749, lon: -122.4194, distance: 500) // 500m radius
    .Build();

// Bounding box query
var bboxQuery = new FeatureQueryBuilder()
    .WithinBounds(
        minLat: 37.7000, minLon: -122.5000,
        maxLat: 37.8000, maxLon: -122.4000)
    .Build();

// Polygon intersection
var polygon = new Polygon(coordinates);
var intersectionQuery = new FeatureQueryBuilder()
    .Intersects(polygon)
    .Build();

// Multiple spatial relationships
var complexQuery = new FeatureQueryBuilder()
    .Within(boundaryPolygon)
    .NotWithin(exclusionPolygon)
    .Build();
```

### Attribute Queries

```csharp
// Simple equality
var equalQuery = new FeatureQueryBuilder()
    .Where("status", "active")
    .Build();

// Comparison operators
var rangeQuery = new FeatureQueryBuilder()
    .Where("temperature", ">", 20.0)
    .Where("temperature", "<=", 35.0)
    .Build();

// Text queries
var textQuery = new FeatureQueryBuilder()
    .Where("description", "LIKE", "%water%")
    .Where("name", "STARTS_WITH", "Site")
    .Build();

// Date ranges
var recentQuery = new FeatureQueryBuilder()
    .Where("created_date", ">=", DateTime.Now.AddDays(-7))
    .Where("created_date", "<", DateTime.Now)
    .Build();

// Multiple conditions with AND/OR
var complexQuery = new FeatureQueryBuilder()
    .Where("category", "utility")
    .And(builder => builder
        .Where("status", "active")
        .Or("priority", "high"))
    .Build();

// IN clause
var inQuery = new FeatureQueryBuilder()
    .Where("type", "IN", new[] { "pole", "transformer", "switch" })
    .Build();

// NULL checks
var nullQuery = new FeatureQueryBuilder()
    .Where("inspection_date", "IS NULL")
    .Where("notes", "IS NOT NULL")
    .Build();
```

### Result Control

```csharp
// Pagination
var pagedQuery = new FeatureQueryBuilder()
    .Where("status", "active")
    .Skip(100)
    .Take(50)
    .Build();

// Sorting
var sortedQuery = new FeatureQueryBuilder()
    .Where("category", "infrastructure")
    .OrderBy("created_date", descending: true)
    .ThenBy("name")
    .Build();

// Field selection
var fieldsQuery = new FeatureQueryBuilder()
    .Where("status", "active")
    .Select("id", "name", "status", "geometry")
    .IncludeGeometry() // Always include if you need spatial data
    .Build();

// Distinct values
var distinctQuery = new FeatureQueryBuilder()
    .Select("DISTINCT category")
    .OrderBy("category")
    .Build();
```

### Advanced Queries

```csharp
// Aggregation
var statsQuery = new FeatureQueryBuilder()
    .Where("category", "sensor_reading")
    .Where("timestamp", ">", DateTime.Now.AddHours(-24))
    .Select(
        "COUNT(*) as reading_count",
        "AVG(temperature) as avg_temp",
        "MIN(temperature) as min_temp",
        "MAX(temperature) as max_temp")
    .GroupBy("sensor_id")
    .Having("COUNT(*) > 10") // Sensors with more than 10 readings
    .Build();

// Subqueries
var subQuery = new FeatureQueryBuilder()
    .Where("status", "active")
    .Select("location_id")
    .Build();

var mainQuery = new FeatureQueryBuilder()
    .Where("location_id", "IN", subQuery)
    .Build();

// Spatial joins (query features near other features)
var nearbyQuery = new FeatureQueryBuilder()
    .WithinDistanceOf(
        targetLayer: "fire_hydrants",
        targetQuery: new FeatureQueryBuilder().Where("status", "active").Build(),
        distance: 100) // Features within 100m of active hydrants
    .Build();
```

---

## Authentication

### API Key Authentication

```csharp
// Configure in MauiProgram.cs
builder.Services.AddHonuaMobileSDK(config =>
{
    config.ApiKey = "your-api-key-here";
    config.ServerEndpoint = "https://your-server.com";
});

// Programmatic API key management
var authProvider = serviceProvider.GetRequiredService<IAuthenticationProvider>();

await authProvider.SetApiKeyAsync("new-api-key");
var currentKey = await authProvider.GetApiKeyAsync();
await authProvider.ClearCredentialsAsync();
```

### OIDC Authentication (Enterprise)

```csharp
// Configure OIDC
builder.Services.AddHonuaMobileSDK(config =>
{
    config.UseOIDC(oidc =>
    {
        oidc.Authority = "https://your-identity-server.com";
        oidc.ClientId = "mobile-app-client";
        oidc.Scope = "openid profile honua-api";
        oidc.RedirectUri = "com.yourcompany.app://callback";
    });
});

// Interactive login
var authService = serviceProvider.GetRequiredService<IOIDCAuthenticationService>();

try
{
    var result = await authService.LoginAsync();
    if (result.IsSuccess)
    {
        Console.WriteLine($"Logged in as: {result.User.Name}");
    }
}
catch (UserCancelledException)
{
    Console.WriteLine("User cancelled login");
}

// Silent token refresh
var token = await authService.GetAccessTokenSilentlyAsync();

// Logout
await authService.LogoutAsync();
```

---

## Configuration

### Basic Configuration

```csharp
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();

    builder.Services.AddHonuaMobileSDK(config =>
    {
        // Required settings
        config.ServerEndpoint = "https://your-honua-server.com";
        config.ApiKey = "your-api-key-here";

        // Optional settings
        config.EnableOfflineStorage = true;
        config.EnableLocationServices = true;
        config.EnableCameraIntegration = true;
        config.LogLevel = LogLevel.Information;
    });

    return builder.Build();
}
```

### Advanced Configuration

```csharp
builder.Services.AddHonuaMobileSDK(config =>
{
    // Server settings
    config.ServerEndpoint = "https://api.honua.com";
    config.ApiTimeout = TimeSpan.FromSeconds(30);
    config.MaxRetryAttempts = 3;
    config.RetryDelay = TimeSpan.FromSeconds(2);

    // Authentication
    config.ApiKey = "your-api-key";
    config.EnableTokenRefresh = true;
    config.TokenRefreshThreshold = TimeSpan.FromMinutes(5);

    // Offline storage
    config.EnableOfflineStorage = true;
    config.OfflineStoragePath = "custom_database.gpkg";
    config.MaxOfflineStorageSize = 500_000_000; // 500MB

    // Sync settings
    config.AutoSyncInterval = TimeSpan.FromMinutes(5);
    config.SyncConflictResolution = ConflictResolution.LastWriterWins;
    config.EnableBackgroundSync = true;
    config.MaxPendingEdits = 1000;

    // Performance
    config.MaxConcurrentRequests = 4;
    config.EnableRequestCompression = true;
    config.EnableResponseCaching = true;
    config.CacheExpiration = TimeSpan.FromHours(1);

    // Features
    config.EnableLocationServices = true;
    config.LocationAccuracyThreshold = 10.0; // meters
    config.EnableCameraIntegration = true;
    config.PhotoCompressionLevel = 0.8;
    config.MaxPhotoSizeBytes = 5_000_000; // 5MB

    // Logging
    config.LogLevel = LogLevel.Information;
    config.EnableTelemetry = false; // Privacy-first default
});
```

### Environment-Specific Configuration

```csharp
// Use configuration from appsettings.json
builder.Configuration.AddJsonFile("appsettings.json");

builder.Services.AddHonuaMobileSDK(config =>
{
    var honuaConfig = builder.Configuration.GetSection("Honua");
    config.ServerEndpoint = honuaConfig["ServerEndpoint"];
    config.ApiKey = honuaConfig["ApiKey"];

    // Environment-specific settings
    if (builder.Environment.IsDevelopment())
    {
        config.LogLevel = LogLevel.Debug;
        config.EnableTelemetry = false;
        config.ApiTimeout = TimeSpan.FromMinutes(5); // Longer timeout for debugging
    }
    else
    {
        config.LogLevel = LogLevel.Warning;
        config.EnableTelemetry = true;
        config.ApiTimeout = TimeSpan.FromSeconds(30);
    }
});
```

---

## Event Handling

### Feature Events

```csharp
public class DataCollectionPage : ContentPage
{
    private readonly IHonuaClient _client;

    public DataCollectionPage(IHonuaClient client)
    {
        _client = client;

        // Subscribe to events
        _client.FeatureCreated += OnFeatureCreated;
        _client.FeatureUpdated += OnFeatureUpdated;
        _client.FeatureDeleted += OnFeatureDeleted;
        _client.SyncCompleted += OnSyncCompleted;
        _client.SyncFailed += OnSyncFailed;
    }

    private async void OnFeatureCreated(object sender, FeatureEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            DisplayAlert("Feature Created",
                $"New feature created: {e.Feature.Id}", "OK");
        });
    }

    private void OnSyncCompleted(object sender, SyncEventArgs e)
    {
        Console.WriteLine($"Sync completed: ↓{e.Downloaded} ↑{e.Uploaded}");
    }
}
```

### Custom Event Handlers

```csharp
public class CustomEventHandler : IFeatureEventHandler
{
    private readonly ILogger<CustomEventHandler> _logger;

    public CustomEventHandler(ILogger<CustomEventHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleFeatureCreatedAsync(Feature feature)
    {
        _logger.LogInformation("Feature created: {FeatureId}", feature.Id);

        // Custom logic: send notification, update cache, etc.
        await SendNotificationAsync($"New feature: {feature.Id}");
    }

    public async Task HandleFeatureUpdatedAsync(Feature feature)
    {
        _logger.LogInformation("Feature updated: {FeatureId}", feature.Id);

        // Custom logic: validate changes, audit log, etc.
        await AuditLogAsync("FEATURE_UPDATED", feature.Id);
    }
}

// Register custom handler
builder.Services.AddScoped<IFeatureEventHandler, CustomEventHandler>();
```

---

## Error Handling

### Exception Types

```csharp
try
{
    await client.QueryFeaturesAsync("service", 1, query);
}
catch (HonuaConnectionException ex)
{
    // Network/server connectivity issues
    Console.WriteLine($"Connection failed: {ex.Message}");
    // Work offline or retry
}
catch (HonuaAuthenticationException ex)
{
    // Authentication/authorization failures
    Console.WriteLine($"Authentication failed: {ex.Message}");
    // Prompt for re-login
}
catch (HonuaValidationException ex)
{
    // Data validation errors
    Console.WriteLine($"Validation failed: {ex.ValidationErrors}");
    // Show user-friendly error messages
}
catch (HonuaQuotaExceededException ex)
{
    // Rate limiting or quota exceeded
    Console.WriteLine($"Quota exceeded: {ex.Message}");
    // Implement backoff strategy
}
catch (HonuaException ex)
{
    // General Honua-specific errors
    Console.WriteLine($"Honua error: {ex.Message}");
    // Generic error handling
}
```

### Retry Strategies

```csharp
public async Task<QueryResult<Feature>> QueryWithRetryAsync(
    string serviceId, int layerId, FeatureQuery query)
{
    const int maxRetries = 3;
    var retryDelay = TimeSpan.FromSeconds(1);

    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await _client.QueryFeaturesAsync(serviceId, layerId, query);
        }
        catch (HonuaConnectionException) when (attempt < maxRetries)
        {
            await Task.Delay(retryDelay);
            retryDelay = TimeSpan.FromMilliseconds(retryDelay.TotalMilliseconds * 2); // Exponential backoff
        }
    }

    throw new InvalidOperationException("Max retries exceeded");
}
```

---

## Performance Optimization

### Query Optimization

```csharp
// ✅ Good: Use spatial indexes
var optimizedQuery = new FeatureQueryBuilder()
    .WithinBounds(minLat, minLon, maxLat, maxLon) // Uses spatial index
    .Where("status", "active") // Uses attribute index
    .Select("id", "name", "geometry") // Only needed fields
    .Take(100) // Limit results
    .Build();

// ❌ Avoid: Full table scans
var slowQuery = new FeatureQueryBuilder()
    .Where("name", "LIKE", "%anything%") // Full text search without index
    .IncludeAllFields() // Downloads unnecessary data
    .Build();
```

### Memory Management

```csharp
// ✅ Use streaming for large datasets
await foreach (var feature in client.QueryFeaturesStreamAsync("service", 1, query))
{
    ProcessFeature(feature);

    // Process in chunks
    if (++processedCount % 1000 == 0)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}

// ❌ Avoid: Loading all features into memory
var allFeatures = await client.QueryFeaturesAsync("service", 1, query); // Could be millions!
```

### Caching Strategies

```csharp
public class CachedHonuaClient : IHonuaClient
{
    private readonly IHonuaClient _innerClient;
    private readonly IMemoryCache _cache;

    public async Task<QueryResult<Feature>> QueryFeaturesAsync(
        string serviceId, int layerId, FeatureQuery query)
    {
        var cacheKey = $"query:{serviceId}:{layerId}:{query.GetHashCode()}";

        if (_cache.TryGetValue(cacheKey, out QueryResult<Feature>? cached))
        {
            return cached!;
        }

        var result = await _innerClient.QueryFeaturesAsync(serviceId, layerId, query);

        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));

        return result;
    }
}
```

---

## Next Steps

- **[Storage API](storage.md)** - Offline storage and sync
- **[MAUI Controls](maui.md)** - Platform-specific UI components
- **[IoT Integration](iot.md)** - Sensor connectivity
- **[Getting Started Tutorial](../getting-started/tutorial.md)** - Build your first app
- **[Examples](../../examples/)** - Real-world implementations