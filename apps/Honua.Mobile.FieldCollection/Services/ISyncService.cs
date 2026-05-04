using System.ComponentModel;

namespace Honua.Mobile.FieldCollection.Services;

public interface ISyncService : INotifyPropertyChanged
{
    bool IsSyncing { get; }
    DateTime? LastSyncTime { get; }
    SyncStatus Status { get; }
    int PendingChangesCount { get; }

    Task<SyncResult> SyncAsync();
    Task<SyncResult> PullChangesAsync();
    Task<SyncResult> PushChangesAsync();
    Task CancelSyncAsync();
    Task<IEnumerable<ConflictInfo>> GetConflictsAsync();
    Task<bool> ResolveConflictAsync(string conflictId, ConflictResolution resolution);
}

public enum SyncStatus
{
    Idle,
    Syncing,
    PullingChanges,
    PushingChanges,
    ResolvingConflicts,
    Error,
    Cancelled
}

public class SyncResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int ChangesPulled { get; set; }
    public int ChangesPushed { get; set; }
    public int ConflictsDetected { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime CompletedAt { get; set; }

    public static SyncResult Success(int pulled, int pushed, int conflicts, TimeSpan duration) =>
        new()
        {
            IsSuccess = true,
            ChangesPulled = pulled,
            ChangesPushed = pushed,
            ConflictsDetected = conflicts,
            Duration = duration,
            CompletedAt = DateTime.UtcNow
        };

    public static SyncResult Failure(string errorMessage) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, CompletedAt = DateTime.UtcNow };
}

public class ConflictInfo
{
    public string Id { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string FeatureId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string LayerName { get; set; } = string.Empty;
    public ConflictType Type { get; set; }
    public DateTime DetectedAt { get; set; }
    public object? LocalVersion { get; set; }
    public object? ServerVersion { get; set; }
    public string? FailureReason { get; set; }
    public string? RedactedLocalVersion { get; set; }
    public string? RedactedServerVersion { get; set; }
    public IReadOnlyList<ConflictResolution> AvailableResolutions { get; set; } =
    [
        ConflictResolution.AcceptLocal,
        ConflictResolution.AcceptServer,
        ConflictResolution.Manual
    ];

    public string ConflictDescription => Type switch
    {
        ConflictType.UpdateUpdate => "Local and server versions were both updated.",
        ConflictType.UpdateDelete => "Local changes conflict with a server delete.",
        ConflictType.DeleteUpdate => "A local delete conflicts with server changes.",
        ConflictType.GeometryOverlap => "Geometry overlaps with an existing feature.",
        _ => "Sync conflict requires review."
    };
}

public enum ConflictType
{
    UpdateUpdate,
    UpdateDelete,
    DeleteUpdate,
    GeometryOverlap
}

public enum ConflictResolution
{
    AcceptLocal,
    AcceptServer,
    Merge,
    Manual
}

public class SyncService : ISyncService
{
    private bool _isSyncing;
    private DateTime? _lastSyncTime;
    private SyncStatus _status = SyncStatus.Idle;
    private int _pendingChangesCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSyncing
    {
        get => _isSyncing;
        private set
        {
            _isSyncing = value;
            OnPropertyChanged();
        }
    }

    public DateTime? LastSyncTime
    {
        get => _lastSyncTime;
        private set
        {
            _lastSyncTime = value;
            OnPropertyChanged();
        }
    }

    public SyncStatus Status
    {
        get => _status;
        private set
        {
            _status = value;
            OnPropertyChanged();
        }
    }

    public int PendingChangesCount
    {
        get => _pendingChangesCount;
        private set
        {
            _pendingChangesCount = value;
            OnPropertyChanged();
        }
    }

    public async Task<SyncResult> SyncAsync()
    {
        if (IsSyncing)
            return SyncResult.Failure("Sync already in progress");

        await Task.CompletedTask;
        Status = SyncStatus.Error;
        return SyncResult.Failure("Sync service is not configured.");
    }

    public Task<SyncResult> PullChangesAsync()
    {
        Status = SyncStatus.Error;
        return Task.FromResult(SyncResult.Failure("Sync service is not configured."));
    }

    public Task<SyncResult> PushChangesAsync()
    {
        Status = SyncStatus.Error;
        return Task.FromResult(SyncResult.Failure("Sync service is not configured."));
    }

    public async Task CancelSyncAsync()
    {
        Status = SyncStatus.Cancelled;
        IsSyncing = false;
        await Task.CompletedTask;
    }

    public Task<IEnumerable<ConflictInfo>> GetConflictsAsync()
    {
        return Task.FromResult<IEnumerable<ConflictInfo>>(Array.Empty<ConflictInfo>());
    }

    public Task<bool> ResolveConflictAsync(string conflictId, ConflictResolution resolution)
    {
        return Task.FromResult(false);
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
