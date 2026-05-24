using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Offline.Abstractions;
using Microsoft.Data.Sqlite;

// Disambiguate the mobile orchestrator from Honua.Sdk.Offline.OfflineSyncEngine.
using OfflineSyncEngine = Honua.Mobile.Offline.Sync.OfflineSyncEngine;
using OfflineSyncEngineOptions = Honua.Mobile.Offline.Sync.OfflineSyncEngineOptions;
using ReplicaSyncClient = Honua.Sdk.Offline.ReplicaSyncClient;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class DisconnectedFieldWorkflowAcceptanceTests : IDisposable
{
    private const string SchemaVersion = "honua.mobile.disconnected-field-workflow.evidence.v1";
    private const string WorkflowName = "disconnected-field-workflow";
    private const string DefaultPackageId = "pkg_acceptance_field_workflow";
    private const string DefaultServiceId = "assets";
    private const int FieldDayOperationCount = 500;
    private static readonly TimeSpan FieldDayDatasetBudget = TimeSpan.FromSeconds(10);
    private const string FailureCategoryConfiguration = "configuration";
    private const string FailureCategoryPackage = "package";
    private const string FailureCategoryLocalCache = "local-cache";
    private const string FailureCategoryEditQueue = "edit-queue";
    private const string FailureCategoryTransport = "transport";
    private const string FailureCategoryConflict = "conflict";
    private const long DeleteTargetObjectId = 3;
    private static readonly DateTimeOffset FixedOperationTime = new(2026, 5, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _rootDirectory;

    public DisconnectedFieldWorkflowAcceptanceTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), $"honua-disconnected-field-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootDirectory);
    }

    [Fact]
    public async Task LoopbackHarness_RunsOnlineOfflineReconnectWorkflow_AndEmitsEvidence()
    {
        await using var server = await HonuaIntegrationServer.StartAsync();
        var config = AcceptanceHarnessConfiguration.Loopback(
            server.BaseUri,
            Path.Combine(_rootDirectory, "evidence"));

        var result = await RunHarnessAsync(
            config,
            createReplicaClient: () => CreateReplicaClient(config),
            createUploader: () => CreateUploader(config),
            verifyPreSyncCloudStateAsync: evidence =>
            {
                Assert.DoesNotContain(server.Requests, request =>
                    request.Method == "POST" &&
                    request.Path == "/rest/services/assets/FeatureServer/0/applyEdits");
                evidence.FinalState.PreSyncCloudVerification = "loopback cloud state unchanged before reconnect";
                return Task.CompletedTask;
            },
            verifyPostSyncCloudStateAsync: evidence =>
            {
                Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/createReplica"));
                Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/extractChanges"));
                Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/synchronizeReplica"));
                Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/0/applyEdits"));

                var applyEdits = server.Requests
                    .Where(request =>
                        request.Method == "POST" &&
                        request.Path == "/rest/services/assets/FeatureServer/0/applyEdits")
                    .Select(request => WebUtility.UrlDecode(request.Body))
                    .ToArray();
                Assert.Equal(3, applyEdits.Length);
                Assert.Contains(applyEdits, body => body.Contains("adds=", StringComparison.OrdinalIgnoreCase) &&
                                                     body.Contains("Offline Pump Acceptance", StringComparison.Ordinal));
                Assert.Contains(applyEdits, body => body.Contains("updates=", StringComparison.OrdinalIgnoreCase) &&
                                                     body.Contains("inspection-complete", StringComparison.Ordinal));
                Assert.Contains(applyEdits, body => body.Contains("deletes=", StringComparison.OrdinalIgnoreCase) &&
                                                     body.Contains("3", StringComparison.Ordinal));
                evidence.FinalState.CloudVerification = "loopback applyEdits observed for create/update/delete";
                return Task.CompletedTask;
            });

        Assert.Equal("passed", result.Evidence.Status);
        Assert.True(File.Exists(result.EvidencePath));
        Assert.Equal(3, result.Evidence.FinalState.PendingOperationCountBeforeReconnect);
        Assert.Equal(0, result.Evidence.FinalState.PendingOperationCount);
        Assert.True(result.Evidence.FinalState.LocalFeatureCount >= 1);
        Assert.Equal("replica-abc-123", result.Evidence.CursorState["replica:assets"]);
        Assert.Equal("100", result.Evidence.CursorState["servergen:assets"]);
        Assert.Equal(
            [
                "op-acceptance-add-001",
                "op-acceptance-update-001",
                "op-acceptance-delete-001",
                "op-acceptance-media-001",
            ],
            result.Evidence.OperationIds);
        Assert.Contains(result.Evidence.PlannedOperations, operation =>
            operation.OperationId == "op-acceptance-media-001" &&
            operation.Kind == "attachment-metadata" &&
            operation.Metadata["fileName"] == "offline-pump-photo.jpg");

        var evidenceJson = await File.ReadAllTextAsync(result.EvidencePath);
        Assert.Contains(SchemaVersion, evidenceJson);
        Assert.Contains("\"online-download\"", evidenceJson);
        Assert.Contains("\"offline-edit\"", evidenceJson);
        Assert.Contains("\"reconnect-sync\"", evidenceJson);
        Assert.Contains("\"verify\"", evidenceJson);
        Assert.Contains("\"failureCategories\"", evidenceJson);
        Assert.Contains("\"attachment-metadata\"", evidenceJson);
    }

    [Fact]
    [Trait("Category", "CloudAcceptance")]
    public async Task CloudHarness_RunsOnlyWhenExplicitlyConfigured_AndOtherwiseEmitsSkippedEvidence()
    {
        AcceptanceHarnessConfiguration? config;
        try
        {
            config = AcceptanceHarnessConfiguration.TryLoadCloudFromEnvironment();
        }
        catch (Exception ex)
        {
            var failed = DisconnectedFieldWorkflowEvidence.FailedCloudGate(
                Environment.GetEnvironmentVariable("HONUA_MOBILE_ACCEPTANCE_EVIDENCE_DIR")
                    ?? Path.Combine(_rootDirectory, "evidence"),
                ex);
            await WriteEvidenceAsync(failed);
            throw;
        }

        if (config is null)
        {
            var skipped = DisconnectedFieldWorkflowEvidence.Skipped(
                Path.Combine(_rootDirectory, "evidence"),
                "Set HONUA_MOBILE_CLOUD_ACCEPTANCE=1 plus cloud fixture env vars to run.");
            await WriteEvidenceAsync(skipped);
            return;
        }

        var result = await RunHarnessAsync(
            config,
            createReplicaClient: () => CreateReplicaClient(config),
            createUploader: () => CreateUploader(config),
            verifyPreSyncCloudStateAsync: async evidence =>
            {
                if (!config.VerifyCloudReadback)
                {
                    evidence.FinalState.PreSyncCloudVerification =
                        "cloud readback verification disabled by HONUA_MOBILE_CLOUD_VERIFY_READBACK=0";
                    return;
                }

                var preSync = await QueryCloudReadbackAsync(config);
                evidence.FinalState.RunTaggedFeatureCountBeforeReconnect = preSync.RunTaggedFeatureCount;
                evidence.FinalState.DeleteTargetPresentBeforeReconnect = preSync.DeleteTargetPresent;
                evidence.FinalState.PreSyncCloudVerification =
                    "cloud fixture has no run-tagged edits before reconnect and includes the deterministic delete target";

                Assert.Equal(0, preSync.RunTaggedFeatureCount);
                Assert.True(preSync.DeleteTargetPresent);
            },
            verifyPostSyncCloudStateAsync: async evidence =>
            {
                if (!config.VerifyCloudReadback)
                {
                    evidence.FinalState.CloudVerification =
                        "cloud readback verification disabled by HONUA_MOBILE_CLOUD_VERIFY_READBACK=0";
                    return;
                }

                var postSync = await QueryCloudReadbackAsync(config);
                evidence.FinalState.RunTaggedFeatureCount = postSync.RunTaggedFeatureCount;
                evidence.FinalState.DeleteTargetPresent = postSync.DeleteTargetPresent;
                evidence.FinalState.CloudVerification =
                    "cloud readback found run-tagged create/update edits and confirmed deterministic delete target removal";

                Assert.True(postSync.RunTaggedFeatureCount >= 2, $"Expected at least two run-tagged create/update records, got {postSync.RunTaggedFeatureCount}.");
                Assert.Contains("created-offline", postSync.Statuses);
                Assert.Contains("inspection-complete", postSync.Statuses);
                Assert.False(postSync.DeleteTargetPresent);
            });

        Assert.Equal("passed", result.Evidence.Status);
        Assert.True(File.Exists(result.EvidencePath));
    }

    [Fact]
    public void AcceptancePlan_IsDeterministic_AndCoversRequiredOfflineOperations()
    {
        var config = AcceptanceHarnessConfiguration.Loopback(
            new Uri("http://127.0.0.1:5000"),
            Path.Combine(_rootDirectory, "evidence"));

        var plan = AcceptanceWorkflowPlan.Create(config);

        Assert.Equal(["online-download", "offline-edit", "reconnect-sync", "verify"], plan.Sequence);
        Assert.Equal(4, plan.Operations.Count);
        Assert.Equal(3, plan.SyncableOperations.Count);
        Assert.Contains(plan.Operations, operation => operation.Kind == "feature-create" && operation.OperationType == OfflineOperationType.Add);
        Assert.Contains(plan.Operations, operation => operation.Kind == "feature-update" && operation.OperationType == OfflineOperationType.Update);
        Assert.Contains(plan.Operations, operation => operation.Kind == "feature-delete" && operation.OperationType == OfflineOperationType.Delete);

        var media = Assert.Single(plan.Operations, operation => operation.Kind == "attachment-metadata");
        Assert.False(media.IsSyncable);
        Assert.Equal("offline-pump-photo.jpg", media.Metadata["fileName"]);
        Assert.Equal("image/jpeg", media.Metadata["contentType"]);
        Assert.Equal("sha256:acceptance-photo-placeholder", media.Metadata["contentHash"]);

        var createPayload = JsonSerializer.Deserialize<OfflineOperationPayload>(
            plan.SyncableOperations[0].SyncOperation!.PayloadJson,
            JsonOptions);
        Assert.NotNull(createPayload);
        Assert.Equal(DefaultPackageId, createPayload.PackageId);
        Assert.Equal("0", createPayload.SourceId);
        Assert.Equal("op-acceptance-media-001", createPayload.Metadata!["mediaOperationId"]);
    }

    [Fact]
    public void AcceptancePlan_CoversCompetitiveProductScenarios_AndGatedDeviceSmoke()
    {
        var config = AcceptanceHarnessConfiguration.Loopback(
            new Uri("http://127.0.0.1:5000"),
            Path.Combine(_rootDirectory, "evidence"));

        var scenarioCoverage = AcceptanceWorkflowPlan.Create(config).ScenarioCoverage
            .ToDictionary(scenario => scenario.Id, StringComparer.Ordinal);

        Assert.Equal(
            [
                "offline-create-sync",
                "offline-edit-delete-sync",
                "conflict-manual-review",
                "attachment-round-trip",
                "form-rules-repeat-sections",
                "bad-network-retry",
                "restart-durability",
                "field-day-scale-budget",
                "appium-field-workflow-smoke",
            ],
            scenarioCoverage.Keys);

        Assert.Equal("service", scenarioCoverage["offline-create-sync"].AutomationLevel);
        Assert.Equal("loopback-server", scenarioCoverage["offline-edit-delete-sync"].AutomationLevel);
        Assert.Equal("gated-device", scenarioCoverage["appium-field-workflow-smoke"].AutomationLevel);
        Assert.Contains("launch", scenarioCoverage["appium-field-workflow-smoke"].Steps);
        Assert.Contains("configure-server", scenarioCoverage["appium-field-workflow-smoke"].Steps);
        Assert.Contains("download-project", scenarioCoverage["appium-field-workflow-smoke"].Steps);
        Assert.Contains("create-record", scenarioCoverage["appium-field-workflow-smoke"].Steps);
        Assert.Contains("sync", scenarioCoverage["appium-field-workflow-smoke"].Steps);
        Assert.Equal(
            "HONUA_MOBILE_APPIUM_SMOKE",
            scenarioCoverage["appium-field-workflow-smoke"].Metadata["skipGate"]);
    }

    [Fact]
    public async Task ManualConflictReviewScenario_LeavesFailedOperationAndEmitsEvidence()
    {
        var config = AcceptanceHarnessConfiguration.Loopback(
            new Uri("http://127.0.0.1:5000"),
            Path.Combine(_rootDirectory, "evidence"));
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = config.DatabasePath,
        });
        var plan = AcceptanceWorkflowPlan.Create(config);
        var evidence = DisconnectedFieldWorkflowEvidence.Started(config, plan);
        var conflictOperation = plan.SyncableOperations.Single(operation => operation.Kind == "feature-update");

        await store.InitializeAsync();
        await store.EnqueueAsync(conflictOperation.SyncOperation!);

        await RunPhaseAsync(evidence, "manual-conflict-review", async phase =>
        {
            var sync = new OfflineSyncEngine(
                store,
                new ConflictUploader("server rejected base sync token; conflict requires manual review"),
                new OfflineSyncEngineOptions
                {
                    BatchSize = 10,
                    ConflictStrategy = SyncConflictStrategy.ManualReview,
                });

            var result = await sync.SyncAsync();
            var rows = await ReadSyncQueueRowsAsync(config.DatabasePath);
            var failed = Assert.Single(rows);

            phase.Details["loaded"] = result.Loaded.ToString();
            phase.Details["succeeded"] = result.Succeeded.ToString();
            phase.Details["failed"] = result.Failed.ToString();
            phase.Details["queueStatus"] = failed.Status;
            phase.Details["lastError"] = failed.LastError ?? string.Empty;
            evidence.FinalState.ManualReviewCount = rows.Count(row => row.Status == "failed");
            evidence.FinalState.PendingOperationCount = await store.CountPendingAsync();
            evidence.FinalState.LocalVerification = "manual-review conflict remains available in the GeoPackage sync queue";

            Assert.Equal(1, result.Loaded);
            Assert.Equal(0, result.Succeeded);
            Assert.Equal(1, result.Failed);
            Assert.Equal("failed", failed.Status);
            Assert.Contains("manual review", failed.LastError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, evidence.FinalState.PendingOperationCount);
        });

        evidence.Status = "passed";
        evidence.CompletedAtUtc = DateTimeOffset.UtcNow;
        var evidencePath = await WriteEvidenceAsync(evidence);

        Assert.True(File.Exists(evidencePath));
        var evidenceJson = await File.ReadAllTextAsync(evidencePath);
        Assert.Contains("\"manual-conflict-review\"", evidenceJson);
        Assert.Contains("\"manualReviewCount\": 1", evidenceJson);
    }

    [Fact]
    public async Task BadNetworkRetryScenario_PersistsRetryThroughRestart_AndDrainsOnReconnect()
    {
        var config = AcceptanceHarnessConfiguration.Loopback(
            new Uri("http://127.0.0.1:5000"),
            Path.Combine(_rootDirectory, "evidence"));
        var plan = AcceptanceWorkflowPlan.Create(config);
        var evidence = DisconnectedFieldWorkflowEvidence.Started(config, plan);
        var uploader = new RetryThenSuccessUploader("Connection refused by field network");

        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = config.DatabasePath,
        });
        await store.InitializeAsync();
        await store.EnqueueAsync(plan.SyncableOperations[0].SyncOperation!);

        await RunPhaseAsync(evidence, "bad-network-retry", async phase =>
        {
            var firstSync = new OfflineSyncEngine(
                store,
                uploader,
                new OfflineSyncEngineOptions { BatchSize = 10, MaxAttempts = 3 });
            var firstResult = await firstSync.SyncAsync();
            var retryRows = await ReadSyncQueueRowsAsync(config.DatabasePath);
            var retry = Assert.Single(retryRows);

            phase.Details["firstLoaded"] = firstResult.Loaded.ToString();
            phase.Details["firstFailed"] = firstResult.Failed.ToString();
            phase.Details["retryStatus"] = retry.Status;
            phase.Details["retryAttemptCount"] = retry.AttemptCount.ToString();
            phase.Details["retryError"] = retry.LastError ?? string.Empty;
            evidence.FinalState.RetryOperationCount = retryRows.Count(row => row.Status == "retry");

            Assert.Equal(1, firstResult.Failed);
            Assert.Equal("retry", retry.Status);
            Assert.Equal(1, retry.AttemptCount);
            Assert.Contains("Connection refused", retry.LastError, StringComparison.Ordinal);
        });

        await RunPhaseAsync(evidence, "restart-durability", async phase =>
        {
            var restartedStore = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
            {
                DatabasePath = config.DatabasePath,
            });
            var pendingAfterRestart = await restartedStore.CountPendingAsync();

            var secondSync = new OfflineSyncEngine(
                restartedStore,
                uploader,
                new OfflineSyncEngineOptions { BatchSize = 10, MaxAttempts = 3 });
            var secondResult = await secondSync.SyncAsync();

            phase.Details["pendingAfterRestart"] = pendingAfterRestart.ToString();
            phase.Details["secondSucceeded"] = secondResult.Succeeded.ToString();
            phase.Details["remainingQueueRows"] = (await ReadSyncQueueRowsAsync(config.DatabasePath)).Count.ToString();
            evidence.FinalState.PendingOperationCountBeforeReconnect = pendingAfterRestart;
            evidence.FinalState.PendingOperationCount = await restartedStore.CountPendingAsync();
            evidence.FinalState.LocalVerification = "retry row survived service restart and drained after reconnect";

            Assert.Equal(1, pendingAfterRestart);
            Assert.Equal(1, secondResult.Succeeded);
            Assert.Equal(0, evidence.FinalState.PendingOperationCount);
            Assert.Empty(await ReadSyncQueueRowsAsync(config.DatabasePath));
        });

        evidence.Status = "passed";
        evidence.CompletedAtUtc = DateTimeOffset.UtcNow;
        var evidencePath = await WriteEvidenceAsync(evidence);

        Assert.True(File.Exists(evidencePath));
        var evidenceJson = await File.ReadAllTextAsync(evidencePath);
        Assert.Contains("\"bad-network-retry\"", evidenceJson);
        Assert.Contains("\"restart-durability\"", evidenceJson);
        Assert.Contains("\"retryOperationCount\": 1", evidenceJson);
    }

    [Fact]
    public async Task FieldDayDatasetScenario_QueuesAndSyncsWithinPerformanceBudget()
    {
        var config = AcceptanceHarnessConfiguration.Loopback(
            new Uri("http://127.0.0.1:5000"),
            Path.Combine(_rootDirectory, "evidence"));
        var plan = AcceptanceWorkflowPlan.Create(config);
        var evidence = DisconnectedFieldWorkflowEvidence.Started(config, plan);
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = config.DatabasePath,
        });
        await store.InitializeAsync();

        await RunPhaseAsync(evidence, "field-day-scale-budget", async phase =>
        {
            var enqueueStopwatch = Stopwatch.StartNew();
            for (var index = 0; index < FieldDayOperationCount; index++)
            {
                await store.EnqueueAsync(CreateFieldDayOperation(config, index));
            }

            enqueueStopwatch.Stop();

            var pendingBeforeSync = await store.CountPendingAsync();
            var syncStopwatch = Stopwatch.StartNew();
            var sync = new OfflineSyncEngine(
                new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
                {
                    DatabasePath = config.DatabasePath,
                }),
                new AlwaysSuccessUploader(),
                new OfflineSyncEngineOptions
                {
                    BatchSize = FieldDayOperationCount,
                    MaxAttempts = 1,
                });
            var result = await sync.SyncAsync();
            syncStopwatch.Stop();

            phase.Details["operationCount"] = FieldDayOperationCount.ToString();
            phase.Details["pendingBeforeSync"] = pendingBeforeSync.ToString();
            phase.Details["succeeded"] = result.Succeeded.ToString();
            phase.Details["enqueueElapsedMilliseconds"] = enqueueStopwatch.ElapsedMilliseconds.ToString();
            phase.Details["syncElapsedMilliseconds"] = syncStopwatch.ElapsedMilliseconds.ToString();
            phase.Details["budgetMilliseconds"] = FieldDayDatasetBudget.TotalMilliseconds.ToString("F0");
            evidence.FinalState.FieldDayOperationCount = FieldDayOperationCount;
            evidence.FinalState.FieldDaySyncElapsedMilliseconds = syncStopwatch.ElapsedMilliseconds;
            evidence.FinalState.PendingOperationCount = await store.CountPendingAsync();

            Assert.Equal(FieldDayOperationCount, pendingBeforeSync);
            Assert.Equal(FieldDayOperationCount, result.Succeeded);
            Assert.Equal(0, result.Failed);
            Assert.Equal(0, evidence.FinalState.PendingOperationCount);
            Assert.True(
                enqueueStopwatch.Elapsed + syncStopwatch.Elapsed < FieldDayDatasetBudget,
                $"Field-day enqueue+sync budget exceeded: enqueue={enqueueStopwatch.Elapsed}, sync={syncStopwatch.Elapsed}, budget={FieldDayDatasetBudget}.");
        });

        evidence.Status = "passed";
        evidence.CompletedAtUtc = DateTimeOffset.UtcNow;
        var evidencePath = await WriteEvidenceAsync(evidence);

        Assert.True(File.Exists(evidencePath));
        var evidenceJson = await File.ReadAllTextAsync(evidencePath);
        Assert.Contains("\"field-day-scale-budget\"", evidenceJson);
        Assert.Contains($"\"fieldDayOperationCount\": {FieldDayOperationCount}", evidenceJson);
    }

    [Theory]
    [InlineData("cloud-fixture-gate", "HONUA_MOBILE_CLOUD_BASE_URL is required", FailureCategoryConfiguration)]
    [InlineData("online-download", "replica package is missing", FailureCategoryPackage)]
    [InlineData("reconnect-sync", "conflict requires manual review", FailureCategoryConflict)]
    [InlineData("reconnect-sync", "Invalid offline payload", FailureCategoryEditQueue)]
    [InlineData("reconnect-sync", "Connection refused", FailureCategoryTransport)]
    [InlineData("online-download", "RemoteCertificateNameMismatch", FailureCategoryTransport)]
    [InlineData("online-download", "TLS certificate hostname mismatch", FailureCategoryTransport)]
    [InlineData("online-download", "operation failed: RemoteCertificateNameMismatch", FailureCategoryTransport)]
    public void FailureClassifier_MapsHarnessFailuresToActionableCategories(
        string phaseName,
        string message,
        string expectedCategory)
    {
        Assert.Equal(expectedCategory, ClassifyFailure(new InvalidOperationException(message), phaseName));
    }

    [Fact]
    public void FailedCloudGate_EvidenceIncludesInnerCertificateDetails()
    {
        var failure = new HttpRequestException(
            "The SSL connection could not be established.",
            new InvalidOperationException("RemoteCertificateNameMismatch"));

        var evidence = DisconnectedFieldWorkflowEvidence.FailedCloudGate(_rootDirectory, failure);

        var phase = Assert.Single(evidence.Phases);
        Assert.Equal(FailureCategoryTransport, phase.Details["failureCategory"]);
        Assert.Contains("The SSL connection could not be established.", phase.Details["error"], StringComparison.Ordinal);
        Assert.Contains("RemoteCertificateNameMismatch", phase.Details["error"], StringComparison.Ordinal);
    }

    [Fact]
    public void FailureClassifier_MapsGeoPackageFailuresToLocalCache()
    {
        Assert.Equal(
            FailureCategoryLocalCache,
            ClassifyFailure(new GeoPackageStorageException("GeoPackage feature cache write failed."), "offline-edit"));
    }

    [Fact]
    public void CloudReadbackParser_ExtractsRunTaggedStatusesAndDeleteTargetPresence()
    {
        using var runTaggedFeatures = JsonDocument.Parse("""
            {
              "features": [
                { "attributes": { "objectid": 9001, "status": "created-offline", "honua_acceptance_run": "run-1" } },
                { "attributes": { "objectid": 1, "status": "inspection-complete", "honua_acceptance_run": "run-1" } }
              ]
            }
            """);
        using var deleteTarget = JsonDocument.Parse("""
            {
              "features": [
                { "attributes": { "objectid": 3, "status": "seeded" } }
              ]
            }
            """);

        var readback = CloudReadback.FromQueryResponses(runTaggedFeatures, deleteTarget);

        Assert.Equal(2, readback.RunTaggedFeatureCount);
        Assert.Equal(["created-offline", "inspection-complete"], readback.Statuses);
        Assert.True(readback.DeleteTargetPresent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootDirectory))
        {
            Directory.Delete(_rootDirectory, recursive: true);
        }
    }

    private static async Task<AcceptanceHarnessResult> RunHarnessAsync(
        AcceptanceHarnessConfiguration config,
        Func<IReplicaSyncClient> createReplicaClient,
        Func<IOfflineOperationUploader> createUploader,
        Func<DisconnectedFieldWorkflowEvidence, Task> verifyPreSyncCloudStateAsync,
        Func<DisconnectedFieldWorkflowEvidence, Task> verifyPostSyncCloudStateAsync)
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = config.DatabasePath,
        });

        var plan = AcceptanceWorkflowPlan.Create(config);
        var evidence = DisconnectedFieldWorkflowEvidence.Started(config, plan);

        try
        {
            await RunPhaseAsync(evidence, "online-download", async phase =>
            {
                var download = new DeltaDownloadEngine(
                    store,
                    createReplicaClient(),
                    new DeltaDownloadOptions
                    {
                        ReplicaName = config.ReplicaName,
                        LayerIds = config.LayerIds,
                    });

                var result = await download.DownloadAsync(config.ServiceId);
                phase.Details["adds"] = result.Adds.ToString();
                phase.Details["updates"] = result.Updates.ToString();
                phase.Details["deletes"] = result.Deletes.ToString();
                phase.Details["serverGen"] = result.ServerGen.ToString();
                phase.Details["packageId"] = config.PackageId;
            });

            await RunPhaseAsync(evidence, "offline-edit", async phase =>
            {
                await store.InitializeAsync();
                foreach (var operation in plan.SyncableOperations)
                {
                    await store.EnqueueAsync(operation.SyncOperation!);
                }

                var pending = await store.CountPendingAsync();
                evidence.FinalState.PendingOperationCountBeforeReconnect = pending;
                phase.Details["plannedOperationCount"] = plan.Operations.Count.ToString();
                phase.Details["queuedOperationCount"] = plan.SyncableOperations.Count.ToString();
                phase.Details["plannedMediaOperationId"] = plan.Operations.Single(operation => operation.Kind == "attachment-metadata").OperationId;
                phase.Details["pendingAfterEdit"] = pending.ToString();

                Assert.Equal(plan.SyncableOperations.Count, pending);
                await verifyPreSyncCloudStateAsync(evidence);
            });

            await RunPhaseAsync(evidence, "reconnect-sync", async phase =>
            {
                var sync = new OfflineSyncEngine(
                    store,
                    createUploader(),
                    new OfflineSyncEngineOptions
                    {
                        BatchSize = 10,
                        ConflictStrategy = SyncConflictStrategy.ManualReview,
                    });

                var result = await sync.SyncAsync();
                phase.Details["loaded"] = result.Loaded.ToString();
                phase.Details["succeeded"] = result.Succeeded.ToString();
                phase.Details["failed"] = result.Failed.ToString();
                phase.Details["failures"] = string.Join(" | ", result.Failures.Select(failure => $"{failure.OperationId}: {failure.Reason}"));

                if (result.Failed > 0 || result.Succeeded != plan.SyncableOperations.Count)
                {
                    throw SyncRunFailedException.FromResult(result);
                }

                Assert.Equal(plan.SyncableOperations.Count, result.Loaded);
                Assert.True(result.Succeeded == plan.SyncableOperations.Count, phase.Details["failures"]);
                Assert.True(result.Failed == 0, phase.Details["failures"]);
            });

            await RunPhaseAsync(evidence, "verify", async phase =>
            {
                var layerKey = config.LayerIds[0].ToString();
                var features = await store.GetFeaturesAsync(layerKey);
                var pending = await store.CountPendingAsync();

                evidence.CursorState[$"replica:{config.ServiceId}"] =
                    await store.GetSyncCursorAsync($"replica:{config.ServiceId}") ?? string.Empty;
                evidence.CursorState[$"servergen:{config.ServiceId}"] =
                    await store.GetSyncCursorAsync($"servergen:{config.ServiceId}") ?? string.Empty;
                evidence.FinalState.LocalFeatureCount = features.Count;
                evidence.FinalState.PendingOperationCount = pending;
                evidence.FinalState.LocalVerification = "downloaded features retained and sync queue drained";

                phase.Details["localFeatureCount"] = features.Count.ToString();
                phase.Details["pendingOperationCount"] = pending.ToString();

                Assert.NotEmpty(features);
                Assert.Equal(0, pending);
                Assert.False(string.IsNullOrWhiteSpace(evidence.CursorState[$"replica:{config.ServiceId}"]));
                Assert.False(string.IsNullOrWhiteSpace(evidence.CursorState[$"servergen:{config.ServiceId}"]));

                await verifyPostSyncCloudStateAsync(evidence);
            });

            evidence.Status = "passed";
            evidence.CompletedAtUtc = DateTimeOffset.UtcNow;
            var evidencePath = await WriteEvidenceAsync(evidence);
            return new AcceptanceHarnessResult(evidence, evidencePath);
        }
        catch
        {
            evidence.Status = "failed";
            evidence.CompletedAtUtc = DateTimeOffset.UtcNow;
            await WriteEvidenceAsync(evidence);
            throw;
        }
    }

    private static async Task RunPhaseAsync(
        DisconnectedFieldWorkflowEvidence evidence,
        string phaseName,
        Func<AcceptancePhaseEvidence, Task> action)
    {
        var phase = new AcceptancePhaseEvidence
        {
            Name = phaseName,
            Status = "running",
            StartedAtUtc = DateTimeOffset.UtcNow,
        };
        evidence.Phases.Add(phase);

        try
        {
            await action(phase);
            phase.Status = "passed";
        }
        catch (Exception ex)
        {
            phase.Status = "failed";
            phase.Details["error"] = FormatExceptionForEvidence(ex);
            phase.Details["failureCategory"] = ClassifyFailure(ex, phaseName);
            throw;
        }
        finally
        {
            phase.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private static string ClassifyFailure(Exception ex, string phaseName)
    {
        if (ex is SyncRunFailedException syncFailure)
        {
            return syncFailure.FailureCategory;
        }

        if (ex is GeoPackageStorageException)
        {
            return FailureCategoryLocalCache;
        }

        if (ex is HttpRequestException or TaskCanceledException or HonuaMobileApiException)
        {
            return FailureCategoryTransport;
        }

        var message = FlattenExceptionMessages(ex);
        if (message.Contains("HONUA_MOBILE_", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("base url", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("api key", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryConfiguration;
        }

        if (message.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("precondition", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryConflict;
        }

        if (message.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("RemoteCertificate", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("TLS", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryTransport;
        }

        if (message.Contains("invalid offline payload", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("offline operation", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryEditQueue;
        }

        return phaseName switch
        {
            "online-download" => FailureCategoryPackage,
            "offline-edit" => FailureCategoryEditQueue,
            "reconnect-sync" => FailureCategoryTransport,
            _ => FailureCategoryConfiguration,
        };
    }

    private static string FormatExceptionForEvidence(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            messages.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" | ", messages);
    }

    private static string FlattenExceptionMessages(Exception ex)
    {
        var messages = new List<string>();
        for (var current = ex; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private static string ClassifySyncFailureReason(string reason)
    {
        if (reason.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("precondition", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryConflict;
        }

        if (reason.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("RemoteCertificate", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("TLS", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("transport", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryTransport;
        }

        if (reason.Contains("invalid offline payload", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("unsupported protocol", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("operation", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryEditQueue;
        }

        return FailureCategoryTransport;
    }

    private static IReadOnlyDictionary<string, string> FailureCategories()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FailureCategoryConfiguration] = "Fixture URLs, credentials, package ids, source ids, or run flags are missing or inconsistent.",
            [FailureCategoryPackage] = "The cloud/staging package or replica fixture cannot be created, downloaded, or decoded.",
            [FailureCategoryLocalCache] = "The mobile GeoPackage cache could not persist features, cursors, or queued operations.",
            [FailureCategoryEditQueue] = "A planned offline operation cannot be serialized, queued, claimed, or uploaded as a valid edit.",
            [FailureCategoryTransport] = "Network transport, TLS/certificate validation, authentication, timeout, or server availability prevented an operation.",
            [FailureCategoryConflict] = "Server state rejected an offline edit because the base sync token or feature version conflicted.",
        };

    private static OfflineEditOperation CreateFeatureOperation(
        AcceptanceHarnessConfiguration config,
        string operationId,
        string kind,
        OfflineOperationType operationType,
        int layerId,
        int priority,
        JsonElement? feature = null,
        IReadOnlyList<long>? deleteObjectIds = null,
        IReadOnlyDictionary<string, string>? extraMetadata = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workflow"] = WorkflowName,
            ["operationKind"] = kind,
            ["fixtureAssumption"] = "honua-server#895 provides FeatureServer replica and applyEdits endpoints",
        };
        if (extraMetadata is not null)
        {
            foreach (var item in extraMetadata)
            {
                metadata[item.Key] = item.Value;
            }
        }

        return new OfflineEditOperation
        {
            OperationId = operationId,
            LayerKey = $"{config.ServiceId}/{layerId}",
            TargetCollection = config.ServiceId,
            OperationType = operationType,
            CreatedAtUtc = FixedOperationTime.AddMinutes(priority - 1),
            Priority = priority,
            PayloadJson = JsonSerializer.Serialize(new OfflineOperationPayload
            {
                PackageId = config.PackageId,
                SourceId = layerId.ToString(),
                BaseSyncToken = $"servergen:{config.ServiceId}",
                Protocol = "FeatureServer",
                ServiceId = config.ServiceId,
                LayerId = layerId,
                Feature = feature,
                DeleteObjectIds = deleteObjectIds,
                Metadata = metadata,
            }, JsonOptions),
        };
    }

    private static OfflineEditOperation CreateFieldDayOperation(AcceptanceHarnessConfiguration config, int index)
    {
        var objectId = 10_000 + index;
        var feature = JsonSerializer.SerializeToElement(new
        {
            attributes = new
            {
                objectid = objectId,
                name = $"Field Day Asset {index:D4}",
                status = "created-offline",
                honua_acceptance_run = config.RunId,
            },
            geometry = new
            {
                x = -157.8 + index * 0.000001,
                y = 21.3 + index * 0.000001,
            },
        }, JsonOptions);

        return CreateFeatureOperation(
            config,
            $"op-field-day-{index:D4}",
            "field-day-create",
            OfflineOperationType.Add,
            config.LayerIds[0],
            priority: index + 1,
            feature: feature,
            extraMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dataset"] = "field-day-scale-budget",
                ["sequence"] = index.ToString(CultureInfo.InvariantCulture),
            });
    }

    private static async Task<IReadOnlyList<SyncQueueRow>> ReadSyncQueueRowsAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id, status, attempt_count, last_error
            FROM honua_sync_queue
            ORDER BY operation_id;
            """;

        var rows = new List<SyncQueueRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new SyncQueueRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return rows;
    }

    private sealed record SyncQueueRow(
        string OperationId,
        string Status,
        int AttemptCount,
        string? LastError);

    private static IReplicaSyncClient CreateReplicaClient(AcceptanceHarnessConfiguration config)
    {
        var http = new HttpClient { BaseAddress = config.BaseUri };
        ApplyAuthHeaders(http.DefaultRequestHeaders, config);
        return new ReplicaSyncClient(http);
    }

    private static IOfflineOperationUploader CreateUploader(AcceptanceHarnessConfiguration config)
    {
        var options = new HonuaMobileClientOptions
        {
            BaseUri = config.BaseUri,
            ApiKey = config.ApiKey,
            BearerToken = config.BearerToken,
            AllowInsecureTransportForDevelopment = config.BaseUri.Scheme == Uri.UriSchemeHttp,
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
        };

        return new HonuaApiOfflineOperationUploader(new HonuaMobileClient(new HttpClient(), options));
    }

    private static void ApplyAuthHeaders(HttpRequestHeaders headers, AcceptanceHarnessConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            headers.TryAddWithoutValidation("X-API-Key", config.ApiKey);
        }

        if (!string.IsNullOrWhiteSpace(config.BearerToken))
        {
            headers.Authorization = new AuthenticationHeaderValue("Bearer", config.BearerToken);
        }
    }

    private static async Task<string> WriteEvidenceAsync(DisconnectedFieldWorkflowEvidence evidence)
    {
        Directory.CreateDirectory(evidence.ArtifactDirectory);
        var path = Path.Combine(evidence.ArtifactDirectory, $"{evidence.RunId}.evidence.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, JsonOptions));
        return path;
    }

    private sealed record AcceptanceHarnessResult(
        DisconnectedFieldWorkflowEvidence Evidence,
        string EvidencePath);

    private sealed class AcceptanceWorkflowPlan
    {
        public required IReadOnlyList<string> Sequence { get; init; }

        public required IReadOnlyList<PlannedAcceptanceOperation> Operations { get; init; }

        public required IReadOnlyList<AcceptanceScenarioEvidence> ScenarioCoverage { get; init; }

        public IReadOnlyList<PlannedAcceptanceOperation> SyncableOperations
            => Operations.Where(operation => operation.IsSyncable).ToArray();

        public static AcceptanceWorkflowPlan Create(AcceptanceHarnessConfiguration config)
        {
            var layerId = config.LayerIds[0];
            var sourceId = layerId.ToString();
            var mediaOperation = new PlannedAcceptanceOperation
            {
                OperationId = "op-acceptance-media-001",
                Kind = "attachment-metadata",
                SourceId = sourceId,
                TargetId = "objectid:9001",
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["fileName"] = "offline-pump-photo.jpg",
                    ["contentType"] = "image/jpeg",
                    ["contentHash"] = "sha256:acceptance-photo-placeholder",
                    ["captureUtc"] = FixedOperationTime.AddMinutes(3).ToString("O"),
                    ["relationship"] = "field-evidence",
                },
            };

            var createFeature = JsonSerializer.SerializeToElement(new
            {
                attributes = new
                {
                    objectid = 9001,
                    name = "Offline Pump Acceptance",
                    status = "created-offline",
                    honua_acceptance_run = config.RunId,
                    media_operation_id = mediaOperation.OperationId,
                },
                geometry = new
                {
                    x = -157.8,
                    y = 21.3,
                },
            }, JsonOptions);
            var updateFeature = JsonSerializer.SerializeToElement(new
            {
                attributes = new
                {
                    objectid = 1,
                    name = "Pump Station",
                    status = "inspection-complete",
                    honua_acceptance_run = config.RunId,
                },
                geometry = new
                {
                    x = -157.8001,
                    y = 21.3001,
                },
            }, JsonOptions);

            var createOperation = CreateFeatureOperation(
                config,
                "op-acceptance-add-001",
                "feature-create",
                OfflineOperationType.Add,
                layerId,
                priority: 1,
                feature: createFeature,
                extraMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["mediaOperationId"] = mediaOperation.OperationId,
                    ["mediaFileName"] = mediaOperation.Metadata["fileName"],
                    ["mediaContentType"] = mediaOperation.Metadata["contentType"],
                    ["mediaContentHash"] = mediaOperation.Metadata["contentHash"],
                });
            var updateOperation = CreateFeatureOperation(
                config,
                "op-acceptance-update-001",
                "feature-update",
                OfflineOperationType.Update,
                layerId,
                priority: 2,
                feature: updateFeature);
            var deleteOperation = CreateFeatureOperation(
                config,
                "op-acceptance-delete-001",
                "feature-delete",
                OfflineOperationType.Delete,
                layerId,
                priority: 3,
                deleteObjectIds: [DeleteTargetObjectId]);

            return new AcceptanceWorkflowPlan
            {
                Sequence = ["online-download", "offline-edit", "reconnect-sync", "verify"],
                ScenarioCoverage = CreateScenarioCoverage(),
                Operations =
                [
                    new PlannedAcceptanceOperation
                    {
                        OperationId = createOperation.OperationId,
                        Kind = "feature-create",
                        OperationType = createOperation.OperationType,
                        SourceId = sourceId,
                        TargetId = "objectid:9001",
                        SyncOperation = createOperation,
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["status"] = "created-offline",
                            ["mediaOperationId"] = mediaOperation.OperationId,
                        },
                    },
                    new PlannedAcceptanceOperation
                    {
                        OperationId = updateOperation.OperationId,
                        Kind = "feature-update",
                        OperationType = updateOperation.OperationType,
                        SourceId = sourceId,
                        TargetId = "objectid:1",
                        SyncOperation = updateOperation,
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["status"] = "inspection-complete",
                        },
                    },
                    new PlannedAcceptanceOperation
                    {
                        OperationId = deleteOperation.OperationId,
                        Kind = "feature-delete",
                        OperationType = deleteOperation.OperationType,
                        SourceId = sourceId,
                        TargetId = "objectid:3",
                        SyncOperation = deleteOperation,
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["deleteObjectIds"] = "3",
                        },
                    },
                    mediaOperation,
                ],
            };
        }

        private static IReadOnlyList<AcceptanceScenarioEvidence> CreateScenarioCoverage()
            =>
            [
                new AcceptanceScenarioEvidence
                {
                    Id = "offline-create-sync",
                    AutomationLevel = "service",
                    Steps = ["queue-create", "reconnect-sync", "verify-server"],
                    Artifacts = ["sync-state", "geopackage-state", "server-apply-edits"],
                },
                new AcceptanceScenarioEvidence
                {
                    Id = "offline-edit-delete-sync",
                    AutomationLevel = "loopback-server",
                    Steps = ["queue-update", "queue-delete", "reconnect-sync", "verify-server"],
                    Artifacts = ["sync-state", "geopackage-state", "server-apply-edits"],
                },
                new AcceptanceScenarioEvidence
                {
                    Id = "conflict-manual-review",
                    AutomationLevel = "service",
                    Steps = ["queue-conflicting-edit", "sync", "verify-manual-review"],
                    Artifacts = ["sync-queue-state", "failure-category"],
                },
                new AcceptanceScenarioEvidence
                {
                    Id = "attachment-round-trip",
                    AutomationLevel = "service",
                    Steps = ["capture-media", "queue-metadata", "push-attachment", "pull-attachment"],
                    Artifacts = ["attachment-metadata", "local-media-path"],
                },
                new AcceptanceScenarioEvidence
                {
                    Id = "form-rules-repeat-sections",
                    AutomationLevel = "service",
                    Steps = ["apply-defaults", "calculate-values", "validate-repeat", "queue-record"],
                    Artifacts = ["form-validation-state", "offline-operation-payload"],
                },
                new AcceptanceScenarioEvidence
                {
                    Id = "bad-network-retry",
                    AutomationLevel = "service",
                    Steps = ["sync-temporary-failure", "persist-retry", "sync-success"],
                    Artifacts = ["sync-queue-state", "retry-attempt-count"],
                },
                new AcceptanceScenarioEvidence
                {
                    Id = "restart-durability",
                    AutomationLevel = "service",
                    Steps = ["queue-offline-change", "restart-store", "resume-sync"],
                    Artifacts = ["geopackage-state", "sync-queue-state"],
                },
                new AcceptanceScenarioEvidence
                {
                    Id = "field-day-scale-budget",
                    AutomationLevel = "service",
                    Steps = ["queue-large-dataset", "sync-large-dataset", "assert-budget"],
                    Artifacts = ["operation-count", "elapsed-milliseconds"],
                },
                new AcceptanceScenarioEvidence
                {
                    Id = "appium-field-workflow-smoke",
                    AutomationLevel = "gated-device",
                    Steps = ["launch", "configure-server", "download-project", "create-record", "sync"],
                    Artifacts = ["device-run-json", "appium-log", "sync-state"],
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["skipGate"] = "HONUA_MOBILE_APPIUM_SMOKE",
                        ["skipReason"] = "Device/Appium smoke is gated because it needs a simulator or device plus live server credentials.",
                    },
                },
            ];
    }

    private sealed class PlannedAcceptanceOperation
    {
        public required string OperationId { get; init; }

        public required string Kind { get; init; }

        public OfflineOperationType? OperationType { get; init; }

        public required string SourceId { get; init; }

        public required string TargetId { get; init; }

        public required Dictionary<string, string> Metadata { get; init; }

        public OfflineEditOperation? SyncOperation { get; init; }

        public bool IsSyncable => SyncOperation is not null;

        public PlannedOperationEvidence ToEvidence()
            => new()
            {
                OperationId = OperationId,
                Kind = Kind,
                OperationType = OperationType?.ToString(),
                SourceId = SourceId,
                TargetId = TargetId,
                IsSyncable = IsSyncable,
                Metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal),
            };
    }

    private sealed class PlannedOperationEvidence
    {
        public required string OperationId { get; init; }

        public required string Kind { get; init; }

        public string? OperationType { get; init; }

        public required string SourceId { get; init; }

        public required string TargetId { get; init; }

        public required bool IsSyncable { get; init; }

        public required Dictionary<string, string> Metadata { get; init; }
    }

    private sealed class AcceptanceScenarioEvidence
    {
        public required string Id { get; init; }

        public required string AutomationLevel { get; init; }

        public required IReadOnlyList<string> Steps { get; init; }

        public required IReadOnlyList<string> Artifacts { get; init; }

        public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class SyncRunFailedException : Exception
    {
        private SyncRunFailedException(string message, string failureCategory)
            : base(message)
        {
            FailureCategory = failureCategory;
        }

        public string FailureCategory { get; }

        public static SyncRunFailedException FromResult(SyncRunResult result)
        {
            var reason = result.Failures.FirstOrDefault()?.Reason
                ?? $"Expected all planned operations to sync; loaded={result.Loaded}, succeeded={result.Succeeded}, failed={result.Failed}.";
            return new SyncRunFailedException(reason, ClassifySyncFailureReason(reason));
        }
    }

    private sealed class ConflictUploader : IOfflineOperationUploader
    {
        private readonly string _message;

        public ConflictUploader(string message)
        {
            _message = message;
        }

        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
            => Task.FromResult(new UploadResult
            {
                Outcome = UploadOutcome.Conflict,
                Message = _message,
            });
    }

    private sealed class RetryThenSuccessUploader : IOfflineOperationUploader
    {
        private readonly string _firstFailureMessage;
        private readonly HashSet<string> _failedOnce = new(StringComparer.Ordinal);

        public RetryThenSuccessUploader(string firstFailureMessage)
        {
            _firstFailureMessage = firstFailureMessage;
        }

        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
        {
            if (_failedOnce.Add(operation.OperationId))
            {
                return Task.FromResult(new UploadResult
                {
                    Outcome = UploadOutcome.RetryableFailure,
                    Message = _firstFailureMessage,
                });
            }

            return Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success });
        }
    }

    private sealed class AlwaysSuccessUploader : IOfflineOperationUploader
    {
        public Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
            => Task.FromResult(new UploadResult { Outcome = UploadOutcome.Success });
    }

    private sealed class AcceptanceHarnessConfiguration
    {
        public required Uri BaseUri { get; init; }

        public required string ServiceId { get; init; }

        public required int[] LayerIds { get; init; }

        public required string PackageId { get; init; }

        public required string RunId { get; init; }

        public required string DatabasePath { get; init; }

        public required string ArtifactDirectory { get; init; }

        public string ReplicaName => $"honua-mobile-acceptance-{RunId}";

        public string[] SourceIds => LayerIds.Select(layerId => layerId.ToString()).ToArray();

        public string? ApiKey { get; init; }

        public string? BearerToken { get; init; }

        public bool VerifyCloudReadback { get; init; } = true;

        public static AcceptanceHarnessConfiguration Loopback(Uri baseUri, string artifactDirectory)
            => new()
            {
                BaseUri = baseUri,
                ServiceId = DefaultServiceId,
                LayerIds = [0],
                PackageId = DefaultPackageId,
                RunId = "loopback-disconnected-field-workflow",
                DatabasePath = Path.Combine(artifactDirectory, "loopback-field-workflow.gpkg"),
                ArtifactDirectory = artifactDirectory,
            };

        public static AcceptanceHarnessConfiguration? TryLoadCloudFromEnvironment()
        {
            var enabled = Environment.GetEnvironmentVariable("HONUA_MOBILE_CLOUD_ACCEPTANCE");
            if (!string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var baseUrl = RequireEnvironment("HONUA_MOBILE_CLOUD_BASE_URL");
            var serviceId = RequireEnvironment("HONUA_MOBILE_CLOUD_SERVICE_ID");
            var layerIds = ParseLayerIds(Environment.GetEnvironmentVariable("HONUA_MOBILE_CLOUD_LAYER_IDS") ?? "0");
            var artifactDirectory = Environment.GetEnvironmentVariable("HONUA_MOBILE_ACCEPTANCE_EVIDENCE_DIR")
                ?? Path.Combine(Path.GetTempPath(), "honua-mobile-acceptance-evidence");
            var runId = Environment.GetEnvironmentVariable("HONUA_MOBILE_ACCEPTANCE_RUN_ID")
                ?? $"cloud-disconnected-field-workflow-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";

            return new AcceptanceHarnessConfiguration
            {
                BaseUri = new Uri(baseUrl),
                ServiceId = serviceId,
                LayerIds = layerIds,
                PackageId = Environment.GetEnvironmentVariable("HONUA_MOBILE_ACCEPTANCE_PACKAGE_ID") ?? DefaultPackageId,
                RunId = runId,
                DatabasePath = Environment.GetEnvironmentVariable("HONUA_MOBILE_ACCEPTANCE_DATABASE_PATH")
                    ?? Path.Combine(artifactDirectory, $"{runId}.gpkg"),
                ArtifactDirectory = artifactDirectory,
                ApiKey = Environment.GetEnvironmentVariable("HONUA_MOBILE_CLOUD_API_KEY"),
                BearerToken = Environment.GetEnvironmentVariable("HONUA_MOBILE_CLOUD_BEARER_TOKEN"),
                VerifyCloudReadback = ReadBoolean(Environment.GetEnvironmentVariable("HONUA_MOBILE_CLOUD_VERIFY_READBACK"), defaultValue: true),
            };
        }

        private static string RequireEnvironment(string name)
            => Environment.GetEnvironmentVariable(name)
               ?? throw new InvalidOperationException($"{name} is required when HONUA_MOBILE_CLOUD_ACCEPTANCE is enabled.");

        private static int[] ParseLayerIds(string value)
        {
            var layerIds = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToArray();

            if (layerIds.Length == 0)
            {
                throw new InvalidOperationException("HONUA_MOBILE_CLOUD_LAYER_IDS must include at least one layer id.");
            }

            return layerIds;
        }

        private static bool ReadBoolean(string? value, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            throw new InvalidOperationException("HONUA_MOBILE_CLOUD_VERIFY_READBACK must be 1, true, yes, 0, false, or no.");
        }
    }

    private sealed class DisconnectedFieldWorkflowEvidence
    {
        public string SchemaVersion { get; init; } = DisconnectedFieldWorkflowAcceptanceTests.SchemaVersion;

        public string WorkflowName { get; init; } = DisconnectedFieldWorkflowAcceptanceTests.WorkflowName;

        public required string RunId { get; init; }

        public required string ArtifactDirectory { get; init; }

        public required string Status { get; set; }

        public required DateTimeOffset StartedAtUtc { get; init; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public required string PackageId { get; init; }

        public required string ServiceId { get; init; }

        public required List<string> SourceIds { get; init; }

        public List<string> OperationIds { get; } = [];

        public Dictionary<string, string> CursorState { get; } = new(StringComparer.Ordinal);

        public List<AcceptancePhaseEvidence> Phases { get; } = [];

        public AcceptanceFinalStateEvidence FinalState { get; } = new();

        public List<PlannedOperationEvidence> PlannedOperations { get; } = [];

        public List<AcceptanceScenarioEvidence> ScenarioCoverage { get; } = [];

        public IReadOnlyDictionary<string, string> FailureCategories { get; init; } =
            DisconnectedFieldWorkflowAcceptanceTests.FailureCategories();

        public static DisconnectedFieldWorkflowEvidence Started(AcceptanceHarnessConfiguration config, AcceptanceWorkflowPlan plan)
        {
            var evidence = new DisconnectedFieldWorkflowEvidence
            {
                RunId = config.RunId,
                ArtifactDirectory = config.ArtifactDirectory,
                Status = "running",
                StartedAtUtc = DateTimeOffset.UtcNow,
                PackageId = config.PackageId,
                ServiceId = config.ServiceId,
                SourceIds = config.SourceIds.ToList(),
            };
            evidence.OperationIds.AddRange(plan.Operations.Select(operation => operation.OperationId));
            evidence.PlannedOperations.AddRange(plan.Operations.Select(operation => operation.ToEvidence()));
            evidence.ScenarioCoverage.AddRange(plan.ScenarioCoverage);
            evidence.FinalState.GeoPackagePath = config.DatabasePath;
            return evidence;
        }

        public static DisconnectedFieldWorkflowEvidence Skipped(string artifactDirectory, string reason)
        {
            var evidence = new DisconnectedFieldWorkflowEvidence
            {
                RunId = "cloud-disconnected-field-workflow-skipped",
                ArtifactDirectory = artifactDirectory,
                Status = "skipped",
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                PackageId = DefaultPackageId,
                ServiceId = DefaultServiceId,
                SourceIds = ["0"],
            };
            evidence.Phases.Add(new AcceptancePhaseEvidence
            {
                Name = "cloud-fixture-gate",
                Status = "skipped",
                StartedAtUtc = evidence.StartedAtUtc,
                CompletedAtUtc = evidence.CompletedAtUtc,
                Details = { ["reason"] = reason },
            });
            return evidence;
        }

        public static DisconnectedFieldWorkflowEvidence FailedCloudGate(string artifactDirectory, Exception ex)
        {
            var evidence = new DisconnectedFieldWorkflowEvidence
            {
                RunId = "cloud-disconnected-field-workflow-configuration-failed",
                ArtifactDirectory = artifactDirectory,
                Status = "failed",
                StartedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                PackageId = DefaultPackageId,
                ServiceId = DefaultServiceId,
                SourceIds = ["0"],
            };
            evidence.Phases.Add(new AcceptancePhaseEvidence
            {
                Name = "cloud-fixture-gate",
                Status = "failed",
                StartedAtUtc = evidence.StartedAtUtc,
                CompletedAtUtc = evidence.CompletedAtUtc,
                Details =
                {
                    ["error"] = FormatExceptionForEvidence(ex),
                    ["failureCategory"] = ClassifyFailure(ex, "cloud-fixture-gate"),
                },
            });
            return evidence;
        }
    }

    private sealed class AcceptancePhaseEvidence
    {
        public required string Name { get; init; }

        public required string Status { get; set; }

        public required DateTimeOffset StartedAtUtc { get; init; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public Dictionary<string, string> Details { get; init; } = new(StringComparer.Ordinal);
    }

    private sealed class AcceptanceFinalStateEvidence
    {
        public string? GeoPackagePath { get; set; }

        public int LocalFeatureCount { get; set; }

        public int PendingOperationCountBeforeReconnect { get; set; }

        public int PendingOperationCount { get; set; }

        public int ManualReviewCount { get; set; }

        public int RetryOperationCount { get; set; }

        public int AttachmentRoundTripCount { get; set; }

        public int FormRepeatEntryCount { get; set; }

        public int FieldDayOperationCount { get; set; }

        public long FieldDaySyncElapsedMilliseconds { get; set; }

        public int RunTaggedFeatureCountBeforeReconnect { get; set; }

        public bool DeleteTargetPresentBeforeReconnect { get; set; }

        public int RunTaggedFeatureCount { get; set; }

        public bool DeleteTargetPresent { get; set; }

        public string? LocalVerification { get; set; }

        public string? PreSyncCloudVerification { get; set; }

        public string? CloudVerification { get; set; }
    }

    private static async Task<CloudReadback> QueryCloudReadbackAsync(AcceptanceHarnessConfiguration config)
    {
        using var client = CreateReadbackClient(config);
        var layerId = config.LayerIds[0];
        var escapedRunId = config.RunId.Replace("'", "''", StringComparison.Ordinal);

        using var runTagged = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = config.ServiceId,
            LayerId = layerId,
            Where = $"honua_acceptance_run = '{escapedRunId}'",
            OutFields = ["objectid", "status", "honua_acceptance_run"],
            ReturnGeometry = false,
            ResultRecordCount = 10,
        });
        using var deleteTarget = await client.QueryFeaturesAsync(new QueryFeaturesRequest
        {
            ServiceId = config.ServiceId,
            LayerId = layerId,
            ObjectIds = [DeleteTargetObjectId],
            OutFields = ["objectid", "status"],
            ReturnGeometry = false,
            ResultRecordCount = 1,
        });

        return CloudReadback.FromQueryResponses(runTagged, deleteTarget);
    }

    private static HonuaMobileClient CreateReadbackClient(AcceptanceHarnessConfiguration config)
    {
        return new HonuaMobileClient(
            new HttpClient(),
            new HonuaMobileClientOptions
            {
                BaseUri = config.BaseUri,
                ApiKey = config.ApiKey,
                BearerToken = config.BearerToken,
                AllowInsecureTransportForDevelopment = config.BaseUri.Scheme == Uri.UriSchemeHttp,
                PreferGrpcForFeatureQueries = false,
                PreferGrpcForFeatureEdits = false,
            });
    }

    private sealed record CloudReadback(
        int RunTaggedFeatureCount,
        IReadOnlyList<string> Statuses,
        bool DeleteTargetPresent)
    {
        public static CloudReadback FromQueryResponses(JsonDocument runTaggedFeatures, JsonDocument deleteTarget)
            => new(
                CountFeatures(runTaggedFeatures.RootElement),
                ReadFeatureStatuses(runTaggedFeatures.RootElement),
                CountFeatures(deleteTarget.RootElement) > 0);

        private static int CountFeatures(JsonElement root)
            => root.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array
                ? features.GetArrayLength()
                : 0;

        private static string[] ReadFeatureStatuses(JsonElement root)
        {
            if (!root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var statuses = new List<string>();
            foreach (var feature in features.EnumerateArray())
            {
                if (feature.TryGetProperty("attributes", out var attributes) &&
                    attributes.TryGetProperty("status", out var status) &&
                    status.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(status.GetString()))
                {
                    statuses.Add(status.GetString()!);
                }
            }

            return statuses.ToArray();
        }
    }
}
