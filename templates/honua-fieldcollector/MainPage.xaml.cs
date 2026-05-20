using Microsoft.Extensions.Logging;

namespace HonuaFieldCollector;

public partial class MainPage : ContentPage
{
    private readonly ILogger<MainPage> _logger;

    private int _recordsCollected = 0;
    private int _photosCollected = 0;
    private readonly List<ActivityItem> _recentActivity = new();

    public MainPage(ILogger<MainPage> logger)
    {
        InitializeComponent();
        _logger = logger;

        InitializeApp();
    }

    private async void InitializeApp()
    {
        try
        {
            _logger.LogInformation("Initializing YOUR_COMPANY_NAME Field Collection App");

            // Initialize recent activity list
            RecentActivityList.ItemsSource = _recentActivity;

            // Load initial form
            await DataForm.LoadFormSchemaAsync("field-site-inspection");

            // Add welcome activity
            AddActivity("🚀", "App Started", "Ready for field data collection");

            _logger.LogInformation("App initialization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App initialization failed");
            await DisplayAlert("Initialization Error",
                $"Failed to initialize app: {ex.Message}", "OK");
        }
    }

    #region Form Events

    private async void OnDataCollected(object sender, EventArgs e)
    {
        try
        {
            _recordsCollected++;
            var formData = ReadFormData(e);

            // Count photos in collected data
            var photos = CountPhotosInFormData(formData);
            _photosCollected += photos;

            // Update statistics
            UpdateStatistics();

            // Add to recent activity
            var location = GetLocationFromFormData(formData);
            AddActivity("📝", $"Record #{_recordsCollected}",
                $"Collected at {location} • {photos} photos");

            // Show success message
            await ShowSuccessToast($"✅ Record #{_recordsCollected} saved successfully!");

            // Optional: Show detailed success dialog
            var showDetails = await DisplayAlert("Success! 🎉",
                $"Record #{_recordsCollected} saved successfully!\n\n" +
                $"📍 Location: {location}\n" +
                $"📷 Photos: {photos} attached\n" +
                $"📊 Total fields: {formData.Count}",
                "View Details", "Continue");

            if (showDetails)
            {
                await ShowDataDetails(formData);
            }

            _logger.LogInformation("Data collection completed: Record {RecordNumber}", _recordsCollected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process collected data");
            await DisplayAlert("Error", $"Failed to process data: {ex.Message}", "OK");
        }
    }

    private void OnValidationChanged(object sender, EventArgs e)
    {
        // Real-time validation feedback is handled by the form component
        _logger.LogDebug("Form validation changed: Valid={IsValid}", ReadProperty<bool>(e, "IsValid"));
    }

    private void OnFormLoadingChanged(object sender, EventArgs e)
    {
        var isLoading = ReadProperty<bool>(e, "IsLoading");
        ShowLoading(isLoading, isLoading ? "Loading form..." : "");
    }

    #endregion

    #region Location Events

    private void OnLocationUpdated(object sender, EventArgs e)
    {
        var location = ReadProperty<object>(e, "Location");
        _logger.LogDebug("Location updated: Lat={Latitude}, Lon={Longitude}, Accuracy={Accuracy}m",
            ReadProperty<double>(location, "Latitude"),
            ReadProperty<double>(location, "Longitude"),
            ReadProperty<double>(location, "Accuracy"));
    }

    private async void OnLocationTapped(object sender, TappedEventArgs e)
    {
        try
        {
            var location = await LocationIndicator.GetCurrentLocationAsync();
            if (location != null)
            {
                await DisplayAlert("Current Location",
                    $"📍 Latitude: {location.Latitude:F6}\n" +
                    $"📍 Longitude: {location.Longitude:F6}\n" +
                    $"🎯 Accuracy: {location.Accuracy:F1} meters\n" +
                    $"⏰ Updated: {location.Timestamp:HH:mm:ss}",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Location Error", ex.Message, "OK");
        }
    }

    #endregion

    #region Map Events

    private void OnViewMapClicked(object sender, EventArgs e)
    {
        MainTabs.SelectedIndex = 1; // Switch to map tab
        AddActivity("🗺️", "Map Opened", "Viewing collected data on map");
    }

    private void OnMapLayerClicked(object sender, EventArgs e)
    {
        _logger.LogInformation("Map layer clicked: {LayerName}", ReadProperty<string>(e, "LayerName"));
    }

    private async void OnMapFeatureSelected(object sender, EventArgs e)
    {
        var feature = ReadProperty<object>(e, "Feature");
        var attributes = ReadProperty<IReadOnlyDictionary<string, object>>(feature, "Attributes");
        await DisplayAlert("Feature Selected",
            $"Feature ID: {ReadProperty<string>(feature, "Id")}\n" +
            $"Layer: {ReadProperty<string>(feature, "LayerName")}\n" +
            $"Attributes: {attributes?.Count ?? 0} fields",
            "OK");
    }

    #endregion

    #region Sync Events

    private void OnSyncCompleted(object sender, EventArgs e)
    {
        AddActivity("🔄", "Sync Complete",
            $"↓{ReadProperty<int>(e, "DownloadedRecords")} ↑{ReadProperty<int>(e, "UploadedRecords")}");
    }

    private async void OnSyncConflict(object sender, EventArgs e)
    {
        var action = await DisplayActionSheet("Sync Conflict",
            "Cancel", null,
            "Use Server Version",
            "Keep Local Version",
            "Merge Changes");

        switch (action)
        {
            case "Use Server Version":
                await InvokeAsync(e, "ResolveWithServerVersion");
                break;
            case "Keep Local Version":
                await InvokeAsync(e, "ResolveWithLocalVersion");
                break;
            case "Merge Changes":
                await InvokeAsync(e, "ShowMergeDialog");
                break;
        }
    }

    private async void OnSyncStatusTapped(object sender, TappedEventArgs e)
    {
        var syncInfo = await SyncStatus.GetDetailedStatusAsync();
        await DisplayAlert("Sync Status",
            $"Last Sync: {syncInfo.LastSyncTime:HH:mm:ss}\n" +
            $"Pending: {syncInfo.PendingUploads} records\n" +
            $"Status: {syncInfo.Status}",
            "OK");
    }

    #endregion

    #region Helper Methods

    private void UpdateStatistics()
    {
        RecordsCountLabel.Text = _recordsCollected.ToString();
        PhotosCountLabel.Text = _photosCollected.ToString();
    }

    private void AddActivity(string icon, string title, string description)
    {
        var activity = new ActivityItem
        {
            Icon = icon,
            Title = title,
            Description = description,
            Time = DateTime.Now.ToString("HH:mm")
        };

        _recentActivity.Insert(0, activity);

        // Keep only last 20 activities
        while (_recentActivity.Count > 20)
        {
            _recentActivity.RemoveAt(_recentActivity.Count - 1);
        }
    }

    private int CountPhotosInFormData(IReadOnlyDictionary<string, object> formData)
    {
        return formData.Values
            .OfType<List<object>>()
            .SelectMany(x => x)
            .Count(x => x.ToString()?.Contains("photo", StringComparison.OrdinalIgnoreCase) == true);
    }

    private string GetLocationFromFormData(IReadOnlyDictionary<string, object> formData)
    {
        if (formData.TryGetValue("location", out var location) && location != null)
        {
            return location.ToString() ?? "Unknown";
        }
        return "No location";
    }

    private async Task ShowDataDetails(IReadOnlyDictionary<string, object> formData)
    {
        var details = string.Join("\n", formData
            .Take(10) // Show first 10 fields
            .Select(kvp => $"{kvp.Key}: {kvp.Value}"));

        if (formData.Count > 10)
        {
            details += $"\n... and {formData.Count - 10} more fields";
        }

        await DisplayAlert("Data Details", details, "OK");
    }

    private async Task ShowSuccessToast(string message)
    {
        SuccessMessage.Text = message;
        SuccessToast.IsVisible = true;

        // Auto-hide after 3 seconds
        await Task.Delay(3000);
        SuccessToast.IsVisible = false;
    }

    private void ShowLoading(bool isLoading, string message = "Loading...")
    {
        LoadingOverlay.IsVisible = isLoading;
        LoadingIndicator.IsRunning = isLoading;
        LoadingMessage.Text = message;
    }

    private static Dictionary<string, object> ReadFormData(EventArgs args)
    {
        return ReadProperty<Dictionary<string, object>>(args, "FormData") ??
            ReadProperty<IReadOnlyDictionary<string, object>>(args, "FormData")?.ToDictionary(
                pair => pair.Key,
                pair => pair.Value) ??
            [];
    }

    private static T? ReadProperty<T>(object? source, string propertyName)
    {
        if (source == null)
        {
            return default;
        }

        var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return value is T typed ? typed : default;
    }

    private static async Task InvokeAsync(object source, string methodName)
    {
        var result = source.GetType().GetMethod(methodName)?.Invoke(source, null);
        if (result is Task task)
        {
            await task;
        }
    }

    #endregion
}

/// <summary>
/// Activity item for recent activity list
/// </summary>
public class ActivityItem
{
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Time { get; set; } = "";
}
