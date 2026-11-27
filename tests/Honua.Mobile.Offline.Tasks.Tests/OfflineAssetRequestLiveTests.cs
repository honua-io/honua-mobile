// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Sdk.Field.Tasks;

namespace Honua.Mobile.Offline.Tasks.Tests;

public sealed class OfflineAssetRequestLiveTests
{
    private const string ConfigVariable = "HONUA_MOBILE_ASSET_REQUEST_LIVE_CONFIG";

    [LiveServerFact]
    public async Task AssetRequestReplaysAfterServerCommitAndEncryptedReopen()
    {
        using var configDocument = JsonDocument.Parse(File.ReadAllText(
            Environment.GetEnvironmentVariable(ConfigVariable)!));
        var config = configDocument.RootElement;
        string Value(string name) => config.GetProperty(name).GetString()!;
        var server = new Uri(Value("server"));
        Assert.Equal(Uri.UriSchemeHttps, server.Scheme);
        var request = new WorkTaskAssetRequest
        {
            TaskId = Guid.Parse(Value("taskId")),
            CommandId = Guid.Parse(Value("commandId")),
            ServiceId = Value("serviceId"),
            ProfileId = Guid.Parse(Value("profileId")),
            AssetId = Value("assetId"),
            Title = "Inspect asset from offline queue"
        };
        var scope = new AssignedWorkScope(server.AbsoluteUri, Value("subject"), request.ServiceId, Value("subject"));
        var handler = new LoseCreateReceiptHandler();
        using var http = new HttpClient(handler) { BaseAddress = server };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            File.ReadAllText(Value("crewTokenFile")).Trim());

        var directory = Directory.CreateTempSubdirectory("honua-live-asset-request-");
        var path = Path.Combine(directory.FullName, "asset.gpkg");
        var encryptionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        async Task<OfflineAssignedWork> OpenAsync(AssignedWorkScope owner)
        {
            var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
            {
                DatabasePath = path, EncryptionKey = encryptionKey
            });
            await store.InitializeAsync();
            return new OfflineAssignedWork(store, new WorkTaskClient(http), owner);
        }

        try
        {
            var adapter = await OpenAsync(scope);
            await adapter.QueueAssetRequestAsync(request);
            Assert.Equal(0, handler.CreateCalls);
            await Assert.ThrowsAsync<WorkTaskApiException>(() => adapter.ReplayAsync());
            var dropped = Assert.IsType<WorkTaskReceipt>(handler.Dropped);
            Assert.Equal("accepted", dropped.Outcome);
            Assert.Equal(request.TaskId, dropped.Task.TaskId);
            Assert.Equal(request.CommandId, dropped.CommandId);
            Assert.Null(dropped.Task.Assignee);

            var other = await OpenAsync(scope with { AccountSubject = "isolated-local-device-owner" });
            Assert.Empty((await other.ReadAsync()).Commands);
            await other.ReplayAsync();
            Assert.Equal(1, handler.CreateCalls);

            adapter = await OpenAsync(scope);
            Assert.Equal(request, Assert.Single((await adapter.ReadAsync()).Commands).AssetRequest);
            await adapter.ReplayAsync();
            adapter = await OpenAsync(scope);
            var final = await adapter.ReadAsync();
            Assert.Empty(final.Tasks);
            var confirmed = Assert.Single(final.Commands);
            Assert.Equal(AssignedCommandStatus.Confirmed, confirmed.Status);
            var receipt = Assert.IsType<WorkTaskReceipt>(confirmed.Receipt);
            Assert.Equal("duplicate", receipt.Outcome);
            Assert.Equal(dropped.Task.TaskId, receipt.Task.TaskId);
            Assert.Equal(dropped.CommandId, receipt.CommandId);
            Assert.Equal(dropped.Task.Version, receipt.Task.Version);
            Assert.Null(receipt.Task.Assignee);
            await adapter.ReplayAsync();
            Assert.Equal(2, handler.CreateCalls);
            Assert.False((await File.ReadAllBytesAsync(path)).AsSpan().StartsWith("SQLite format 3"u8));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class LoseCreateReceiptHandler() : DelegatingHandler(new HttpClientHandler())
    {
        public int CreateCalls { get; private set; }
        public WorkTaskReceipt? Dropped { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/tasks", request.RequestUri!.AbsolutePath);
            CreateCalls++;
            var response = await base.SendAsync(request, ct);
            if (CreateCalls != 1 || !response.IsSuccessStatusCode)
                return response;

            using (response)
                Dropped = await response.Content.ReadFromJsonAsync<WorkTaskReceipt>(ct);
            throw new HttpRequestException("Test dropped the successful create response.");
        }
    }

    private sealed class LiveServerFactAttribute : FactAttribute
    {
        public LiveServerFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConfigVariable)))
                Skip = $"Set {ConfigVariable} to opt in to the disposable live server test.";
        }
    }
}
