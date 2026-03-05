// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HonuaFieldCollector.Features.Authentication;
using HonuaFieldCollector.Features.DataCollection;
using HonuaFieldCollector.Features.Sync;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace HonuaFieldCollector.Features.Dashboard;

/// <summary>
/// ViewModel for the main dashboard providing overview of field collection activities.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly ILogger<DashboardViewModel> _logger;
    private readonly IAuthenticationService _authService;
    private readonly IDataCollectionService _dataService;
    private readonly ISyncService _syncService;
    private readonly ILocationService _locationService;
    private readonly IConnectivity _connectivity;

    [ObservableProperty]
    private string _welcomeMessage = "Welcome Back!";

    [ObservableProperty]
    private string _currentProjectName = "Loading...";

    [ObservableProperty]
    private DateTime _lastSyncTime;

    [ObservableProperty]
    private int _todayRecordsCount;

    [ObservableProperty]
    private int _todayPhotosCount;

    [ObservableProperty]
    private int _pendingSyncCount;

    [ObservableProperty]
    private double _locationAccuracy;

    [ObservableProperty]
    private string _connectivityStatus = "Checking...";

    [ObservableProperty]
    private string _connectivityIcon = "🔄";

    [ObservableProperty]
    private Color _connectivityColor = Colors.Gray;

    [ObservableProperty]
    private string _versionInfo = $"Honua Field Collector v{AppInfo.VersionString}";

    [ObservableProperty]
    private bool _isLoading = true;

    public ObservableCollection<ActivityItem> RecentActivities { get; } = new();

    public DashboardViewModel(
        ILogger<DashboardViewModel> logger,
        IAuthenticationService authService,
        IDataCollectionService dataService,
        ISyncService syncService,
        ILocationService locationService,
        IConnectivity connectivity)
    {
        _logger = logger;
        _authService = authService;
        _dataService = dataService;
        _syncService = syncService;
        _locationService = locationService;
        _connectivity = connectivity;

        // Subscribe to connectivity changes
        _connectivity.ConnectivityChanged += OnConnectivityChanged;

        // Subscribe to sync events
        _syncService.SyncCompleted += OnSyncCompleted;
        _syncService.SyncStarted += OnSyncStarted;

        // Subscribe to location updates
        _locationService.LocationUpdated += OnLocationUpdated;

        // Initialize dashboard data
        _ = InitializeAsync();
    }

    /// <summary>
    /// Initializes dashboard data asynchronously.
    /// </summary>
    private async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing dashboard");

            // Set welcome message with user name
            var user = await _authService.GetCurrentUserAsync();
            WelcomeMessage = $"Welcome back, {user?.FirstName ?? "Field Worker"}!";

            // Load current project
            var currentProject = await _dataService.GetCurrentProjectAsync();
            CurrentProjectName = currentProject?.Name ?? "No active project";

            // Load today's statistics
            await LoadTodayStatisticsAsync();

            // Load recent activities
            await LoadRecentActivitiesAsync();

            // Update connectivity status
            await UpdateConnectivityStatusAsync();

            // Get location accuracy
            await UpdateLocationStatusAsync();

            // Get last sync time
            LastSyncTime = await _syncService.GetLastSyncTimeAsync();

            IsLoading = false;
            _logger.LogInformation("Dashboard initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize dashboard");
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads today's statistics for records, photos, and sync status.
    /// </summary>
    private async Task LoadTodayStatisticsAsync()
    {
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        // Get today's records
        var todayRecords = await _dataService.GetRecordsAsync(today, tomorrow);
        TodayRecordsCount = todayRecords.Count;

        // Get today's photos
        var todayPhotos = await _dataService.GetPhotosAsync(today, tomorrow);
        TodayPhotosCount = todayPhotos.Count;

        // Get pending sync count
        PendingSyncCount = await _syncService.GetPendingSyncCountAsync();

        _logger.LogDebug("Today's stats: {Records} records, {Photos} photos, {Pending} pending sync",
            TodayRecordsCount, TodayPhotosCount, PendingSyncCount);
    }

    /// <summary>
    /// Loads recent activity items for display.
    /// </summary>
    private async Task LoadRecentActivitiesAsync()
    {
        try
        {
            var activities = await _dataService.GetRecentActivitiesAsync(limit: 10);

            RecentActivities.Clear();
            foreach (var activity in activities)
            {
                RecentActivities.Add(new ActivityItem
                {
                    Id = activity.Id,
                    Title = activity.Title,
                    Description = activity.Description,
                    Timestamp = activity.Timestamp,
                    Type = activity.Type,
                    StatusColor = GetStatusColor(activity.Status)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recent activities");
        }
    }

    /// <summary>
    /// Updates connectivity status based on current connection.
    /// </summary>
    private async Task UpdateConnectivityStatusAsync()
    {
        await Task.Run(() =>
        {
            var networkAccess = _connectivity.NetworkAccess;
            var connectionProfiles = _connectivity.ConnectionProfiles;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                switch (networkAccess)
                {
                    case NetworkAccess.Internet:
                        ConnectivityStatus = "Online";
                        ConnectivityIcon = "🟢";
                        ConnectivityColor = Colors.Green;
                        break;
                    case NetworkAccess.ConstrainedInternet:
                        ConnectivityStatus = "Limited Connection";
                        ConnectivityIcon = "🟡";
                        ConnectivityColor = Colors.Orange;
                        break;
                    case NetworkAccess.Local:
                        ConnectivityStatus = "Local Network Only";
                        ConnectivityIcon = "🟡";
                        ConnectivityColor = Colors.Orange;
                        break;
                    default:
                        ConnectivityStatus = "Offline";
                        ConnectivityIcon = "🔴";
                        ConnectivityColor = Colors.Red;
                        break;
                }
            });
        });
    }

    /// <summary>
    /// Updates location accuracy status.
    /// </summary>
    private async Task UpdateLocationStatusAsync()
    {
        try
        {
            var location = await _locationService.GetCurrentLocationAsync();
            LocationAccuracy = location?.Accuracy ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get location accuracy");
            LocationAccuracy = 0;
        }
    }

    /// <summary>
    /// Gets status color based on activity status.
    /// </summary>
    private static Color GetStatusColor(ActivityStatus status)
    {
        return status switch
        {
            ActivityStatus.Completed => Colors.Green,
            ActivityStatus.InProgress => Colors.Orange,
            ActivityStatus.Failed => Colors.Red,
            ActivityStatus.Pending => Colors.Blue,
            _ => Colors.Gray
        };
    }

    #region Commands

    [RelayCommand]
    private async Task StartDataCollectionAsync()
    {
        try
        {
            _logger.LogInformation("Starting data collection from dashboard");
            await Shell.Current.GoToAsync("//collect");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to data collection");
            await ShowErrorAsync("Navigation Error", "Failed to open data collection screen");
        }
    }

    [RelayCommand]
    private async Task OpenMapAsync()
    {
        try
        {
            _logger.LogInformation("Opening map from dashboard");
            await Shell.Current.GoToAsync("//map");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to map");
            await ShowErrorAsync("Navigation Error", "Failed to open map screen");
        }
    }

    [RelayCommand]
    private async Task OpenARViewAsync()
    {
        try
        {
            _logger.LogInformation("Opening AR view from dashboard");
            await Shell.Current.GoToAsync("//arview");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to AR view");
            await ShowErrorAsync("Navigation Error", "Failed to open AR view screen");
        }
    }

    [RelayCommand]
    private async Task OpenSensorsAsync()
    {
        try
        {
            _logger.LogInformation("Opening sensors from dashboard");
            await Shell.Current.GoToAsync("//sensors");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to sensors");
            await ShowErrorAsync("Navigation Error", "Failed to open sensors screen");
        }
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        try
        {
            _logger.LogInformation("Manual sync triggered from dashboard");

            // Show sync in progress
            ConnectivityStatus = "Syncing...";
            ConnectivityIcon = "🔄";
            ConnectivityColor = Colors.Blue;

            // Trigger sync
            await _syncService.TriggerSyncAsync();

            // Refresh dashboard data
            await LoadTodayStatisticsAsync();
            await LoadRecentActivitiesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual sync failed");
            await ShowErrorAsync("Sync Error", "Failed to sync data. Please check your connection and try again.");
        }
    }

    [RelayCommand]
    private async Task SettingsAsync()
    {
        try
        {
            await Shell.Current.GoToAsync("//settings");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to settings");
            await ShowErrorAsync("Navigation Error", "Failed to open settings screen");
        }
    }

    [RelayCommand]
    private async Task ViewActivityAsync(ActivityItem activity)
    {
        if (activity == null) return;

        try
        {
            _logger.LogInformation("Viewing activity: {ActivityId}", activity.Id);

            var route = activity.Type switch
            {
                ActivityType.DataCollection => $"//collect?recordId={activity.Id}",
                ActivityType.PhotoCapture => $"//photos?photoId={activity.Id}",
                ActivityType.Sync => $"//sync",
                ActivityType.ARVisualization => $"//arview",
                ActivityType.IoTSensor => $"//sensors?sensorId={activity.Id}",
                _ => "//dashboard"
            };

            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to view activity: {ActivityId}", activity.Id);
            await ShowErrorAsync("Navigation Error", "Failed to open activity details");
        }
    }

    [RelayCommand]
    private async Task SyncActivityAsync(ActivityItem activity)
    {
        if (activity == null) return;

        try
        {
            _logger.LogInformation("Syncing activity: {ActivityId}", activity.Id);

            await _syncService.SyncSpecificItemAsync(activity.Id, activity.Type);
            await LoadTodayStatisticsAsync();

            // Update activity status
            activity.StatusColor = Colors.Green;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync activity: {ActivityId}", activity.Id);
            await ShowErrorAsync("Sync Error", "Failed to sync this item. Please try again.");
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh dashboard");
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Event Handlers

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await UpdateConnectivityStatusAsync();
        });
    }

    private void OnSyncStarted(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ConnectivityStatus = "Syncing...";
            ConnectivityIcon = "🔄";
            ConnectivityColor = Colors.Blue;
        });
    }

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            LastSyncTime = e.CompletedAt;
            await LoadTodayStatisticsAsync();
            await UpdateConnectivityStatusAsync();
        });
    }

    private void OnLocationUpdated(object? sender, LocationUpdatedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            LocationAccuracy = e.Location.Accuracy;
        });
    }

    #endregion

    #region Helper Methods

    private static async Task ShowErrorAsync(string title, string message)
    {
        if (Application.Current?.MainPage != null)
        {
            await Application.Current.MainPage.DisplayAlert(title, message, "OK");
        }
    }

    #endregion

    /// <summary>
    /// Cleans up resources and event subscriptions.
    /// </summary>
    public void Dispose()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        _syncService.SyncCompleted -= OnSyncCompleted;
        _syncService.SyncStarted -= OnSyncStarted;
        _locationService.LocationUpdated -= OnLocationUpdated;
    }
}

/// <summary>
/// Represents a recent activity item in the dashboard.
/// </summary>
public partial class ActivityItem : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    [ObservableProperty]
    private ActivityType _type = ActivityType.DataCollection;

    [ObservableProperty]
    private Color _statusColor = Colors.Gray;
}

/// <summary>
/// Types of activities that can be performed in the app.
/// </summary>
public enum ActivityType
{
    DataCollection,
    PhotoCapture,
    Sync,
    ARVisualization,
    IoTSensor,
    FormEdit,
    MapQuery,
    Report
}

/// <summary>
/// Status of an activity.
/// </summary>
public enum ActivityStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Synced
}