# Performance Optimization Guide

This guide covers performance optimization techniques for mobile applications using the Honua Mobile SDK.

## Overview

Mobile performance is critical for field data collection applications. Users expect responsive interfaces, efficient battery usage, and fast data operations even with large datasets.

## Core Performance Principles

1. **Lazy Loading**: Load data only when needed
2. **Efficient Caching**: Cache frequently accessed data
3. **Background Processing**: Keep UI responsive
4. **Memory Management**: Avoid memory leaks and excessive allocation
5. **Network Optimization**: Minimize data transfer

## Data Query Optimization

### Efficient Query Design

Use specific queries instead of loading all data:

```csharp
// ❌ Bad: Load every feature and field
using var allFeatures = await _client.QueryFeaturesAsync(new QueryFeaturesRequest
{
    ServiceId = "service-id",
    LayerId = 0,
    Where = "1=1",
});

// ✅ Good: Request only the rows and fields you need
using var visibleFeatures = await _client.QueryFeaturesAsync(new QueryFeaturesRequest
{
    ServiceId = "service-id",
    LayerId = 0,
    Where = "status = 'pending'",
    OutFields = new[] { "objectid", "name", "status" }, // only needed fields
    ResultRecordCount = 50,                             // limit results
});
```

`QueryFeaturesAsync` takes a `QueryFeaturesRequest` (carrying `ServiceId`/`LayerId`)
and returns a `JsonDocument` holding an Esri-style FeatureSet — read rows from
`features[].attributes` / `features[].geometry` and dispose the document with
`using`. The legacy query surface filters by `Where`, `ObjectIds`, and paging
options; for provider-neutral or spatial queries use
`QueryAsync(FeatureQueryRequest)`.

### Pagination Implementation

Implement efficient pagination for large datasets:

```csharp
public class PaginatedDataLoader : ObservableObject
{
    private const int PageSize = 50;
    private readonly ObservableCollection<Feature> _features = new();
    private int _currentPage = 0;
    private bool _hasMoreData = true;

    [ObservableProperty]
    private bool isLoading;

    public ObservableCollection<Feature> Features => _features;

    [RelayCommand]
    public async Task LoadNextPageAsync()
    {
        if (IsLoading || !_hasMoreData) return;

        IsLoading = true;

        try
        {
            var request = new QueryFeaturesRequest
            {
                ServiceId = "service-id",
                LayerId = 0,
                Where = "status = 'active'",
                ResultOffset = _currentPage * PageSize,
                ResultRecordCount = PageSize,
                OutFields = new[] { "objectid", "name", "status" },
                ReturnGeometry = true,
            };

            using var response = await _client.QueryFeaturesAsync(request);
            var page = response.RootElement.GetProperty("features");

            foreach (var element in page.EnumerateArray())
            {
                _features.Add(MapFeature(element)); // map the FeatureSet entry to your model
            }

            _hasMoreData = page.GetArrayLength() == PageSize;
            _currentPage++;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

### Spatial Indexing

Use spatial queries for map-based applications:

```csharp
public async Task<List<JsonElement>> LoadVisibleFeaturesAsync(string extentWhereClause)
{
    using var response = await _client.QueryFeaturesAsync(new QueryFeaturesRequest
    {
        ServiceId = "service-id",
        LayerId = 0,
        Where = extentWhereClause,
        OutFields = new[] { "objectid", "name", "status" },
        ReturnGeometry = true,
    });

    return response.RootElement.GetProperty("features").EnumerateArray()
        .Select(feature => feature.Clone())
        .ToList();
}
```

The legacy `QueryFeaturesRequest` surface filters by `Where`/`ObjectIds`;
bounding-box/geometry filtering and geometry generalization are applied
server-side. For client-driven spatial predicates use the provider-neutral
`QueryAsync(FeatureQueryRequest)` contract instead.

## Memory Management

### Proper Disposal

Always dispose of resources properly:

```csharp
public class DataService : IDisposable
{
    private readonly HonuaMobileClient _client;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public DataService(HonuaMobileClient client)
    {
        _client = client;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public async Task<JsonElement?> LoadFeatureAsync(long objectId)
    {
        using var response = await _client.QueryFeaturesAsync(
            new QueryFeaturesRequest
            {
                ServiceId = "service-id",
                LayerId = 0,
                ObjectIds = new[] { objectId },
                OutFields = new[] { "*" },
            },
            _cancellationTokenSource.Token);

        var features = response.RootElement.GetProperty("features");
        return features.GetArrayLength() > 0 ? features[0].Clone() : null;
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
    }
}
```

### Image Memory Management

Handle images efficiently:

```csharp
public class ImageProcessor
{
    public async Task<byte[]> ProcessImageAsync(FileResult imageFile)
    {
        using var sourceStream = await imageFile.OpenReadAsync();

        // Load with memory constraints
        var options = new ImageLoadOptions
        {
            MaxWidth = 1920,
            MaxHeight = 1080,
            Quality = 85
        };

        using var image = await Image.LoadAsync(sourceStream, options);

        // Process in-place to avoid additional allocations
        image.Mutate(x => x
            .Resize(options.MaxWidth, options.MaxHeight, KnownResamplers.Lanczos3)
            .AutoOrient());

        using var outputStream = new MemoryStream();
        await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = options.Quality });

