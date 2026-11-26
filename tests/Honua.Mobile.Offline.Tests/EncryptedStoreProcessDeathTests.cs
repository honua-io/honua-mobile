// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Diagnostics;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Microsoft.Data.Sqlite;

namespace Honua.Mobile.Offline.Tests;

public sealed class EncryptedStoreProcessDeathTests : IDisposable
{
    private const string FixtureVariable = "HONUA_OFFLINE_CRASH_FIXTURE_DIR";
    private const string CheckpointVariable = "HONUA_OFFLINE_CRASH_CHECKPOINT";
    private const string TestKey = "test-only-crash-fixture-key";
    private const string Feature = """{"attributes":{"OBJECTID":42,"name":"saved offline"}}""";
    private const string OperationId = "process-death-edit";
    private const string ServerId = "server-42";
    private static readonly DateTimeOffset InitialTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"honua-crash-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("queue_schema_before_commit", 0L)]
    [InlineData("queue_schema_committed", 4L)]
    public async Task QueueSchemaUpgradeRecoversAfterProcessDeath(string checkpoint, long expectedColumns)
    {
        var store = await InitializeAsync();
        await SaveEditAsync(store);
        await using (var connection = await OpenFixtureAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DROP INDEX idx_honua_features_layer_expires;
                ALTER TABLE honua_features DROP COLUMN expires_at_utc;
                ALTER TABLE honua_sync_queue DROP COLUMN claimed_at_utc;
                ALTER TABLE honua_sync_queue DROP COLUMN claim_token;
                ALTER TABLE honua_sync_queue DROP COLUMN replica_id;
                ALTER TABLE honua_sync_queue DROP COLUMN replica_base_generation;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await RunCrashAsync(checkpoint);

        await using (var connection = await OpenFixtureAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT count(*) FROM pragma_table_info('honua_sync_queue')
                WHERE name IN ('claimed_at_utc', 'claim_token', 'replica_id', 'replica_base_generation');
                """;
            Assert.Equal(expectedColumns, await command.ExecuteScalarAsync());
            command.CommandText = "SELECT count(*) FROM pragma_index_info('idx_honua_features_layer_expires') WHERE name = 'expires_at_utc';";
            Assert.Equal(expectedColumns == 0 ? 0L : 1L, await command.ExecuteScalarAsync());
        }

        var reopened = await InitializeAsync();
        Assert.Equal(Feature, Assert.Single(await reopened.GetFeaturesAsync("assets")));
        var pending = Assert.Single(await reopened.GetPendingAsync(10));
        Assert.Equal(OperationId, pending.OperationId);
        Assert.Equal(Feature, pending.PayloadJson);
        Assert.Null(pending.ReplicaBinding);
    }

    [Fact]
    public async Task CacheAndQueueRecoverAtomicallyAfterProcessDeath()
    {
        const string previousFeature = """{"attributes":{"OBJECTID":42,"name":"previous value"}}""";
        var store = await InitializeAsync();
        await store.UpsertFeatureAsync("assets", previousFeature);
        await RunCrashAsync("edit_enqueued_before_cache_write");

        var reopened = CreateStore(_directory);
        Assert.Equal(previousFeature, Assert.Single(await reopened.GetFeaturesAsync("assets")));
        Assert.Equal(0, await reopened.CountPendingAsync());

        await SaveEditAsync(reopened);
        var replayed = CreateStore(_directory);
        Assert.Equal(Feature, Assert.Single(await replayed.GetFeaturesAsync("assets")));
        Assert.Equal(OperationId, Assert.Single(await replayed.GetPendingAsync(10)).OperationId);
    }

    [Theory]
    [InlineData("queue_lease_committed", false)]
    [InlineData("upload_acknowledgement_before_commit", true)]
    public async Task SyncReclaimsLeaseAndRecordsReceiptAfterProcessDeath(string checkpoint, bool acceptedBeforeDeath)
    {
        await SaveEditAsync(await InitializeAsync());
        await RunCrashAsync(checkpoint);

        var uploader = new DurableFixtureUploader(_directory);
        var restarted = CreateStore(_directory);
        Assert.Null(await restarted.GetUploadAcknowledgementAsync(OperationId));
        Assert.Equal(acceptedBeforeDeath, File.Exists(Path.Combine(_directory, "server-receipt")));
        Assert.Equal(0, (await new OfflineSyncEngine(restarted, uploader).SyncAsync()).Loaded);

        var expired = CreateStore(_directory, age: TimeSpan.FromMinutes(2));
        Assert.Equal(1, (await new OfflineSyncEngine(expired, uploader).SyncAsync()).Succeeded);
        var verified = CreateStore(_directory, age: TimeSpan.FromMinutes(2));
        Assert.Equal(0, (await new OfflineSyncEngine(verified, uploader).SyncAsync()).Loaded);
        Assert.Equal([ServerId], (await verified.GetUploadAcknowledgementAsync(OperationId))!.ServerFeatureIds);
        Assert.Equal(Feature, Assert.Single(await verified.GetFeaturesAsync("assets")));
        Assert.Equal(acceptedBeforeDeath ? [OperationId, OperationId] : new[] { OperationId },
            await File.ReadAllLinesAsync(Path.Combine(_directory, "server-attempts")));
    }

    [Fact]
    public async Task CrashFixtureChild()
    {
        var directory = Environment.GetEnvironmentVariable(FixtureVariable);
        if (directory is null)
        {
            return;
        }

        var checkpoint = Environment.GetEnvironmentVariable(CheckpointVariable);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MobileStorageTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == $"honua.mobile.storage.checkpoint.{checkpoint}")
                {
                    File.WriteAllText(Path.Combine(directory, "checkpoint"), activity.OperationName);
                    using var current = Process.GetCurrentProcess();
                    current.Kill();
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var store = CreateStore(directory);
        switch (checkpoint)
        {
            case "queue_schema_before_commit":
            case "queue_schema_committed":
                await store.InitializeAsync();
                break;
            case "edit_enqueued_before_cache_write":
                await SaveEditAsync(store);
                break;
            default:
                await new OfflineSyncEngine(store, new DurableFixtureUploader(directory)).SyncAsync();
                break;
        }

        Assert.Fail("The crash checkpoint was not reached.");
    }

    private async Task RunCrashAsync(string checkpoint)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(EncryptedStoreProcessDeathTests).Assembly.Location);
        start.ArgumentList.Add($"/TestCaseFilter:FullyQualifiedName={typeof(EncryptedStoreProcessDeathTests).FullName}.{nameof(CrashFixtureChild)}");
        start.Environment[FixtureVariable] = _directory;
        start.Environment[CheckpointVariable] = checkpoint;

        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }

        Assert.NotEqual(0, child.ExitCode);
        var marker = Path.Combine(_directory, "checkpoint");
        var transcript = $"{await output}\n{await error}";
        Assert.True(File.Exists(marker), transcript);
        Assert.Equal($"honua.mobile.storage.checkpoint.{checkpoint}", await File.ReadAllTextAsync(marker));
    }

    private async Task<GeoPackageSyncStore> InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var store = CreateStore(_directory);
        await store.InitializeAsync();
        return store;
    }

    private async Task<SqliteConnection> OpenFixtureAsync()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "queue.gpkg"),
            Password = TestKey,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }

    private static GeoPackageSyncStore CreateStore(string directory, TimeSpan age = default) => new(new GeoPackageSyncStoreOptions
    {
        DatabasePath = Path.Combine(directory, "queue.gpkg"),
        EncryptionKey = TestKey,
        InProgressLeaseTimeout = TimeSpan.FromMinutes(1),
        TimeProvider = new FixedTimeProvider(InitialTime + age),
    });

    private static Task SaveEditAsync(GeoPackageSyncStore store) => store.UpsertFeatureAndEnqueueAsync(new OfflineEditOperation
    {
        OperationId = OperationId,
        LayerKey = "assets",
        TargetCollection = "assets",
        OperationType = OfflineOperationType.Add,
        PayloadJson = Feature,
    }, Feature);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DurableFixtureUploader(string directory) : IOfflineOperationUploader
    {
        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct)
        {
            File.AppendAllLines(Path.Combine(directory, "server-attempts"), [operation.OperationId]);
            var receipt = Path.Combine(directory, "server-receipt");
            if (!File.Exists(receipt))
            {
                File.WriteAllText(receipt, operation.OperationId);
            }

            Assert.Equal(operation.OperationId, File.ReadAllText(receipt));
            return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success, ServerFeatureIds = [ServerId] });
        }
    }
}
