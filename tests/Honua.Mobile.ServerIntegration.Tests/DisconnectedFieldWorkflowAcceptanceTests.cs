using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Mobile.Sdk;
using Honua.Sdk.Offline;
using Honua.Sdk.Offline.Abstractions;

namespace Honua.Mobile.ServerIntegration.Tests;

public sealed class DisconnectedFieldWorkflowAcceptanceTests : IDisposable
{
    private const string SchemaVersion = "honua.mobile.disconnected-field-workflow.evidence.v1";
    private const string WorkflowName = "disconnected-field-workflow";
    private const string DefaultPackageId = "pkg_acceptance_field_workflow";
    private const string DefaultServiceId = "assets";
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
            verifyCloudStateAsync: async evidence =>
            {
                Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/createReplica"));
                Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/extractChanges"));
                Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/synchronizeReplica"));
                Assert.True(server.Received("POST", "/rest/services/assets/FeatureServer/0/applyEdits"));

                var applyEdits = server.SingleRequest("POST", "/rest/services/assets/FeatureServer/0/applyEdits");
                Assert.Contains("Offline Pump Acceptance", WebUtility.UrlDecode(applyEdits.Body));
                evidence.FinalState.CloudVerification = "loopback applyEdits observed";
                await Task.CompletedTask;
            });

        Assert.Equal("passed", result.Evidence.Status);
        Assert.True(File.Exists(result.EvidencePath));
        Assert.Equal(0, result.Evidence.FinalState.PendingOperationCount);
        Assert.True(result.Evidence.FinalState.LocalFeatureCount >= 1);
        Assert.Equal("replica-abc-123", result.Evidence.CursorState["replica:assets"]);
        Assert.Equal("100", result.Evidence.CursorState["servergen:assets"]);
        Assert.Equal(["op-acceptance-add-001"], result.Evidence.OperationIds);

        var evidenceJson = await File.ReadAllTextAsync(result.EvidencePath);
        Assert.Contains(SchemaVersion, evidenceJson);
        Assert.Contains("\"online-download\"", evidenceJson);
        Assert.Contains("\"offline-edit\"", evidenceJson);
        Assert.Contains("\"reconnect-sync\"", evidenceJson);
        Assert.Contains("\"verify\"", evidenceJson);
    }

    [Fact]
    [Trait("Category", "CloudAcceptance")]
    public async Task CloudHarness_RunsOnlyWhenExplicitlyConfigured_AndOtherwiseEmitsSkippedEvidence()
    {
        var config = AcceptanceHarnessConfiguration.TryLoadCloudFromEnvironment();
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
            verifyCloudStateAsync: evidence =>
            {
                evidence.FinalState.CloudVerification =
                    "cloud fixture verification delegated to honua-server#895; request-level verification is not available from this process";
                return Task.CompletedTask;
            });

        Assert.Equal("passed", result.Evidence.Status);
        Assert.True(File.Exists(result.EvidencePath));
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
        Func<DisconnectedFieldWorkflowEvidence, Task> verifyCloudStateAsync)
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = config.DatabasePath,
        });

        var evidence = DisconnectedFieldWorkflowEvidence.Started(config);

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
            });

            await RunPhaseAsync(evidence, "offline-edit", async phase =>
            {
                await store.InitializeAsync();
                var operation = CreateAcceptanceOperation(config);
                await store.EnqueueAsync(operation);
                evidence.OperationIds.Add(operation.OperationId);
                phase.Details["queuedOperationId"] = operation.OperationId;
                phase.Details["queuedOperationType"] = operation.OperationType.ToString();
                phase.Details["pendingAfterEdit"] = (await store.CountPendingAsync()).ToString();
            });

            await RunPhaseAsync(evidence, "reconnect-sync", async phase =>
            {
                var sync = new Honua.Mobile.Offline.Sync.OfflineSyncEngine(
                    store,
                    createUploader(),
                    new Honua.Mobile.Offline.Sync.OfflineSyncEngineOptions
                    {
                        BatchSize = 10,
                        ConflictStrategy = SyncConflictStrategy.ManualReview,
                    });

                var result = await sync.SyncAsync();
                phase.Details["loaded"] = result.Loaded.ToString();
                phase.Details["succeeded"] = result.Succeeded.ToString();
                phase.Details["failed"] = result.Failed.ToString();
                phase.Details["failures"] = string.Join(" | ", result.Failures.Select(failure => $"{failure.OperationId}: {failure.Reason}"));

                Assert.Equal(1, result.Loaded);
                Assert.True(result.Succeeded == 1, phase.Details["failures"]);
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

                await verifyCloudStateAsync(evidence);
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
            throw;
        }
        finally
        {
            phase.CompletedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private static OfflineEditOperation CreateAcceptanceOperation(AcceptanceHarnessConfiguration config)
    {
        var layerId = config.LayerIds[0];
        var feature = JsonSerializer.SerializeToElement(new
        {
            attributes = new
            {
                objectid = 2,
                name = "Offline Pump Acceptance",
                status = "inspected-offline",
                honua_acceptance_run = config.RunId,
            },
            geometry = new
            {
                x = -157.8,
                y = 21.3,
            },
        }, JsonOptions);

        return new OfflineEditOperation
        {
            OperationId = "op-acceptance-add-001",
            LayerKey = $"{config.ServiceId}/{layerId}",
            TargetCollection = config.ServiceId,
            OperationType = OfflineOperationType.Add,
            CreatedAtUtc = FixedOperationTime,
            Priority = 1,
            PayloadJson = JsonSerializer.Serialize(new OfflineOperationPayload
            {
                PackageId = config.PackageId,
                SourceId = layerId.ToString(),
                BaseSyncToken = $"servergen:{config.ServiceId}",
                Protocol = "FeatureServer",
                ServiceId = config.ServiceId,
                LayerId = layerId,
                Feature = feature,
                Metadata = new Dictionary<string, string>
                {
                    ["workflow"] = WorkflowName,
                    ["fixtureAssumption"] = "honua-server#895 provides FeatureServer replica and applyEdits endpoints",
                },
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

        public static DisconnectedFieldWorkflowEvidence Started(AcceptanceHarnessConfiguration config)
            => new()
            {
                RunId = config.RunId,
                ArtifactDirectory = config.ArtifactDirectory,
                Status = "running",
                StartedAtUtc = DateTimeOffset.UtcNow,
                PackageId = config.PackageId,
                ServiceId = config.ServiceId,
                SourceIds = config.SourceIds.ToList(),
            };

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

        public int PendingOperationCount { get; set; }

        public string? LocalVerification { get; set; }

        public string? CloudVerification { get; set; }
    }
}
