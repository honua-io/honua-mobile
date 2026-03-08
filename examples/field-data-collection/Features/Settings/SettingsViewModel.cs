// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace HonuaFieldCollector.Features.Settings;

/// <summary>
/// ViewModel for the Settings / Diagnostics screen.
/// Manages server configuration, authentication session, offline settings,
/// and device diagnostics.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IAuthenticationService _authService;
    private readonly IApplicationConfigurationService _configService;
    private readonly IConnectivity _connectivity;
    private readonly IGeolocation _geolocation;
    private readonly ILogger<SettingsViewModel> _logger;

    // Account
    [ObservableProperty] private string _userDisplayName = "Not signed in";
    [ObservableProperty] private string _serverUrl = string.Empty;
    [ObservableProperty] private string _sessionStatusText = "No session";
    [ObservableProperty] private Color _sessionStatusColor = Colors.Gray;

    // Server config
    [ObservableProperty] private string _serverUrlEntry = string.Empty;
    [ObservableProperty] private string _connectionTestResult = string.Empty;
    [ObservableProperty] private Color _connectionTestColor = Colors.Gray;
    [ObservableProperty] private bool _hasConnectionTestResult;

    // Offline / Sync
    [ObservableProperty] private string _selectedSyncInterval = "2 minutes";
    [ObservableProperty] private bool _syncWifiOnly;
    [ObservableProperty] private string _offlineDbSize = "Calculating...";

    // Diagnostics
    [ObservableProperty] private string _appVersion = string.Empty;
    [ObservableProperty] private string _platformInfo = string.Empty;
    [ObservableProperty] private string _networkStatus = string.Empty;
    [ObservableProperty] private string _gpsStatus = string.Empty;
    [ObservableProperty] private string _grpcStatus = string.Empty;

    public List<string> SyncIntervalOptions { get; } = ["1 minute", "2 minutes", "5 minutes", "15 minutes", "Manual"];
    public ObservableCollection<PermissionItemViewModel> Permissions { get; } = [];

    public SettingsViewModel(
        IAuthenticationService authService,
        IApplicationConfigurationService configService,
        IConnectivity connectivity,
        IGeolocation geolocation,
        ILogger<SettingsViewModel> logger)
    {
        _authService = authService;
        _configService = configService;
        _connectivity = connectivity;
        _geolocation = geolocation;
        _logger = logger;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            // Account info
            if (_authService.IsAuthenticated)
            {
                UserDisplayName = _authService.CurrentUser?.DisplayName ?? "Unknown";
                SessionStatusText = "Active";
                SessionStatusColor = Colors.Green;
            }
            else
            {
                UserDisplayName = "Not signed in";
                SessionStatusText = "No session";
                SessionStatusColor = Colors.Gray;
            }

            ServerUrl = _configService.GetServerUrl();
            ServerUrlEntry = ServerUrl;

            // Diagnostics
            AppVersion = $"{AppInfo.VersionString} ({AppInfo.BuildString})";
            PlatformInfo = $"{DeviceInfo.Platform} {DeviceInfo.VersionString} — {DeviceInfo.Manufacturer} {DeviceInfo.Model}";
            NetworkStatus = _connectivity.NetworkAccess.ToString();

            try
            {
                var loc = await _geolocation.GetLastKnownLocationAsync();
                GpsStatus = loc is not null ? $"Available ({loc.Accuracy:F1}m)" : "No fix";
            }
            catch
            {
                GpsStatus = "Unavailable";
            }

            GrpcStatus = _configService.IsGrpcAvailable() ? "Connected" : "Disconnected";

            // Offline DB size
            var dbPath = _configService.GetOfflineDatabasePath();
            if (File.Exists(dbPath))
            {
                var size = new FileInfo(dbPath).Length;
                OfflineDbSize = size switch
                {
                    < 1024 => $"{size} B",
                    < 1024 * 1024 => $"{size / 1024.0:F1} KB",
                    _ => $"{size / (1024.0 * 1024.0):F1} MB",
                };
            }
            else
            {
                OfflineDbSize = "No database";
            }

            // Permissions
            await RefreshPermissionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh settings");
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        var confirmed = await Shell.Current.DisplayAlert("Sign Out", "Are you sure?", "Sign Out", "Cancel");
        if (!confirmed) return;

        await _authService.LogoutAsync();
        await Shell.Current.GoToAsync("//login");
    }

    [RelayCommand]
    private void SaveServerUrl()
    {
        _configService.SetServerUrl(ServerUrlEntry);
        ServerUrl = ServerUrlEntry;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        HasConnectionTestResult = true;
        ConnectionTestResult = "Testing...";
        ConnectionTestColor = Colors.Gray;

        try
        {
            var ok = await _configService.TestConnectionAsync(ServerUrlEntry);
            ConnectionTestResult = ok ? "Connection successful" : "Connection failed";
            ConnectionTestColor = ok ? Colors.Green : Colors.Red;
        }
        catch (Exception ex)
        {
            ConnectionTestResult = $"Error: {ex.Message}";
            ConnectionTestColor = Colors.Red;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var confirmed = await Shell.Current.DisplayAlert(
            "Clear Cache", "This will remove all offline data. Unsynced changes will be lost.", "Clear", "Cancel");
        if (!confirmed) return;

        _configService.ClearOfflineCache();
        OfflineDbSize = "No database";
    }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        var diagnostics = $"""
            Honua Field Collector Diagnostics
            ==================================
            App Version: {AppVersion}
            Platform: {PlatformInfo}
            Network: {NetworkStatus}
            GPS: {GpsStatus}
            gRPC: {GrpcStatus}
            Server: {ServerUrl}
            Offline DB: {OfflineDbSize}
            Session: {SessionStatusText}
            Timestamp: {DateTime.UtcNow:O}
            """;

        var path = Path.Combine(FileSystem.CacheDirectory, $"honua-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await File.WriteAllTextAsync(path, diagnostics);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Honua Diagnostics",
            File = new ShareFile(path)
        });
    }

    [RelayCommand]
    private async Task ViewLogsAsync()
    {
        var logDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
        if (Directory.Exists(logDir))
        {
            var latest = Directory.GetFiles(logDir, "*.log")
                .OrderByDescending(f => f)
                .FirstOrDefault();

            if (latest is not null)
            {
                var content = await File.ReadAllTextAsync(latest);
                await Shell.Current.DisplayAlert("Logs", content.Length > 2000 ? content[..2000] + "..." : content, "OK");
            }
        }
    }

    [RelayCommand]
    private async Task RequestPermissionsAsync()
    {
        await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        await Permissions.RequestAsync<Permissions.Camera>();
        await Permissions.RequestAsync<Permissions.StorageRead>();
        await RefreshPermissionsAsync();
    }

    private async Task RefreshPermissionsAsync()
    {
        Permissions.Clear();
        Permissions.Add(await CheckPermission<Microsoft.Maui.ApplicationModel.Permissions.LocationWhenInUse>("Location"));
        Permissions.Add(await CheckPermission<Microsoft.Maui.ApplicationModel.Permissions.Camera>("Camera"));
        Permissions.Add(await CheckPermission<Microsoft.Maui.ApplicationModel.Permissions.StorageRead>("Storage"));
    }

    private static async Task<PermissionItemViewModel> CheckPermission<T>(string name) where T : Microsoft.Maui.ApplicationModel.Permissions.BasePermission, new()
    {
        var status = await Microsoft.Maui.ApplicationModel.Permissions.CheckStatusAsync<T>();
        return new PermissionItemViewModel
        {
            Name = name,
            StatusText = status.ToString(),
            StatusColor = status == PermissionStatus.Granted ? Colors.Green : Colors.Red,
        };
    }
}

/// <summary>
/// ViewModel for a single permission status row.
/// </summary>
public partial class PermissionItemViewModel : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private Color _statusColor = Colors.Gray;
}
