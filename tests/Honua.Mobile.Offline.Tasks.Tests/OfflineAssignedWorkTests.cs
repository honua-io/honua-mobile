// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Sdk.Field.Tasks;

namespace Honua.Mobile.Offline.Tasks.Tests;

public sealed class OfflineAssignedWorkTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"assigned-{Guid.NewGuid():N}.gpkg");
    private static readonly AssignedWorkScope Scope = new("https://honua.test", "subject-1", "assets", "worker");

    [Fact]
    public async Task CompletionSurvivesReceiptLossAndEncryptedReopen()
    {
        var task = MakeTask(Guid.NewGuid());
        var commandId = Guid.NewGuid();
        var inspection = Guid.NewGuid();
        var requests = new List<string>();
        using var http = CreateClient(async request =>
        {
            if (request.Method == HttpMethod.Get)
                return Ok(new WorkTaskPage { Items = [task] });

            Assert.EndsWith($"/{task.TaskId}/completion", request.RequestUri!.AbsolutePath);
            requests.Add(await request.Content!.ReadAsStringAsync());
            if (requests.Count == 1)
                throw new HttpRequestException("Connection closed after commit.");
            return Ok(new WorkTaskReceipt
            {
                Outcome = "duplicate", CommandId = commandId, InspectionId = inspection,
                Task = task with { Status = "completed", Version = 4 }
            });
        });

        var adapter = await OpenAsync(http, Scope);
        await adapter.RefreshAsync();
        await adapter.QueueCompletionAsync(task.TaskId, new WorkTaskCompletionRequest
        {
            CommandId = commandId,
            ExpectedVersion = 3,
            Answers = JsonSerializer.SerializeToElement(new { condition = "private-observation" })
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.ReplayAsync());

        adapter = await OpenAsync(http, Scope);
        Assert.Equal(AssignedCommandStatus.Pending, Assert.Single((await adapter.ReadAsync()).Commands).Status);
        await adapter.ReplayAsync();

        var final = await (await OpenAsync(http, Scope)).ReadAsync();
        var command = Assert.Single(final.Commands);
        Assert.Equal(AssignedCommandStatus.Confirmed, command.Status);
        Assert.Equal(inspection, command.Receipt?.InspectionId);
        Assert.Equal("duplicate", command.Receipt?.Outcome);
        Assert.Equal(4L, Assert.Single(final.Tasks).Version);
        Assert.Equal(2, requests.Count);
        Assert.Equal(requests[0], requests[1]);
        Assert.Contains("private-observation", requests[1], StringComparison.Ordinal);
        Assert.False((await File.ReadAllBytesAsync(_path)).AsSpan().StartsWith("SQLite format 3"u8));
        Assert.Empty((await (await OpenAsync(http, Scope with { AccountSubject = "subject-2" })).ReadAsync()).Commands);
    }

    [Fact]
    public async Task RefreshIsAtomicAndReassignmentPreservesVersionConflict()
    {
        var task = MakeTask(Guid.NewGuid());
        var commandId = Guid.NewGuid();
        var pendingRefresh = false;
        var failPage = false;
        using var http = CreateClient(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var conflict = new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { commandId, currentVersion = 5, reason = "version_conflict" })
                };
                conflict.Headers.Add("Honua-Command-Outcome", "conflict");
                return Task.FromResult(conflict);
            }

            var query = request.RequestUri!.Query;
            Assert.Contains("serviceId=assets", query, StringComparison.Ordinal);
            Assert.Contains("assignee=worker", query, StringComparison.Ordinal);
            if (!pendingRefresh)
                return Task.FromResult(Ok(new WorkTaskPage { Items = [task] }));

            return Task.FromResult(query.Contains("cursor=next", StringComparison.Ordinal)
                ? failPage ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Ok(new WorkTaskPage { Items = [] })
                : Ok(new WorkTaskPage { Items = [], NextCursor = "next" }));
        });

        var adapter = await OpenAsync(http, Scope);
        await adapter.RefreshAsync();
        await adapter.QueueTransitionAsync(task.TaskId, new WorkTaskTransitionRequest
        {
            CommandId = commandId, ExpectedVersion = 3, Action = "start"
        });
        pendingRefresh = true;
        failPage = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.RefreshAsync());
        Assert.Equal(task.TaskId, Assert.Single((await adapter.ReadAsync()).Tasks).TaskId);

        failPage = false;
        await adapter.RefreshAsync();
        await (await OpenAsync(http, Scope)).ReplayAsync();

        var final = await (await OpenAsync(http, Scope)).ReadAsync();
        Assert.Empty(final.Tasks);
        var command = Assert.Single(final.Commands);
        Assert.Equal(commandId, command.CommandId);
        Assert.Equal(3L, command.Transition?.ExpectedVersion);
        Assert.Equal(AssignedCommandStatus.Conflict, command.Status);
        Assert.Equal(409, command.HttpStatus);
        Assert.Equal(5L, command.CurrentVersion);
    }

    [Fact]
    public async Task ReassignmentDenialRemainsVisibleAfterRestartAndRefresh()
    {
        var task = MakeTask(Guid.NewGuid());
        var commandId = Guid.NewGuid();
        var stillAssigned = true;
        var posts = 0;
        using var http = CreateClient(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(Ok(new WorkTaskPage { Items = stillAssigned ? [task] : [] }));

            posts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        });
        var adapter = await OpenAsync(http, Scope);
        await adapter.RefreshAsync();
        await adapter.QueueTransitionAsync(task.TaskId, new WorkTaskTransitionRequest
        {
            CommandId = commandId, ExpectedVersion = 3, Action = "start"
        });

        stillAssigned = false;
        await adapter.ReplayAsync();
        adapter = await OpenAsync(http, Scope);
        Assert.Empty((await adapter.ReadAsync()).Tasks);
        await adapter.RefreshAsync();
        await adapter.ReplayAsync();

        var final = await adapter.ReadAsync();
        Assert.Empty(final.Tasks);
        var command = Assert.Single(final.Commands);
        Assert.Equal(commandId, command.CommandId);
        Assert.Equal(AssignedCommandStatus.Rejected, command.Status);
        Assert.Equal(403, command.HttpStatus);
        Assert.Equal(1, posts);
    }

    [Theory]
    [InlineData("other-service", "worker")]
    [InlineData("assets", "other-worker")]
    public async Task RefreshRejectsTasksFromAnotherScope(string service, string assignee)
    {
        var cached = MakeTask(Guid.NewGuid());
        var outside = MakeTask(Guid.NewGuid()) with { ServiceId = service, Assignee = assignee };
        var invalid = false;
        using var http = CreateClient(_ => Task.FromResult(Ok(new WorkTaskPage
        {
            Items = invalid ? [outside] : [cached]
        })));
        var adapter = await OpenAsync(http, Scope);
        await adapter.RefreshAsync();

        invalid = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => adapter.RefreshAsync());
        Assert.Equal(cached.TaskId, Assert.Single((await adapter.ReadAsync()).Tasks).TaskId);
    }

    private async Task<OfflineAssignedWork> OpenAsync(HttpClient http, AssignedWorkScope scope)
    {
        var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
        {
            DatabasePath = _path, EncryptionKey = "test-assignments"
        });
        await store.InitializeAsync();
        return new OfflineAssignedWork(store, new WorkTaskClient(http), scope);
    }

    private static WorkTask MakeTask(Guid id)
        => new()
        {
            TaskId = id, ServiceId = Scope.ServiceId, Assignee = Scope.Assignee,
            Type = "inspection", Status = "assigned", Version = 3, Title = "Inspect"
        };

    private static HttpResponseMessage Ok<T>(T payload)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };

    private static HttpClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
        => new(new TaskHandler(send)) { BaseAddress = new Uri(Scope.Server) };

    public void Dispose() => File.Delete(_path);

    private sealed class TaskHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request);
    }
}
