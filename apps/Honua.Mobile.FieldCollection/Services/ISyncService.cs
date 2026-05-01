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
    public string FeatureId { get; set; } = string.Empty;
    public string LayerName { get; set; } = string.Empty;
    public ConflictType Type { get; set; }
    public DateTime DetectedAt { get; set; }
    public object? LocalVersion { get; set; }
    public object? ServerVersion { get; set; }

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

        try
        {
            IsSyncing = true;
            Status = SyncStatus.Syncing;
            var startTime = DateTime.UtcNow;

            // Simulate pull changes
            Status = SyncStatus.PullingChanges;
            await Task.Delay(2000); // Simulate network operation
            var pulled = Random.Shared.Next(0, 10);

            // Simulate push changes
            Status = SyncStatus.PushingChanges;
            await Task.Delay(1500); // Simulate network operation
            var pushed = PendingChangesCount;
            PendingChangesCount = 0;

            // Simulate conflict detection
            var conflicts = Random.Shared.Next(0, 3);
            if (conflicts > 0)
            {
                Status = SyncStatus.ResolvingConflicts;
                await Task.Delay(1000); // Simulate conflict resolution
            }

            var duration = DateTime.UtcNow - startTime;
            LastSyncTime = DateTime.UtcNow;
            Status = SyncStatus.Idle;

            return SyncResult.Success(pulled, pushed, conflicts, duration);
        }
        catch (Exception ex)
        {
            Status = SyncStatus.Error;
            return SyncResult.Failure(ex.Message);
        }
        finally
        {
            IsSyncing = false;
        }
    }

    public async Task<SyncResult> PullChangesAsync()
    {
        // Implementation would use the gRPC PullChanges service
        await Task.Delay(1000);
        return SyncResult.Success(Random.Shared.Next(0, 20), 0, 0, TimeSpan.FromSeconds(1));
    }

    public async Task<SyncResult> PushChangesAsync()
    {
        // Implementation would use the gRPC PushChanges service
        await Task.Delay(1000);
        var pushed = PendingChangesCount;
        PendingChangesCount = 0;
        return SyncResult.Success(0, pushed, 0, TimeSpan.FromSeconds(1));
    }

    public async Task CancelSyncAsync()
    {
        Status = SyncStatus.Cancelled;
        IsSyncing = false;
        await Task.CompletedTask;
    }

    public async Task<IEnumerable<ConflictInfo>> GetConflictsAsync()
    {
        await Task.Delay(100);
        // Return demo conflicts
        return new[]
        {
            new ConflictInfo
            {
                Id = "conflict_1",
                FeatureId = "feature_123",
                LayerName = "Points of Interest",
                Type = ConflictType.UpdateUpdate,
                DetectedAt = DateTime.UtcNow.AddMinutes(-5)
            }
        };
    }

    public async Task<bool> ResolveConflictAsync(string conflictId, ConflictResolution resolution)
    {
        await Task.Delay(500);
        return true; // Simulate successful resolution
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
