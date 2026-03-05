using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Services;

namespace Honua.Mobile.FieldCollection.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    protected readonly INavigationService NavigationService;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private bool isRefreshing;

    public BaseViewModel(INavigationService navigationService)
    {
        NavigationService = navigationService;
    }

    [RelayCommand]
    protected virtual async Task Refresh()
    {
        if (IsBusy) return;

        try
        {
            IsRefreshing = true;
            await OnRefresh();
        }
        catch (Exception ex)
        {
            await ShowError("Refresh Failed", ex.Message);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    protected virtual async Task OnRefresh()
    {
        await Task.CompletedTask;
    }

    [RelayCommand]
    protected async Task GoBack()
    {
        await NavigationService.GoBackAsync();
    }

    protected async Task ShowError(string title, string message)
    {
        await NavigationService.DisplayAlert(title, message, "OK");
    }

    protected async Task ShowMessage(string title, string message)
    {
        await NavigationService.DisplayAlert(title, message, "OK");
    }

    protected async Task<bool> ShowConfirmation(string title, string message, string accept = "Yes", string cancel = "No")
    {
        return await NavigationService.DisplayAlert(title, message, accept, cancel);
    }

    protected async Task ExecuteAsync(Func<Task> operation, string? loadingMessage = null)
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            await operation();
        }
        catch (Exception ex)
        {
            await ShowError("Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected async Task<T?> ExecuteAsync<T>(Func<Task<T>> operation, string? loadingMessage = null)
    {
        if (IsBusy) return default;

        try
        {
            IsBusy = true;
            return await operation();
        }
        catch (Exception ex)
        {
            await ShowError("Error", ex.Message);
            return default;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class MainViewModel : BaseViewModel
{
    private readonly IAuthenticationService _authService;
    private readonly ISyncService _syncService;
    private readonly IConnectivityService _connectivityService;

    [ObservableProperty]
    private string welcomeMessage = string.Empty;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private int pendingChangesCount;

    [ObservableProperty]
    private DateTime? lastSyncTime;

    public MainViewModel(
        INavigationService navigationService,
        IAuthenticationService authService,
        ISyncService syncService,
        IConnectivityService connectivityService)
        : base(navigationService)
    {
        _authService = authService;
        _syncService = syncService;
        _connectivityService = connectivityService;

        Title = "Honua Field Collection";

        // Subscribe to connectivity changes
        _connectivityService.ConnectivityChanged += OnConnectivityChanged;

        // Subscribe to auth changes
        _authService.PropertyChanged += OnAuthServicePropertyChanged;

        // Subscribe to sync changes
        _syncService.PropertyChanged += OnSyncServicePropertyChanged;

        // Initialize
        UpdateWelcomeMessage();
        IsOnline = _connectivityService.IsConnected;
        PendingChangesCount = _syncService.PendingChangesCount;
        LastSyncTime = _syncService.LastSyncTime;
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        IsOnline = isConnected;
    }

    private void OnAuthServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IAuthenticationService.CurrentUserName))
        {
            UpdateWelcomeMessage();
        }
    }

    private void OnSyncServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISyncService.PendingChangesCount))
        {
            PendingChangesCount = _syncService.PendingChangesCount;
        }
        else if (e.PropertyName == nameof(ISyncService.LastSyncTime))
        {
            LastSyncTime = _syncService.LastSyncTime;
        }
    }

    private void UpdateWelcomeMessage()
    {
        WelcomeMessage = string.IsNullOrEmpty(_authService.CurrentUserName)
            ? "Welcome to Honua Field Collection"
            : $"Welcome back, {_authService.CurrentUserName}!";
    }

    [RelayCommand]
    private async Task NavigateToMap()
    {
        await NavigationService.NavigateToAsync("//map");
    }

    [RelayCommand]
    private async Task NavigateToRecords()
    {
        await NavigationService.NavigateToAsync("//records");
    }

    [RelayCommand]
    private async Task NavigateToSync()
    {
        await NavigationService.NavigateToAsync("//sync");
    }

    [RelayCommand]
    private async Task NavigateToSettings()
    {
        await NavigationService.NavigateToAsync("//settings");
    }

    [RelayCommand]
    private async Task QuickSync()
    {
        if (!IsOnline)
        {
            await ShowError("No Connection", "Please check your internet connection and try again.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _syncService.SyncAsync();
            if (result.IsSuccess)
            {
                await ShowMessage("Sync Complete",
                    $"Sync completed successfully!\nPulled: {result.ChangesPulled} changes\nPushed: {result.ChangesPushed} changes");
            }
            else
            {
                await ShowError("Sync Failed", result.ErrorMessage ?? "Unknown error occurred");
            }
        });
    }
}