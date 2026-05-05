using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class DisconnectedFieldWorkflowAcceptanceTests : IDisposable
{
    private const string SchemaVersion = "honua.mobile.disconnected-field-workflow.evidence.v1";
    private const string WorkflowName = "disconnected-field-workflow";
    private const string DefaultPackageId = "pkg_acceptance_field_workflow";
    private const string DefaultServiceId = "assets";
    private const string FailureCategoryConfiguration = "configuration";
    private const string FailureCategoryPackage = "package";
    private const string FailureCategoryLocalCache = "local-cache";
    private const string FailureCategoryEditQueue = "edit-queue";
    private const string FailureCategoryTransport = "transport";
    private const string FailureCategoryConflict = "conflict";
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
            verifyPreSyncCloudStateAsync: evidence =>
            {
                evidence.FinalState.PreSyncCloudVerification =
                    "cloud fixture pre-sync verification requires fixture query inputs from honua-server#895";
                return Task.CompletedTask;
            },
            verifyPostSyncCloudStateAsync: evidence =>
            {
                evidence.FinalState.CloudVerification =
                    "cloud fixture verification delegated to honua-server#895; request-level verification is not available from this process";
                return Task.CompletedTask;
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

    [Theory]
    [InlineData("cloud-fixture-gate", "HONUA_MOBILE_CLOUD_BASE_URL is required", FailureCategoryConfiguration)]
    [InlineData("online-download", "replica package is missing", FailureCategoryPackage)]
    [InlineData("reconnect-sync", "conflict requires manual review", FailureCategoryConflict)]
    [InlineData("reconnect-sync", "Invalid offline payload", FailureCategoryEditQueue)]
    [InlineData("reconnect-sync", "Connection refused", FailureCategoryTransport)]
    public void FailureClassifier_MapsHarnessFailuresToActionableCategories(
        string phaseName,
        string message,
        string expectedCategory)
    {
        Assert.Equal(expectedCategory, ClassifyFailure(new InvalidOperationException(message), phaseName));
    }

    [Fact]
    public void FailureClassifier_MapsGeoPackageFailuresToLocalCache()
    {
        Assert.Equal(
            FailureCategoryLocalCache,
            ClassifyFailure(new GeoPackageStorageException("GeoPackage feature cache write failed."), "offline-edit"));
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
            phase.Details["error"] = ex.Message;
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

        var message = ex.Message;
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

        if (message.Contains("invalid offline payload", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("operation", StringComparison.OrdinalIgnoreCase))
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

    private static string ClassifySyncFailureReason(string reason)
    {
        if (reason.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("precondition", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryConflict;
        }

        if (reason.Contains("invalid offline payload", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("unsupported protocol", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("operation", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryEditQueue;
        }

        if (reason.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("transport", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryTransport;
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
            [FailureCategoryTransport] = "Network transport, authentication, timeout, or server availability prevented an operation.",
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
                deleteObjectIds: [3]);

            return new AcceptanceWorkflowPlan
            {
                Sequence = ["online-download", "offline-edit", "reconnect-sync", "verify"],
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
                    ["error"] = ex.Message,
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
        public int LocalFeatureCount { get; set; }

        public int PendingOperationCountBeforeReconnect { get; set; }

        public int PendingOperationCount { get; set; }

        public string? LocalVerification { get; set; }

        public string? PreSyncCloudVerification { get; set; }

        public string? CloudVerification { get; set; }
    }
}