        return outputStream.ToArray();
    }
}
```

### Collection Management

Use efficient collections and avoid unnecessary allocations:

```csharp
public class FeatureCollection : ObservableObject
{
    private readonly Dictionary<int, Feature> _featureIndex = new();
    private readonly ObservableCollection<Feature> _features = new();

    public IReadOnlyCollection<Feature> Features => _features;

    public void AddFeature(Feature feature)
    {
        if (_featureIndex.ContainsKey(feature.Id))
        {
            // Update existing feature instead of adding duplicate
            var index = _features.IndexOf(_featureIndex[feature.Id]);
            _features[index] = feature;
            _featureIndex[feature.Id] = feature;
        }
        else
        {
            _features.Add(feature);
            _featureIndex[feature.Id] = feature;
        }
    }

    public Feature? GetFeature(int id)
    {
        return _featureIndex.TryGetValue(id, out var feature) ? feature : null;
    }
}
```

## UI Performance

### Virtual Scrolling

Implement virtual scrolling for large lists:

```xml
<CollectionView ItemsSource="{Binding Features}"
                ItemSizingStrategy="MeasureAllItems"
                RemainingItemsThreshold="10"
                RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}">

    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:Feature">
            <Grid HeightRequest="60">
                <Label Text="{Binding Name}" />
                <Label Text="{Binding Status}" Grid.Column="1" />
            </Grid>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

### Efficient Data Binding

Use compiled bindings for better performance:

```xml
<ContentPage x:Class="App.Views.FeaturePage"
             x:DataType="vm:FeatureViewModel">

    <StackLayout>
        <Label Text="{Binding Feature.Name}" />
        <Label Text="{Binding Feature.Status}" />
    </StackLayout>

</ContentPage>
```

### Image Optimization

Optimize image display:

```csharp
public class OptimizedImageView : ContentView
{
    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(OptimizedImageView),
            propertyChanged: OnSourceChanged);

    public string Source
    {
        get => (string)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    private static async void OnSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is OptimizedImageView view && newValue is string source)
        {
            await view.LoadImageAsync(source);
        }
    }

    private async Task LoadImageAsync(string source)
    {
        // Load thumbnail for list views, full image for detail views
        var imageSize = GetOptimalImageSize();
        var cachedImage = await ImageCache.GetResizedImageAsync(source, imageSize);

        Content = new Image { Source = ImageSource.FromStream(() => cachedImage) };
    }

    private Size GetOptimalImageSize()
    {
        // Calculate optimal size based on current view size
        var density = DeviceDisplay.MainDisplayInfo.Density;
        return new Size(Width * density, Height * density);
    }
}
```

## Background Processing

### Task Parallelization

Use parallel processing for independent operations:

```csharp
public async Task ProcessMultipleFeaturesAsync(IEnumerable<Feature> features)
{
    var semaphore = new SemaphoreSlim(Environment.ProcessorCount);
    var tasks = features.Select(async feature =>
    {
        await semaphore.WaitAsync();
        try
        {
            return await ProcessSingleFeatureAsync(feature);
        }
        finally
        {
            semaphore.Release();
        }
    });

    var results = await Task.WhenAll(tasks);

    // Update UI on main thread
    MainThread.BeginInvokeOnMainThread(() =>
    {
        UpdateUI(results);
    });
}
```

### Background Sync Optimization

Implement efficient background synchronization:

