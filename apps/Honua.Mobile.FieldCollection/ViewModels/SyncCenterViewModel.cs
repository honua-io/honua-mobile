using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using System.Collections.ObjectModel;

namespace Honua.Mobile.FieldCollection.ViewModels;

public partial class SyncCenterViewModel : BaseViewModel
{
    private readonly ISyncService _syncService;
    private readonly IConnectivityService _connectivityService;
    private readonly IAuthenticationService _authService;

    [ObservableProperty]
    private bool isSyncing;

    [ObservableProperty]
    private SyncStatus syncStatus;

    [ObservableProperty]
    private int pendingChangesCount;

    [ObservableProperty]
    private DateTime? lastSyncTime;

    [ObservableProperty]
    private bool isOnline;

    [ObservableProperty]
    private SyncStatistics? lastSyncStatistics;

    [ObservableProperty]
    private string syncStatusMessage = "Ready to sync";

    [ObservableProperty]
    private double syncProgress;

    public ObservableCollection<ConflictInfo> ActiveConflicts { get; } = new();
    public ObservableCollection<SyncHistoryItem> SyncHistory { get; } = new();

    public SyncCenterViewModel(
        INavigationService navigationService,
        ISyncService syncService,
        IConnectivityService connectivityService,
        IAuthenticationService authService)
        : base(navigationService)
    {
        _syncService = syncService;
        _connectivityService = connectivityService;
        _authService = authService;

        Title = "Sync Center";

        // Subscribe to service events
        _syncService.PropertyChanged += OnSyncServicePropertyChanged;
        _connectivityService.ConnectivityChanged += OnConnectivityChanged;

        // Initialize properties
        UpdateFromSyncService();
        IsOnline = _connectivityService.IsConnected;
    }

