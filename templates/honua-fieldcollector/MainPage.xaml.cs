using Microsoft.Extensions.Logging;
using Honua.Mobile.Core;
using Honua.Mobile.Core.Events;
<!--#if (enableIoT)-->
using Honua.Mobile.IoT;
<!--#endif-->

namespace namespace;

public partial class MainPage : ContentPage
{
    private readonly ILogger<MainPage> _logger;
    private readonly IHonuaClient _honuaClient;
<!--#if (enableIoT)-->
    private readonly IIoTSensorService _sensorService;
<!--#endif-->

    private int _recordsCollected = 0;
    private int _photosCollected = 0;
    private readonly List<ActivityItem> _recentActivity = new();

    public MainPage(ILogger<MainPage> logger, IHonuaClient honuaClient
<!--#if (enableIoT)-->
        , IIoTSensorService sensorService
<!--#endif-->
        )
    {
        InitializeComponent();
        _logger = logger;
        _honuaClient = honuaClient;
<!--#if (enableIoT)-->
        _sensorService = sensorService;
<!--#endif-->

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
            await DataForm.LoadFormSchemaAsync("site_inspection");

            // Add welcome activity
            AddActivity("🚀", "App Started", "Ready for field data collection");

<!--#if (enableIoT)-->
            // Start sensor discovery if enabled
            if (_sensorService != null)
            {
                await _sensorService.StartSensorDiscoveryAsync(SensorType.Environmental);
                AddActivity("🤖", "Sensor Discovery", "Scanning for IoT sensors");
            }
<!--#endif-->

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

    private async void OnDataCollected(object sender, FormSubmittedEventArgs e)
    {
        try
        {
            _recordsCollected++;

            // Count photos in collected data
            var photos = CountPhotosInFormData(e.FormData);
            _photosCollected += photos;

            // Update statistics
            UpdateStatistics();

            // Add to recent activity
            var location = GetLocationFromFormData(e.FormData);
            AddActivity("📝", $"Record #{_recordsCollected}",
                $"Collected at {location} • {photos} photos");

            // Show success message
            await ShowSuccessToast($"✅ Record #{_recordsCollected} saved successfully!");

            // Optional: Show detailed success dialog
            var showDetails = await DisplayAlert("Success! 🎉",
                $"Record #{_recordsCollected} saved successfully!\n\n" +
                $"📍 Location: {location}\n" +
                $"📷 Photos: {photos} attached\n" +
                $"📊 Total fields: {e.FormData.Count}",
                "View Details", "Continue");

            if (showDetails)
            {
                await ShowDataDetails(e.FormData);
            }

            _logger.LogInformation("Data collection completed: Record {RecordNumber}", _recordsCollected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process collected data");
            await DisplayAlert("Error", $"Failed to process data: {ex.Message}", "OK");
        }
    }

    private void OnValidationChanged(object sender, FormValidationEventArgs e)
    {
        // Real-time validation feedback is handled by the form component
        _logger.LogDebug("Form validation changed: Valid={IsValid}", e.IsValid);
    }

    private void OnFormLoadingChanged(object sender, FormLoadingEventArgs e)
    {
        ShowLoading(e.IsLoading, e.IsLoading ? "Loading form..." : "");
    }

    #endregion

    #region Location Events

    private void OnLocationUpdated(object sender, LocationUpdatedEventArgs e)
    {
        _logger.LogDebug("Location updated: Lat={Latitude}, Lon={Longitude}, Accuracy={Accuracy}m",
            e.Location.Latitude, e.Location.Longitude, e.Location.Accuracy);
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

    private void OnMapLayerClicked(object sender, LayerClickedEventArgs e)
    {
        _logger.LogInformation("Map layer clicked: {LayerName}", e.LayerName);
    }

    private async void OnMapFeatureSelected(object sender, FeatureSelectedEventArgs e)
    {
        await DisplayAlert("Feature Selected",
            $"Feature ID: {e.Feature.Id}\n" +
            $"Layer: {e.Feature.LayerName}\n" +
            $"Attributes: {e.Feature.Attributes.Count} fields",
            "OK");
    }

    #endregion

<!--#if (enableIoT)-->
    #region IoT Sensor Events

    private async void OnSensorConnected(object sender, SensorConnectedEventArgs e)
    {
        AddActivity("🤖", "Sensor Connected", $"{e.SensorName} ({e.SensorType})");

        await DisplayAlert("Sensor Connected! 🤖",
            $"Successfully connected to {e.SensorName}\n" +
            $"Type: {e.SensorType}\n" +
            $"Capabilities: {string.Join(", ", e.Capabilities)}",
            "OK");
    }

    private void OnSensorDataReceived(object sender, SensorDataEventArgs e)
    {
        _logger.LogDebug("Sensor data received: {SensorName} = {Value} {Unit}",
            e.SensorName, e.Value, e.Unit);

        // Update form fields if sensor data is configured
        DataForm.UpdateSensorField(e.SensorName, e.Value, e.Unit);
    }

    #endregion
<!--#endif-->

<!--#if (enableAR)-->
    #region AR Events

    private async void OnARSessionStarted(object sender, ARSessionEventArgs e)
    {
        AddActivity("🥽", "AR Started", "Augmented reality session active");

        await DisplayAlert("AR Active! 🥽",
            "Augmented reality is now active.\n" +
            "Point your camera at infrastructure to see overlays.",
            "OK");
    }

    private async void OnARUtilitySelected(object sender, ARUtilityEventArgs e)
    {
        await DisplayAlert("Utility Info",
            $"Type: {e.Utility.Type}\n" +
            $"Depth: {e.Utility.DepthMeters:F1}m\n" +
            $"Material: {e.Utility.Material}\n" +
            $"Install Date: {e.Utility.InstallDate:yyyy}",
            "OK");
    }

    #endregion
<!--#endif-->

    #region Sync Events

    private void OnSyncCompleted(object sender, SyncCompletedEventArgs e)
    {
        AddActivity("🔄", "Sync Complete",
            $"↓{e.DownloadedRecords} ↑{e.UploadedRecords}");
    }

    private async void OnSyncConflict(object sender, SyncConflictEventArgs e)
    {
        var action = await DisplayActionSheet("Sync Conflict",
            "Cancel", null,
            "Use Server Version",
            "Keep Local Version",
            "Merge Changes");

        switch (action)
        {
            case "Use Server Version":
                await e.ResolveWithServerVersion();
                break;
            case "Keep Local Version":
                await e.ResolveWithLocalVersion();
                break;
            case "Merge Changes":
                await e.ShowMergeDialog();
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

    private int CountPhotosInFormData(Dictionary<string, object> formData)
    {
        return formData.Values
            .OfType<List<object>>()
            .SelectMany(x => x)
            .Count(x => x.ToString()?.Contains("photo", StringComparison.OrdinalIgnoreCase) == true);
    }

    private string GetLocationFromFormData(Dictionary<string, object> formData)
    {
        if (formData.TryGetValue("location", out var location) && location != null)
        {
            return location.ToString() ?? "Unknown";
        }
        return "No location";
    }

    private async Task ShowDataDetails(Dictionary<string, object> formData)
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