```csharp
// In production prefer the registered BackgroundSyncOrchestrator (connectivity-aware,
// timer + semaphore gated). This manual timer is shown only to illustrate WiFi gating;
// it drives the same IOfflineSyncRunner.SyncAsync() drain cycle.
public class BackgroundSyncService
{
    private readonly IOfflineSyncRunner _syncRunner;
    private readonly Timer _syncTimer;

    public BackgroundSyncService(IOfflineSyncRunner syncRunner)
    {
        _syncRunner = syncRunner;
        _syncTimer = new Timer(SyncCallback, null, TimeSpan.Zero, TimeSpan.FromMinutes(15));
    }

    private async void SyncCallback(object? state)
    {
        if (!NetworkHelper.IsConnectedToWifi()) return; // Respect data usage

        try
        {
            // The offline sync engine drains queued edits and reports the run outcome.
            var result = await _syncRunner.SyncAsync();
            Debug.WriteLine($"Background sync: {result.Succeeded} succeeded, {result.Failed} failed.");
        }
        catch (Exception ex)
        {
            // Log error, don't crash background task
            Debug.WriteLine($"Background sync failed: {ex.Message}");
        }
    }
}
```

## Network Optimization

### Request Batching

Batch multiple requests when possible:

```csharp
public async Task<List<JsonElement>> LoadMultipleFeaturesAsync(IEnumerable<long> featureIds)
{
    var results = new List<JsonElement>();

    // Batch IDs into reasonable chunks using the objectId filter.
    foreach (var batch in featureIds.Chunk(50))
    {
        using var response = await _client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = "service-id",
            LayerId = 0,
            ObjectIds = batch,
            OutFields = new[] { "*" },
        });

        foreach (var feature in response.RootElement.GetProperty("features").EnumerateArray())
        {
            results.Add(feature.Clone());
        }
    }

    return results;
}
```

### Compression

Use compression for large data transfers:

```csharp
public class CompressedApiClient
{
    private readonly HttpClient _httpClient;

    public CompressedApiClient()
    {
        var handler = new HttpClientHandler();
        if (handler.SupportsAutomaticDecompression)
        {
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        }

        _httpClient = new HttpClient(handler);
        _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(
            new StringWithQualityHeaderValue("gzip"));
    }
}
```

### Caching Strategy

Implement intelligent caching:

```csharp
public class FeatureCache
{
    private readonly MemoryCache _memoryCache = new();
    private readonly string _diskCachePath;

    public async Task<Feature?> GetFeatureAsync(int featureId)
    {
        // Check memory cache first
        if (_memoryCache.TryGetValue(featureId, out Feature cachedFeature))
        {
            return cachedFeature;
        }

        // Check disk cache
        var diskFeature = await LoadFromDiskAsync(featureId);
        if (diskFeature != null)
        {
            // Add to memory cache
            _memoryCache.Set(featureId, diskFeature, TimeSpan.FromMinutes(30));
            return diskFeature;
        }

        // Load from network
        var networkFeature = await LoadFromNetworkAsync(featureId);
        if (networkFeature != null)
        {
            // Cache in both memory and disk
            _memoryCache.Set(featureId, networkFeature, TimeSpan.FromMinutes(30));
            await SaveToDiskAsync(networkFeature);
        }

        return networkFeature;
    }
}
```

## Battery Optimization

### Reduce GPS Usage

Optimize location services:

```csharp
public class BatteryEfficientLocationService
{
    private readonly ILocationService _locationService;
    private Location? _lastKnownLocation;
    private DateTime _lastLocationUpdate;

    public async Task<Location?> GetCurrentLocationAsync()
    {
        // Use cached location if recent enough
        if (_lastKnownLocation != null &&
            DateTime.UtcNow - _lastLocationUpdate < TimeSpan.FromMinutes(5))
        {
            return _lastKnownLocation;
        }

        var request = new GeolocationRequest
        {
            DesiredAccuracy = GeolocationAccuracy.High,
            Timeout = TimeSpan.FromSeconds(10) // Short timeout to save battery
        };

        _lastKnownLocation = await Geolocation.GetLocationAsync(request);
        _lastLocationUpdate = DateTime.UtcNow;

        return _lastKnownLocation;
    }
}
```

### Smart Sync Scheduling

Schedule sync operations efficiently:

