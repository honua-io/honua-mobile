using System.Diagnostics.Metrics;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.Tests;

public sealed class OfflineSyncEngineTests : IDisposable
{
    private readonly string _databasePath;

    public OfflineSyncEngineTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"honua-sync-{Guid.NewGuid():N}.gpkg");
    }

    [Fact]
    public async Task SyncAsync_ClientWins_RetriesConflictWithForceWrite()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "conflict-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{\"id\":1}",
            Priority = 1,
        });

        var uploader = new ConflictThenSuccessUploader();
        var engine = new OfflineSyncEngine(
            store,
            uploader,
            new OfflineSyncEngineOptions { ConflictStrategy = SyncConflictStrategy.ClientWins });

        var result = await engine.SyncAsync();
        var remaining = await store.CountPendingAsync();

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, remaining);
        Assert.Equal(2, uploader.CallCount);
    }

    [Fact]
    public async Task SyncAsync_ManualReview_LeavesConflictAsFailed()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "manual-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 1,
        });

        var uploader = new AlwaysConflictUploader();
        var engine = new OfflineSyncEngine(
            store,
            uploader,
            new OfflineSyncEngineOptions { ConflictStrategy = SyncConflictStrategy.ManualReview });

        var result = await engine.SyncAsync();
        var pending = await store.GetPendingAsync(10);
        var operationState = await ReadOperationStateAsync("manual-op");

        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Empty(pending);
        Assert.NotNull(operationState);
        Assert.Equal("failed", operationState!.Value.Status);
        Assert.Equal(1, operationState.Value.AttemptCount);
    }

    [Fact]
    public async Task SyncAsync_ConflictPolicyRules_HandleConflictsAcrossThreeLayers()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();

        await store.EnqueueAsync(CreateOperation("critical-update", "critical", OfflineOperationType.Update));
        await store.EnqueueAsync(CreateOperation("reference-delete", "reference", OfflineOperationType.Delete));
        await store.EnqueueAsync(CreateOperation("assets-add", "assets", OfflineOperationType.Add));

        var uploader = new AlwaysConflictRecordingUploader();
        var engine = new OfflineSyncEngine(
            store,
            uploader,
            new OfflineSyncEngineOptions
            {
                BatchSize = 10,
                ConflictStrategy = SyncConflictStrategy.ManualReview,
                ConflictPolicyRules =
                [
                    new SyncConflictPolicyRule
                    {
                        LayerKey = "critical",
                        OperationType = OfflineOperationType.Update,
                        Strategy = SyncConflictStrategy.ClientWins,
                    },
                    new SyncConflictPolicyRule
                    {
                        LayerKey = "reference",
                        Strategy = SyncConflictStrategy.ServerWins,
                    },
                ],
            });

        var result = await engine.SyncAsync();
        var manualState = await ReadOperationStateAsync("assets-add");

        Assert.Equal(3, result.Loaded);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Null(await ReadOperationStateAsync("critical-update"));
        Assert.Null(await ReadOperationStateAsync("reference-delete"));
        Assert.NotNull(manualState);
        Assert.Equal("failed", manualState!.Value.Status);
        Assert.Contains(uploader.Calls, call => call.OperationId == "critical-update" && call.ForceWrite);
        Assert.DoesNotContain(uploader.Calls, call => call.OperationId == "reference-delete" && call.ForceWrite);
    }

    [Fact]
    public async Task SyncAsync_NullConflictPolicyRules_UsesDefaultConflictStrategy()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();
        await store.EnqueueAsync(CreateOperation("server-wins-op", "assets", OfflineOperationType.Update));

        var engine = new OfflineSyncEngine(
            store,
            new AlwaysConflictUploader(),
            new OfflineSyncEngineOptions
            {
                ConflictStrategy = SyncConflictStrategy.ServerWins,
                ConflictPolicyRules = null!,
            });

        var result = await engine.SyncAsync();

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Null(await ReadOperationStateAsync("server-wins-op"));
    }

    [Fact]
    public async Task SyncAsync_WhenCanceled_RequeuesClaimedOperationsWithoutIncrementingAttempts()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "cancel-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 1,
        });

        var uploader = new BlockingUploader();
        var engine = new OfflineSyncEngine(store, uploader);
        using var cts = new CancellationTokenSource();

        var syncTask = engine.SyncAsync(cts.Token);
        await uploader.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await syncTask);

        var pending = await store.GetPendingAsync(10);
        Assert.Single(pending);
        Assert.Equal("cancel-op", pending[0].OperationId);
        Assert.Equal(0, pending[0].AttemptCount);
    }

    [Fact]
    public async Task SyncAsync_WhenUploaderThrows_MapsProblemAndKeepsOperationsRetryable()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "throw-op-1",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 1,
        });

        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "throw-op-2",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 2,
        });

        var engine = new OfflineSyncEngine(store, new ThrowingUploader());

        var result = await engine.SyncAsync();

        var pending = await store.GetPendingAsync(10);
        Assert.Equal(2, result.Loaded);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(2, result.Failed);
        Assert.All(result.Failures, failure =>
        {
            Assert.DoesNotContain("Grpc", failure.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Sqlite", failure.Reason, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(2, pending.Count);
        Assert.Contains(pending, operation => operation.OperationId == "throw-op-1");
        Assert.Contains(pending, operation => operation.OperationId == "throw-op-2");
    }

    [Fact]
    public async Task SyncAsync_EmitsSyncTelemetryCountersAndPendingGauge()
    {
        var measurements = new List<MetricMeasurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == MobileSyncTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            measurements.Add(new MetricMeasurement(instrument.Name, measurement, tags.ToArray()));
        });
        listener.Start();

        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();
        await store.EnqueueAsync(new OfflineEditOperation
        {
            OperationId = "telemetry-op",
            LayerKey = "assets",
            TargetCollection = "assets",
            OperationType = OfflineOperationType.Update,
            PayloadJson = "{}",
            Priority = 1,
        });

        var engine = new OfflineSyncEngine(store, new AlwaysSuccessUploader());

        var result = await engine.SyncAsync();
        listener.RecordObservableInstruments();

        Assert.Equal(1, result.Succeeded);
        Assert.Contains(measurements, measurement =>
            measurement.Name == "mobile_sync_runs_total" &&
            measurement.Value == 1 &&
            measurement.HasTag("result", "succeeded"));
        Assert.Contains(measurements, measurement =>
            measurement.Name == "mobile_pending_operations" &&
            measurement.Value == 0);
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private sealed class ConflictThenSuccessUploader : IOfflineOperationUploader
    {
        public int CallCount { get; private set; }

        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
        {
            CallCount++;

            if (!forceWrite)
            {
                return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Conflict, Message = "version conflict" });
            }

            return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success });
        }
    }

    private sealed class AlwaysConflictUploader : IOfflineOperationUploader
    {
        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
            => Task.FromResult(new UploadResult { Outcome = UploadOutcome.Conflict, Message = "conflict" });
    }

    private sealed class AlwaysConflictRecordingUploader : IOfflineOperationUploader
    {
        public List<(string OperationId, bool ForceWrite)> Calls { get; } = [];

        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
        {
            Calls.Add((operation.OperationId, forceWrite));
            return Task.FromResult(forceWrite
                ? new UploadResult { Outcome = UploadOutcome.Success }
                : new UploadResult { Outcome = UploadOutcome.Conflict, Message = "conflict" });
        }
    }

    private sealed class AlwaysSuccessUploader : IOfflineOperationUploader
    {
        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
            => Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success });
    }

    private sealed class BlockingUploader : IOfflineOperationUploader
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new UploadResult { Outcome = UploadOutcome.Success };
        }
    }

    private sealed class ThrowingUploader : IOfflineOperationUploader
    {
        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
            => throw new InvalidOperationException("Grpc.Core.RpcException: Status(StatusCode=\"Unavailable\")");
    }

    private static OfflineEditOperation CreateOperation(
        string operationId,
        string layerKey,
        OfflineOperationType operationType)
        => new()
        {
            OperationId = operationId,
            LayerKey = layerKey,
            TargetCollection = layerKey,
            OperationType = operationType,
            PayloadJson = "{}",
            Priority = 1,
        };

    private sealed record MetricMeasurement(
        string Name,
        long Value,
        KeyValuePair<string, object?>[] Tags)
    {
        public bool HasTag(string name, string value)
            => Tags.Any(tag =>
                tag.Key == name &&
                string.Equals(tag.Value as string, value, StringComparison.Ordinal));
    }

    private async Task<(int AttemptCount, string Status)?> ReadOperationStateAsync(string operationId)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT attempt_count, status
FROM honua_sync_queue
WHERE operation_id = $operation_id
LIMIT 1;
";
        command.Parameters.AddWithValue("$operation_id", operationId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.GetInt32(0), reader.GetString(1));
    }
}
