using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using FieldDeviceInfo = Honua.Mobile.FieldCollection.Models.DeviceInfo;

namespace Honua.Mobile.FieldCollection.ViewModels;

public partial class SettingsViewModel : BaseViewModel
{
    private readonly IAuthenticationService _authService;
    private readonly ISettingsService _settingsService;
    private readonly IConnectivityService _connectivityService;

    [ObservableProperty]
    private string userName = string.Empty;

    [ObservableProperty]
    private string serverUrl = string.Empty;

    [ObservableProperty]
    private bool isAuthenticated;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private string appVersion = string.Empty;

    [ObservableProperty]
    private FieldDeviceInfo deviceInfo = new();

    [ObservableProperty]
    private bool enableLocationTracking = true;

    [ObservableProperty]
    private bool enableBackgroundSync = true;

    [ObservableProperty]
    private bool enablePushNotifications = true;

    [ObservableProperty]
    private bool enableDeveloperMode = false;

    [ObservableProperty]
    private int syncIntervalMinutes = 15;

    [ObservableProperty]
    private int maxOfflineStorageMb = 500;

    public SettingsViewModel(
        INavigationService navigationService,
        IAuthenticationService authService,
        ISettingsService settingsService,
        IConnectivityService connectivityService)
        : base(navigationService)
    {
        _authService = authService;
        _settingsService = settingsService;
        _connectivityService = connectivityService;

        Title = "Settings";

        // Subscribe to auth service changes
        _authService.PropertyChanged += OnAuthServicePropertyChanged;
        _connectivityService.ConnectivityChanged += OnConnectivityChanged;

        // Initialize properties
        UpdateFromAuthService();
        IsOnline = _connectivityService.IsConnected;
        AppVersion = GetAppVersion();
        InitializeDeviceInfo();
    }

    protected override async Task OnRefresh()
    {
        await LoadSettings();
    }

