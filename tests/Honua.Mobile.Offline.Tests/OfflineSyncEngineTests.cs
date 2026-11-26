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
    public async Task SyncAsync_SuccessRetainsServerIdentityAfterRestart()
    {
        var options = new GeoPackageSyncStoreOptions { DatabasePath = _databasePath };
        var store = new GeoPackageSyncStore(options);
        await store.InitializeAsync();
        await store.EnqueueAsync(CreateOperation("saved-record", "assets", OfflineOperationType.Add));

        var engine = new OfflineSyncEngine(store, new AlwaysSuccessUploader());
        Assert.Equal(1, (await engine.SyncAsync()).Succeeded);

        var reopened = new GeoPackageSyncStore(options);
        Assert.Equal(0, await reopened.CountPendingAsync());
        Assert.Equal(["server-42"], (await reopened.GetUploadAcknowledgementAsync("saved-record"))!.ServerFeatureIds);
    }

    [Theory]
    [InlineData(UploadOutcome.Success, false)]
    [InlineData(UploadOutcome.RetryableFailure, false)]
    [InlineData(UploadOutcome.Conflict, false)]
    [InlineData(UploadOutcome.Success, true)]
    public async Task SyncAsync_LateWorkerCannotCompleteOrReleaseReclaimedEdit(UploadOutcome staleOutcome, bool cancel)
    {
        var options = new GeoPackageSyncStoreOptions
        {
            DatabasePath = _databasePath,
            InProgressLeaseTimeout = TimeSpan.Zero,
            TimeProvider = new FixedTimeProvider(),
        };
        var firstStore = new GeoPackageSyncStore(options);
        await firstStore.InitializeAsync();
        const string operationId = "reclaimed-edit";
        await firstStore.EnqueueAsync(CreateOperation(operationId, "assets", OfflineOperationType.Add));

        var firstUploader = new GatedUploader();
        var secondUploader = new GatedUploader();
        var firstEngine = new OfflineSyncEngine(firstStore, firstUploader,
            new OfflineSyncEngineOptions { ConflictStrategy = SyncConflictStrategy.ServerWins });
        var secondStore = new GeoPackageSyncStore(options);
        var secondEngine = new OfflineSyncEngine(secondStore, secondUploader);
        using var cancelled = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var staleRun = firstEngine.SyncAsync(cancelled.Token);
        await firstUploader.Started.Task.WaitAsync(timeout.Token);
        var currentRun = secondEngine.SyncAsync(timeout.Token);
        await secondUploader.Started.Task.WaitAsync(timeout.Token);

        if (cancel)
        {
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => staleRun.WaitAsync(timeout.Token));
            Assert.True(staleRun.IsCanceled);
        }
        else
        {
            firstUploader.Result.SetResult(new UploadResult { Outcome = staleOutcome, ServerFeatureIds = ["stale-id"] });
            var staleResult = await staleRun.WaitAsync(timeout.Token);
            Assert.Equal(0, staleResult.Succeeded);
            Assert.Equal(0, staleResult.Failed);
        }

        Assert.Null(await firstStore.GetUploadAcknowledgementAsync(operationId, timeout.Token));
        Assert.Equal(0, await firstStore.CountPendingAsync(timeout.Token));
        secondUploader.Result.SetResult(new UploadResult { Outcome = UploadOutcome.Success, ServerFeatureIds = ["current-id"] });
        Assert.Equal(1, (await currentRun).Succeeded);
        var acknowledgement = await secondStore.GetUploadAcknowledgementAsync(operationId, timeout.Token);
        Assert.NotNull(acknowledgement);
        Assert.Equal(["current-id"], acknowledgement.ServerFeatureIds);
    }

    [Fact]
    public async Task SyncAsync_DecoratorWithoutAcknowledgementSupportStillCompletesUpload()
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();
        await store.EnqueueAsync(CreateOperation("wrapped-record", "assets", OfflineOperationType.Add));
        await store.EnqueueAsync(CreateOperation("wrapped-record-2", "assets", OfflineOperationType.Add));

        var result = await new OfflineSyncEngine(new ForwardingStore(store), new AlwaysSuccessUploader()).SyncAsync();

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, await store.CountPendingAsync());
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
        Assert.Equal(["server-42"], (await store.GetUploadAcknowledgementAsync("conflict-op"))!.ServerFeatureIds);
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
    public async Task SyncAsync_ServerWins_DiscardsLocalEditWithoutReUpload()
    {
        // ServerWins is intentionally discard-only: the conflicting local edit is
        // dropped (marked succeeded) and the server value stays authoritative. The
        // engine must NOT issue a force-write re-upload, and there is no local
        // re-pull/overwrite step here. This test pins that documented divergence so a
        // future change to ServerWins semantics is a deliberate, reviewed decision.
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions { DatabasePath = _databasePath });
        await store.InitializeAsync();
        await store.EnqueueAsync(CreateOperation("server-wins-discard", "assets", OfflineOperationType.Update));

        var uploader = new AlwaysConflictRecordingUploader();
        var engine = new OfflineSyncEngine(
            store,
            uploader,
            new OfflineSyncEngineOptions { ConflictStrategy = SyncConflictStrategy.ServerWins });

        var result = await engine.SyncAsync();

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(0, result.Failed);
        // The local edit was discarded, not retried/forced.
        Assert.Null(await ReadOperationStateAsync("server-wins-discard"));
        Assert.Null(await store.GetUploadAcknowledgementAsync("server-wins-discard"));
        Assert.Single(uploader.Calls);
        Assert.DoesNotContain(uploader.Calls, call => call.ForceWrite);
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
            measurement.HasTag("result", "succeeded") &&
            measurement.HasTag("service_id", "assets"));
        Assert.Contains(measurements, measurement =>
            measurement.Name == "mobile_pending_operations" &&
            measurement.Value == 0);
    }

    [Fact]
    public async Task SyncAsync_MultipleTargetLayers_EmitsMultipleServiceIdTag()
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
        await store.EnqueueAsync(CreateOperation("multi-op-1", "assets", OfflineOperationType.Update));
        await store.EnqueueAsync(CreateOperation("multi-op-2", "inspections", OfflineOperationType.Update));

        var engine = new OfflineSyncEngine(store, new AlwaysSuccessUploader());

        var result = await engine.SyncAsync();

        Assert.Equal(2, result.Succeeded);
        Assert.Contains(measurements, measurement =>
            measurement.Name == "mobile_sync_runs_total" &&
            measurement.Value == 1 &&
            measurement.HasTag("result", "succeeded") &&
            measurement.HasTag("service_id", "multiple"));
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

            return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success, ServerFeatureIds = ["server-42"] });
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
                ? new UploadResult { Outcome = UploadOutcome.Success, ServerFeatureIds = ["server-42"] }
                : new UploadResult { Outcome = UploadOutcome.Conflict, Message = "conflict" });
        }
    }

    // A decorator written before durable acknowledgements: it forwards every abstract member and
    // relies on the interface defaults for the rest, including MarkUploadedAsync.
    private sealed class ForwardingStore(IGeoPackageSyncStore inner) : IGeoPackageSyncStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => inner.InitializeAsync(ct);
        public Task EnqueueAsync(OfflineEditOperation operation, CancellationToken ct = default) => inner.EnqueueAsync(operation, ct);
        public Task<IReadOnlyList<OfflineEditOperation>> GetPendingAsync(int maxCount, CancellationToken ct = default) => inner.GetPendingAsync(maxCount, ct);
        public Task<IReadOnlyList<OfflineEditOperation>> GetPendingByLayerKeyPrefixAsync(string layerKeyPrefix, int maxCount, CancellationToken ct = default)
            => inner.GetPendingByLayerKeyPrefixAsync(layerKeyPrefix, maxCount, ct);
        public Task<int> CountPendingAsync(CancellationToken ct = default) => inner.CountPendingAsync(ct);
        public Task MarkSucceededAsync(string operationId, CancellationToken ct = default) => inner.MarkSucceededAsync(operationId, ct);
        public Task MarkPendingAsync(string operationId, CancellationToken ct = default) => inner.MarkPendingAsync(operationId, ct);
        public Task MarkFailedAsync(string operationId, string failureReason, bool retryable, CancellationToken ct = default)
            => inner.MarkFailedAsync(operationId, failureReason, retryable, ct);
        public Task SetSyncCursorAsync(string cursorKey, string cursorValue, CancellationToken ct = default) => inner.SetSyncCursorAsync(cursorKey, cursorValue, ct);
        public Task<string?> GetSyncCursorAsync(string cursorKey, CancellationToken ct = default) => inner.GetSyncCursorAsync(cursorKey, ct);
        public Task UpsertMapAreaAsync(MapAreaPackage mapArea, CancellationToken ct = default) => inner.UpsertMapAreaAsync(mapArea, ct);
        public Task<IReadOnlyList<MapAreaPackage>> ListMapAreasAsync(CancellationToken ct = default) => inner.ListMapAreasAsync(ct);
        public Task UpsertScenePackageAsync(ScenePackageRecord scenePackage, CancellationToken ct = default) => inner.UpsertScenePackageAsync(scenePackage, ct);
        public Task<IReadOnlyList<ScenePackageRecord>> ListScenePackagesAsync(CancellationToken ct = default) => inner.ListScenePackagesAsync(ct);
        public Task DeleteScenePackageAsync(string packageId, CancellationToken ct = default) => inner.DeleteScenePackageAsync(packageId, ct);
        public Task UpsertFeatureAsync(string layerKey, string featureJson, CancellationToken ct = default) => inner.UpsertFeatureAsync(layerKey, featureJson, ct);
        public Task DeleteFeatureAsync(string layerKey, long objectId, CancellationToken ct = default) => inner.DeleteFeatureAsync(layerKey, objectId, ct);
        public Task<IReadOnlyList<string>> GetFeaturesAsync(string layerKey, CancellationToken ct = default) => inner.GetFeaturesAsync(layerKey, ct);
    }

    private sealed class AlwaysSuccessUploader : IOfflineOperationUploader
    {
        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
            => Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success, ServerFeatureIds = ["server-42"] });
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private sealed class GatedUploader : IOfflineOperationUploader
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<UploadResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
        {
            Started.SetResult();
            return Result.Task.WaitAsync(ct);
        }
    }

    private sealed class BlockingUploader : IOfflineOperationUploader
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new UploadResult { Outcome = UploadOutcome.Success, ServerFeatureIds = ["server-42"] };
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
