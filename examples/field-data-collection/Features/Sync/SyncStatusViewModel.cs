// Copyright (c) Honua. All rights reserved.
// Licensed under the Apache License 2.0. See LICENSE in the project root.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace HonuaFieldCollector.Features.Sync;

/// <summary>
/// ViewModel for the Sync Center screen. Shows sync queue, status,
/// pending/conflict counts, and history.
/// </summary>
public partial class SyncStatusViewModel : ObservableObject
{
    private readonly ISyncService _syncService;
    private readonly IConflictResolutionService _conflictService;
    private readonly IConnectivity _connectivity;
    private readonly ILogger<SyncStatusViewModel> _logger;

    [ObservableProperty] private string _syncStatusTitle = "Ready";
    [ObservableProperty] private Color _syncStatusColor = Colors.Green;
    [ObservableProperty] private string _lastSyncText = "Never synced";
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _conflictCount;
    [ObservableProperty] private Color _conflictCountColor = Colors.Black;
    [ObservableProperty] private int _uploadedCount;
    [ObservableProperty] private bool _hasConflicts;
    [ObservableProperty] private bool _canSync;

    public ObservableCollection<SyncItemViewModel> SyncItems { get; } = [];

    public SyncStatusViewModel(
        ISyncService syncService,
        IConflictResolutionService conflictService,
        IConnectivity connectivity,
        ILogger<SyncStatusViewModel> logger)
    {
        _syncService = syncService;
        _conflictService = conflictService;
        _connectivity = connectivity;
        _logger = logger;

        _connectivity.ConnectivityChanged += OnConnectivityChanged;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var status = await _syncService.GetSyncStatusAsync();

            PendingCount = status.PendingOperations;
            ConflictCount = status.ConflictCount;
            UploadedCount = status.CompletedOperations;
            HasConflicts = status.ConflictCount > 0;
            ConflictCountColor = HasConflicts ? Colors.Red : Colors.Black;
            CanSync = _connectivity.NetworkAccess == NetworkAccess.Internet && !IsSyncing;

            if (status.LastSyncTime.HasValue)
                LastSyncText = $"Last sync: {status.LastSyncTime.Value:g}";

            SyncStatusTitle = PendingCount > 0 ? $"{PendingCount} pending" : "Up to date";
            SyncStatusColor = HasConflicts ? Colors.Orange : (PendingCount > 0 ? Colors.Blue : Colors.Green);

            // Populate queue items
            SyncItems.Clear();
            foreach (var op in status.Operations)
            {
                SyncItems.Add(new SyncItemViewModel
                {
                    Title = op.Title,
                    Subtitle = op.OperationType,
                    TimestampText = op.CreatedAt.ToString("g"),
                    StatusIcon = op.Status switch
                    {
                        "pending" => "clock",
                        "conflict" => "warning",
                        "uploaded" => "check",
                        "error" => "error",
                        _ => "help"
                    },
                    HasAction = op.Status == "conflict",
                    ActionText = op.Status == "conflict" ? "Resolve" : string.Empty,
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh sync status");
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        SyncStatusTitle = "Syncing...";
        SyncStatusColor = Colors.Blue;

        try
        {
            await _syncService.SyncNowAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed");
            SyncStatusTitle = "Sync failed";
            SyncStatusColor = Colors.Red;
        }
        finally
        {
            IsSyncing = false;
        }
    }

    [RelayCommand]
    private async Task ResolveConflictsAsync()
    {
        await Shell.Current.GoToAsync("conflicts");
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CanSync = e.NetworkAccess == NetworkAccess.Internet && !IsSyncing;
        });
    }
}

/// <summary>
/// ViewModel for a single sync queue item.
/// </summary>
public partial class SyncItemViewModel : ObservableObject
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty] private string _timestampText = string.Empty;
    [ObservableProperty] private string _statusIcon = string.Empty;
    [ObservableProperty] private bool _hasAction;
    [ObservableProperty] private string _actionText = string.Empty;

    [RelayCommand]
    private async Task ActionAsync()
    {
        // Navigate to conflict resolution for this item
    }
}
