using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Configuration;
using Honua.Mobile.FieldCollection.Services.Diagnostics;
using Honua.Mobile.Maui.Diagnostics;
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
    private MobileBuildConfiguration buildConfiguration = MobileBuildConfiguration.Empty;

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
    private bool enableExceptionReporting;

    [ObservableProperty]
    private bool canEnableExceptionReporting = true;

    [ObservableProperty]
    private string exceptionReportingEndpoint = string.Empty;

    [ObservableProperty]
    private string exceptionReportingStatus = "Disabled";

    [ObservableProperty]
    private int syncIntervalMinutes = 15;

    [ObservableProperty]
    private int maxOfflineStorageMb = 500;

    public SettingsViewModel(
        INavigationService navigationService,
        IAuthenticationService authService,
        ISettingsService settingsService,
        IConnectivityService connectivityService,
        MobileBuildConfiguration buildConfiguration)
        : base(navigationService)
    {
        _authService = authService;
        _settingsService = settingsService;
        _connectivityService = connectivityService;
        BuildConfiguration = buildConfiguration;

        Title = "Settings";

        // Subscribe to auth service changes
        _authService.PropertyChanged += OnAuthServicePropertyChanged;
        _connectivityService.ConnectivityChanged += OnConnectivityChanged;

        // Initialize properties
        UpdateFromAuthService();
        IsOnline = _connectivityService.IsConnected;
        AppVersion = BuildConfiguration.Metadata.VersionDisplay;
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
            CanEnableExceptionReporting = Preferences.Default.Get(
                FieldCollectionExceptionReporting.EnvironmentEnabledPreferenceKey,
                true);
            EnableExceptionReporting = CanEnableExceptionReporting &&
                Preferences.Default.Get(FieldCollectionExceptionReporting.TesterConsentPreferenceKey, false) &&
                ReadExceptionReportingMode() != MobileExceptionReportingMode.Disabled;
            ExceptionReportingEndpoint = Preferences.Default.Get(
                FieldCollectionExceptionReporting.EndpointPreferenceKey,
                string.Empty);
            UpdateExceptionReportingStatus();
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
            SaveExceptionReportingPreferences();
            await _settingsService.SetSettingAsync("sync_interval_minutes", SyncIntervalMinutes);
            await _settingsService.SetSettingAsync("max_offline_storage_mb", MaxOfflineStorageMb);

            await ShowMessage(
                "Settings Saved",
                "Your settings have been saved. Exception reporting changes take effect after app restart.");
        });
    }

    partial void OnEnableExceptionReportingChanged(bool value)
    {
        UpdateExceptionReportingStatus();
    }

    partial void OnCanEnableExceptionReportingChanged(bool value)
    {
        if (!value)
        {
            EnableExceptionReporting = false;
        }

        UpdateExceptionReportingStatus();
    }

    partial void OnExceptionReportingEndpointChanged(string value)
    {
        UpdateExceptionReportingStatus();
    }

    private MobileExceptionReportingMode ReadExceptionReportingMode()
    {
        var value = Preferences.Default.Get(
            FieldCollectionExceptionReporting.ModePreferenceKey,
            MobileExceptionReportingMode.Disabled.ToString());
        return string.Equals(value, "Server", StringComparison.OrdinalIgnoreCase)
            ? MobileExceptionReportingMode.ServerUpload
            : Enum.TryParse<MobileExceptionReportingMode>(value, ignoreCase: true, out var mode)
                ? mode
                : MobileExceptionReportingMode.Disabled;
    }

    private void SaveExceptionReportingPreferences()
    {
        var endpoint = (ExceptionReportingEndpoint ?? string.Empty).Trim();
        var shouldEnable = EnableExceptionReporting && CanEnableExceptionReporting;
        var mode = shouldEnable
            ? string.IsNullOrWhiteSpace(endpoint)
                ? MobileExceptionReportingMode.LocalOnly
                : MobileExceptionReportingMode.ServerUpload
            : MobileExceptionReportingMode.Disabled;

        Preferences.Default.Set(
            FieldCollectionExceptionReporting.TesterConsentPreferenceKey,
            shouldEnable);
        Preferences.Default.Set(
            FieldCollectionExceptionReporting.ModePreferenceKey,
            mode.ToString());

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Preferences.Default.Remove(FieldCollectionExceptionReporting.EndpointPreferenceKey);
        }
        else
        {
            Preferences.Default.Set(FieldCollectionExceptionReporting.EndpointPreferenceKey, endpoint);
        }

        UpdateExceptionReportingStatus();
    }

    private void UpdateExceptionReportingStatus()
    {
        if (!CanEnableExceptionReporting)
        {
            ExceptionReportingStatus = "Disabled by environment";
            return;
        }

        if (!EnableExceptionReporting)
        {
            ExceptionReportingStatus = "Disabled";
            return;
        }

        ExceptionReportingStatus = string.IsNullOrWhiteSpace(ExceptionReportingEndpoint)
            ? "Local queue only"
            : "Uploads after local queue";
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
            await ShowError("Cache Clear Unavailable", "Cache clearing is not configured yet.");
        }
    }

    [RelayCommand]
    private async Task ExportData()
    {
        await ShowError("Export Unavailable", "Data export is not configured yet.");
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