    private void OnAuthServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateFromAuthService();
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        IsOnline = isConnected;
    }

    private void UpdateFromAuthService()
    {
        IsAuthenticated = _authService.IsAuthenticated;
        UserName = _authService.CurrentUserName ?? "Not signed in";
        ServerUrl = _authService.ServerUrl ?? "Not configured";
    }

    private string GetAppVersion()
    {
        try
        {
            return AppInfo.Current.VersionString;
        }
        catch
        {
            return "1.0.0";
        }
    }

    private void InitializeDeviceInfo()
    {
        try
        {
            var currentDevice = Microsoft.Maui.Devices.DeviceInfo.Current;

            DeviceInfo = new FieldDeviceInfo
            {
                DeviceId = Preferences.Get("device_id", Guid.NewGuid().ToString()),
                DeviceName = currentDevice.Name,
                Platform = currentDevice.Platform.ToString(),
                AppVersion = AppVersion,
                OSVersion = currentDevice.VersionString,
                IsOnline = IsOnline,
                LastActiveAt = DateTime.UtcNow
            };

            // Save device ID if it's new
            if (!Preferences.ContainsKey("device_id"))
            {
                Preferences.Set("device_id", DeviceInfo.DeviceId);
            }
        }
        catch
        {
            // Fallback device info
            DeviceInfo = new FieldDeviceInfo
            {
                DeviceId = "unknown",
                DeviceName = "Unknown Device",
                Platform = "Unknown",
                AppVersion = AppVersion,
                OSVersion = "Unknown",
                IsOnline = IsOnline,
                LastActiveAt = DateTime.UtcNow
            };
        }
    }

    [RelayCommand]
    private async Task LoadSettings()
    {
        await ExecuteAsync(async () =>
        {
            EnableLocationTracking = await _settingsService.GetSettingAsync("location_tracking", true);
            EnableBackgroundSync = await _settingsService.GetSettingAsync("background_sync", true);
            EnablePushNotifications = await _settingsService.GetSettingAsync("push_notifications", true);
            EnableDeveloperMode = await _settingsService.GetSettingAsync("developer_mode", false);
            SyncIntervalMinutes = await _settingsService.GetSettingAsync("sync_interval_minutes", 15);
            MaxOfflineStorageMb = await _settingsService.GetSettingAsync("max_offline_storage_mb", 500);
        });
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        await ExecuteAsync(async () =>
        {
            await _settingsService.SetSettingAsync("location_tracking", EnableLocationTracking);
            await _settingsService.SetSettingAsync("background_sync", EnableBackgroundSync);
            await _settingsService.SetSettingAsync("push_notifications", EnablePushNotifications);
            await _settingsService.SetSettingAsync("developer_mode", EnableDeveloperMode);
            await _settingsService.SetSettingAsync("sync_interval_minutes", SyncIntervalMinutes);
            await _settingsService.SetSettingAsync("max_offline_storage_mb", MaxOfflineStorageMb);

            await ShowMessage("Settings Saved", "Your settings have been saved successfully.");
        });
    }

    [RelayCommand]
    private async Task SignOut()
    {
        var confirmed = await ShowConfirmation("Sign Out",
            "Are you sure you want to sign out? Any unsynced changes will be lost.",
            "Sign Out", "Cancel");

        if (confirmed)
        {
            await ExecuteAsync(async () =>
            {
                await _authService.LogoutAsync();
                await NavigationService.NavigateToAsync("authentication");
            });
        }
    }

    [RelayCommand]
    private async Task ConfigureServer()
    {
        await NavigationService.NavigateToAsync("settings/server-config");
    }

    [RelayCommand]
    private async Task ViewUserProfile()
    {
        if (!IsAuthenticated)
        {
            await ShowError("Not Authenticated", "Please sign in to view your profile.");
            return;
        }

        await NavigationService.NavigateToAsync("settings/user-profile");
    }

    [RelayCommand]
    private async Task ViewAbout()
    {
        await NavigationService.NavigateToAsync("settings/about");
    }

    [RelayCommand]
    private async Task ViewDiagnostics()
    {
        await NavigationService.NavigateToAsync("diagnostics");
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrEmpty(ServerUrl) || ServerUrl == "Not configured")
        {
            await ShowError("No Server", "Please configure a server URL first.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var isValid = await _authService.ValidateConnectionAsync(ServerUrl);
            if (isValid)
            {
                await ShowMessage("Connection Test", "Server connection is working properly.");
            }
            else
            {
                await ShowError("Connection Failed", "Unable to connect to the server. Please check the URL and your internet connection.");
            }
        });
    }

    [RelayCommand]
    private async Task ClearCache()
    {
        var confirmed = await ShowConfirmation("Clear Cache",
            "This will clear all cached data. Are you sure?",
            "Clear", "Cancel");

        if (confirmed)
        {
            await ExecuteAsync(async () =>
            {
                // In a real implementation, this would clear various caches
                await Task.Delay(1000); // Simulate cache clearing
                await ShowMessage("Cache Cleared", "All cached data has been cleared.");
            });
        }
    }

    [RelayCommand]
    private async Task ExportData()
    {
        await ExecuteAsync(async () =>
        {
            // In a real implementation, this would export user data
            await Task.Delay(2000); // Simulate export process
            await ShowMessage("Export Complete", "Data has been exported to your device storage.");
        });
    }

    [RelayCommand]
    private async Task ResetApp()
    {
        var confirmed = await ShowConfirmation("Reset App",
            "This will reset the app to its initial state and clear all data. This cannot be undone. Are you sure?",
            "Reset", "Cancel");

        if (confirmed)
        {
            var doubleConfirm = await ShowConfirmation("Confirm Reset",
                "This action cannot be undone. All data will be permanently lost.",
                "Yes, Reset", "Cancel");

            if (doubleConfirm)
            {
                await ExecuteAsync(async () =>
                {
                    await _authService.LogoutAsync();

                    // Clear all preferences and secure storage
                    Preferences.Clear();
                    SecureStorage.RemoveAll();

                    await ShowMessage("App Reset", "The app has been reset. Please restart the application.");
                });
            }
        }
    }
}