```csharp
public class BatteryAwareSyncScheduler
{
    public async Task ScheduleSyncAsync()
    {
        var battery = Battery.Default;
        var connectivity = Connectivity.Current;

        // Only sync on WiFi when battery is low
        if (battery.ChargeLevel < 0.2 && connectivity.NetworkAccess == NetworkAccess.Internet)
        {
            if (connectivity.ConnectionProfiles.Contains(ConnectionProfile.WiFi))
            {
                await PerformLightweightSyncAsync();
            }
            return;
        }

        // Full sync when charging or battery above 50%
        if (battery.PowerSource == BatteryPowerSource.AC || battery.ChargeLevel > 0.5)
        {
            await PerformFullSyncAsync();
        }
    }
}
```

## Performance Monitoring

### Metrics Collection

Implement performance monitoring:

```csharp
public class PerformanceMonitor
{
    private readonly Dictionary<string, List<TimeSpan>> _operationTimes = new();

    public async Task<T> MeasureOperationAsync<T>(string operationName, Func<Task<T>> operation)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await operation();
            stopwatch.Stop();

            RecordOperationTime(operationName, stopwatch.Elapsed);
            return result;
        }
        catch
        {
            stopwatch.Stop();
            RecordOperationTime($"{operationName}_failed", stopwatch.Elapsed);
            throw;
        }
    }

    private void RecordOperationTime(string operationName, TimeSpan duration)
    {
        if (!_operationTimes.ContainsKey(operationName))
        {
            _operationTimes[operationName] = new List<TimeSpan>();
        }

        _operationTimes[operationName].Add(duration);

        // Log slow operations
        if (duration > TimeSpan.FromSeconds(5))
        {
            Debug.WriteLine($"Slow operation detected: {operationName} took {duration.TotalSeconds:F2}s");
        }
    }

    public PerformanceReport GenerateReport()
    {
        return new PerformanceReport
        {
            OperationStats = _operationTimes.ToDictionary(
                kvp => kvp.Key,
                kvp => new OperationStats
                {
                    Count = kvp.Value.Count,
                    AverageTime = TimeSpan.FromTicks((long)kvp.Value.Average(t => t.Ticks)),
                    MaxTime = kvp.Value.Max(),
                    MinTime = kvp.Value.Min()
                })
        };
    }
}
```

## Testing Performance

### Benchmark Tests

Create performance benchmark tests:

```csharp
[Test]
public async Task QueryPerformance_LargeDataset_CompletesWithinThreshold()
{
    // Arrange
    var request = new QueryFeaturesRequest
    {
        ServiceId = "service-id",
        LayerId = 0,
        Where = "1=1",
        ResultRecordCount = 1000,
    };

    // Act
    var stopwatch = Stopwatch.StartNew();
    using var result = await _client.QueryFeaturesAsync(request);
    stopwatch.Stop();

    // Assert
    Assert.Less(stopwatch.ElapsedMilliseconds, 5000, "Query should complete within 5 seconds");
    Assert.AreEqual(1000, result.RootElement.GetProperty("features").GetArrayLength());
}
```

### Memory Tests

Test memory usage:

```csharp
[Test]
public void ImageProcessing_LargeImages_DoesNotExceedMemoryLimit()
{
    var initialMemory = GC.GetTotalMemory(true);

    // Process multiple large images
    for (int i = 0; i < 10; i++)
    {
        ProcessLargeImage($"test-image-{i}.jpg");

        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    var finalMemory = GC.GetTotalMemory(true);
    var memoryIncrease = finalMemory - initialMemory;

    Assert.Less(memoryIncrease, 50 * 1024 * 1024, "Memory increase should be less than 50MB");
}
```

## Best Practices Summary

1. **Data Loading**:
   - Use pagination and lazy loading
   - Implement efficient queries with proper filtering
   - Cache frequently accessed data

2. **Memory Management**:
   - Dispose resources properly
   - Use efficient collections
   - Optimize image handling

3. **UI Performance**:
   - Implement virtual scrolling
   - Use compiled bindings
   - Optimize image display

4. **Background Processing**:
   - Use parallel processing appropriately
   - Implement efficient background sync
   - Respect device resources

5. **Network Optimization**:
   - Batch requests when possible
   - Use compression for large transfers
   - Implement intelligent caching

6. **Battery Optimization**:
   - Minimize GPS usage
   - Schedule operations efficiently
   - Respect device power state

## Related Documentation

- [Mobile SDK Overview](../README.md)
- [Offline Sync](offline-sync.md)
- [Troubleshooting](troubleshooting.md)
- [Camera Integration](camera-integration.md)