    private void OnSyncServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        UpdateFromSyncService();
    }

    private void OnConnectivityChanged(object? sender, bool isConnected)
    {
        IsOnline = isConnected;
        UpdateSyncStatusMessage();
    }

    private void UpdateFromSyncService()
    {
        IsSyncing = _syncService.IsSyncing;
        SyncStatus = _syncService.Status;
        PendingChangesCount = _syncService.PendingChangesCount;
        LastSyncTime = _syncService.LastSyncTime;

        UpdateSyncStatusMessage();
    }

    private void UpdateSyncStatusMessage()
    {
        if (!IsOnline)
        {
            SyncStatusMessage = "Offline - sync unavailable";
        }
        else if (!_authService.IsAuthenticated)
        {
            SyncStatusMessage = "Not authenticated";
        }
        else
        {
            SyncStatusMessage = SyncStatus switch
            {
                SyncStatus.Idle when PendingChangesCount > 0 => $"{PendingChangesCount} changes pending",
                SyncStatus.Idle => "All changes synced",
                SyncStatus.Syncing => "Synchronizing...",
                SyncStatus.PullingChanges => "Downloading changes from server...",
                SyncStatus.PushingChanges => "Uploading changes to server...",
                SyncStatus.ResolvingConflicts => "Resolving conflicts...",
                SyncStatus.Error => "Sync error occurred",
                SyncStatus.Cancelled => "Sync was cancelled",
                _ => "Unknown status"
            };
        }
    }

    protected override async Task OnRefresh()
    {
        await LoadConflicts();
        await LoadSyncHistory();
    }

    [RelayCommand]
    private async Task StartFullSync()
    {
        if (IsSyncing)
        {
            await ShowMessage("Sync In Progress", "A sync operation is already running.");
            return;
        }

        if (!IsOnline)
        {
            await ShowError("No Connection", "Please check your internet connection and try again.");
            return;
        }

        if (!_authService.IsAuthenticated)
        {
            await ShowError("Not Authenticated", "Please sign in before syncing.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _syncService.SyncAsync();

            if (result.IsSuccess)
            {
                LastSyncStatistics = new SyncStatistics
                {
                    LastSyncTime = result.CompletedAt,
                    FeaturesPulled = result.ChangesPulled,
                    FeaturesPushed = result.ChangesPushed,
                    ConflictsDetected = result.ConflictsDetected,
                    LastSyncDuration = result.Duration
                };

                // Add to history
                SyncHistory.Insert(0, new SyncHistoryItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = SyncType.Full,
                    StartTime = result.CompletedAt.Subtract(result.Duration),
                    EndTime = result.CompletedAt,
                    Status = SyncHistoryStatus.Completed,
                    ChangesPulled = result.ChangesPulled,
                    ChangesPushed = result.ChangesPushed,
                    ConflictsCount = result.ConflictsDetected
                });

                await ShowMessage("Sync Complete",
                    $"Sync completed successfully!\n" +
                    $"Downloaded: {result.ChangesPulled} changes\n" +
                    $"Uploaded: {result.ChangesPushed} changes\n" +
                    $"Duration: {result.Duration:mm\\:ss}");

                // Refresh conflicts if any were detected
                if (result.ConflictsDetected > 0)
                {
                    await LoadConflicts();
                }
            }
            else
            {
                // Add failed sync to history
                SyncHistory.Insert(0, new SyncHistoryItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = SyncType.Full,
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow,
                    Status = SyncHistoryStatus.Failed,
                    ErrorMessage = result.ErrorMessage
                });

                await ShowError("Sync Failed", result.ErrorMessage ?? "An unknown error occurred during sync.");
            }
        });
    }

    [RelayCommand]
    private async Task PullChangesOnly()
    {
        if (!IsOnline || !_authService.IsAuthenticated)
        {
            await ShowError("Cannot Pull", "Please check your connection and authentication.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _syncService.PullChangesAsync();
            if (result.IsSuccess)
            {
                await ShowMessage("Pull Complete", $"Downloaded {result.ChangesPulled} changes from server.");
            }
            else
            {
                await ShowError("Pull Failed", result.ErrorMessage ?? "Unknown error occurred.");
            }
        });
    }

    [RelayCommand]
    private async Task PushChangesOnly()
    {
        if (!IsOnline || !_authService.IsAuthenticated)
        {
            await ShowError("Cannot Push", "Please check your connection and authentication.");
            return;
        }

        if (PendingChangesCount == 0)
        {
            await ShowMessage("No Changes", "There are no pending changes to upload.");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var result = await _syncService.PushChangesAsync();
            if (result.IsSuccess)
            {
                await ShowMessage("Push Complete", $"Uploaded {result.ChangesPushed} changes to server.");
            }
            else
            {
                await ShowError("Push Failed", result.ErrorMessage ?? "Unknown error occurred.");
            }
        });
    }

    [RelayCommand]
    private async Task CancelSync()
    {
        if (!IsSyncing)
            return;

        var confirmed = await ShowConfirmation("Cancel Sync",
            "Are you sure you want to cancel the current sync operation?",
            "Yes", "No");

        if (confirmed)
        {
            await _syncService.CancelSyncAsync();
        }
    }

    [RelayCommand]
    private async Task LoadConflicts()
    {
        await ExecuteAsync(async () =>
        {
            var conflicts = await _syncService.GetConflictsAsync();

            ActiveConflicts.Clear();
            foreach (var conflict in conflicts)
            {
                ActiveConflicts.Add(conflict);
            }
        });
    }

    [RelayCommand]
    private async Task ResolveConflict(ConflictInfo conflict)
    {
        var resolution = await NavigationService.DisplayActionSheet(
            $"Resolve Conflict - {conflict.LayerName}",
            "Cancel",
            null,
            "Accept Local Changes",
            "Accept Server Changes",
            "Manual Resolution");

        if (resolution == "Cancel")
            return;

        ConflictResolution resolutionType = resolution switch
        {
            "Accept Local Changes" => ConflictResolution.AcceptLocal,
            "Accept Server Changes" => ConflictResolution.AcceptServer,
            "Manual Resolution" => ConflictResolution.Manual,
            _ => ConflictResolution.Manual
        };

        if (resolutionType == ConflictResolution.Manual)
        {
            // Navigate to detailed conflict resolution page
            var parameters = new Dictionary<string, object> { ["conflictId"] = conflict.Id };
            await NavigationService.NavigateToAsync("sync/conflict-resolution", parameters);
        }
        else
        {
            await ExecuteAsync(async () =>
            {
                var success = await _syncService.ResolveConflictAsync(conflict.Id, resolutionType);
                if (success)
                {
                    ActiveConflicts.Remove(conflict);
                    await ShowMessage("Conflict Resolved", "The conflict has been resolved successfully.");
                }
                else
                {
                    await ShowError("Resolution Failed", "Failed to resolve the conflict. Please try again.");
                }
            });
        }
    }

    [RelayCommand]
    private async Task ViewSyncHistory()
    {
        await NavigationService.NavigateToAsync("sync/sync-history");
    }

    [RelayCommand]
    private async Task LoadSyncHistory()
    {
        await ExecuteAsync(async () =>
        {
            // In a real implementation, this would load from local storage
            await Task.Delay(200);

            if (SyncHistory.Count == 0)
            {
                // Add some demo history items
                SyncHistory.Add(new SyncHistoryItem
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = SyncType.Full,
                    StartTime = DateTime.Now.AddHours(-2),
                    EndTime = DateTime.Now.AddHours(-2).AddMinutes(2),
                    Status = SyncHistoryStatus.Completed,
                    ChangesPulled = 5,
                    ChangesPushed = 3,
                    ConflictsCount = 0
                });
            }
        });
    }
}

public class SyncHistoryItem
{
    public string Id { get; set; } = string.Empty;
    public SyncType Type { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public SyncHistoryStatus Status { get; set; }
    public int ChangesPulled { get; set; }
    public int ChangesPushed { get; set; }
    public int ConflictsCount { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;
}

public enum SyncType
{
    Full,
    PullOnly,
    PushOnly,
    ConflictResolution
}

public enum SyncHistoryStatus
{
    InProgress,
    Completed,
    Failed,
    Cancelled
}