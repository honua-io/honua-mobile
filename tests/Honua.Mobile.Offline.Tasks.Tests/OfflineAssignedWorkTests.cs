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
        await Assert.ThrowsAsync<WorkTaskApiException>(() => adapter.ReplayAsync());

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
    public async Task ConflictForAnotherCommandStaysPendingAcrossReopen()
    {
        var task = MakeTask(Guid.NewGuid());
        var commandId = Guid.NewGuid();
        var posts = 0;
        using var http = CreateClient(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(Ok(new WorkTaskPage { Items = [task] }));

            if (++posts == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(new { commandId = Guid.NewGuid(), reason = "version_conflict", currentVersion = 4 })
                };
                response.Headers.Add("Honua-Command-Outcome", "conflict");
                return Task.FromResult(response);
            }

            return Task.FromResult(Ok(new WorkTaskReceipt
            {
                Outcome = "duplicate", CommandId = commandId, Task = task with { Version = 4 }
            }));
        });

        var adapter = await OpenAsync(http, Scope);
        await adapter.RefreshAsync();
        await adapter.QueueTransitionAsync(task.TaskId, new WorkTaskTransitionRequest
        {
            CommandId = commandId, ExpectedVersion = 3, Action = "start"
        });
        var error = await Assert.ThrowsAsync<WorkTaskApiException>(() => adapter.ReplayAsync());
        Assert.Equal(409, error.HttpStatus);
        Assert.IsType<JsonException>(error.InnerException);

        var reopened = await OpenAsync(http, Scope);
        Assert.Equal(AssignedCommandStatus.Pending, Assert.Single((await reopened.ReadAsync()).Commands).Status);
        await reopened.ReplayAsync();
        var confirmed = Assert.Single((await reopened.ReadAsync()).Commands);
        Assert.Equal(AssignedCommandStatus.Confirmed, confirmed.Status);
        Assert.Equal(commandId, confirmed.Receipt?.CommandId);
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
        await Assert.ThrowsAsync<WorkTaskApiException>(() => adapter.RefreshAsync());
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
        await Assert.ThrowsAsync<WorkTaskApiException>(() => adapter.RefreshAsync());
        Assert.Equal(cached.TaskId, Assert.Single((await adapter.ReadAsync()).Tasks).TaskId);
    }

    [Theory]
    [InlineData("asset-forbidden")]
    [InlineData("list-forbidden")]
    [InlineData("omitted")]
    [InlineData("replay-forbidden")]
    public async Task AssetAccessChangesDoNotDiscardPinnedFormOrAttachments(string accessChange)
    {
        var form = new WorkTaskFormReference { FormId = "inspection", Version = 7 };
        var task = MakeTask(Guid.NewGuid()) with { AssetId = "asset-123", Form = form };
        var summary = new AssignedAssetSummary("asset-123", 4, JsonSerializer.SerializeToElement(new { name = "Pump" }));
        var disposition = "authorized";
        using var http = CreateClient(request =>
        {
            if (request.Method == HttpMethod.Post || disposition == "list-forbidden")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            return Task.FromResult(Ok(new WorkTaskPage { Items = disposition == "omitted" ? [] : [task] }));
        });

        Task<AssignedAssetSummary?> ReadAsset(AssignedWorkScope scope, WorkTask assigned, CancellationToken ct)
        {
            Assert.Equal(Scope, scope);
            Assert.Equal(task.TaskId, assigned.TaskId);
            return disposition switch
            {
                "incomplete" => throw new HttpRequestException("Incomplete asset refresh"),
                "asset-forbidden" => throw new HttpRequestException(null, null, HttpStatusCode.Forbidden),
                _ => Task.FromResult<AssignedAssetSummary?>(summary)
            };
        }

        var adapter = await OpenAsync(http, Scope);
        await adapter.RefreshWithAssetsAsync(ReadAsset);
        var completion = new WorkTaskCompletionRequest
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 3,
            Form = form, SummaryRevision = summary.Revision,
            Answers = JsonSerializer.SerializeToElement(new { condition = "observation" })
        };
        var attachment = new AssignedAttachmentReference("local-photo-1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.QueueCompletionWithAttachmentsAsync(
            task.TaskId, completion with { Form = form with { Version = 8 } }, [attachment]));
        await adapter.QueueCompletionWithAttachmentsAsync(task.TaskId, completion, [attachment]);

        disposition = "incomplete";
        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.RefreshWithAssetsAsync(ReadAsset));
        adapter = await OpenAsync(http, Scope);
        var offline = await adapter.ReadAsync();
        Assert.Equal("Pump", offline.AssetSummaries[task.TaskId].Attributes.GetProperty("name").GetString());
        Assert.Equal(form, Assert.Single(offline.Tasks).Form);
        var otherScope = await (await OpenAsync(http, Scope with { AccountSubject = "other" })).ReadAsync();
        Assert.Empty(otherScope.AssetSummaries);

        disposition = accessChange;
        if (accessChange == "replay-forbidden")
            await adapter.ReplayAsync();
        else if (accessChange == "list-forbidden")
            await Assert.ThrowsAsync<WorkTaskApiException>(() => adapter.RefreshWithAssetsAsync(ReadAsset));
        else
            await adapter.RefreshWithAssetsAsync(ReadAsset);

        var revoked = await (await OpenAsync(http, Scope)).ReadAsync();
        Assert.Empty(revoked.AssetSummaries);
        Assert.Equal(accessChange == "asset-forbidden" ? 1 : 0, revoked.Tasks.Count);
        var journal = Assert.Single(revoked.Commands);
        Assert.Equal(form, journal.Completion?.Form);
        Assert.Equal(4L, journal.Completion?.SummaryRevision);
        Assert.Equal("observation", journal.Completion?.Answers?.GetProperty("condition").GetString());
        Assert.Equal(attachment, Assert.Single(journal.Attachments));
    }

    [Fact]
    public async Task ExplicitAssetDenialIsRetainedWhenAnotherAssetRefreshIsIncomplete()
    {
        var inaccessible = MakeTask(Guid.NewGuid()) with { AssetId = "first" };
        var remaining = MakeTask(Guid.NewGuid()) with { AssetId = "second" };
        using var http = CreateClient(_ => Task.FromResult(Ok(new WorkTaskPage { Items = [inaccessible, remaining] })));
        var adapter = await OpenAsync(http, Scope);
        await adapter.RefreshWithAssetsAsync((_, task, _) => Task.FromResult<AssignedAssetSummary?>(
            new(task.AssetId!, null, JsonSerializer.SerializeToElement(new { name = task.AssetId }))));
        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.RefreshWithAssetsAsync((_, task, _) =>
            throw new HttpRequestException(null, null, task.TaskId == inaccessible.TaskId
                ? HttpStatusCode.Forbidden : HttpStatusCode.ServiceUnavailable)));

        var state = await (await OpenAsync(http, Scope)).ReadAsync();
        Assert.Equal(2, state.Tasks.Count);
        Assert.Equal(remaining.TaskId, Assert.Single(state.AssetSummaries).Key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterResponseCheckpointsOutcomeBeforeStoppingReplay(bool forbidden)
    {
        var first = MakeTask(Guid.NewGuid()) with { AssetId = "first-asset" };
        var second = MakeTask(Guid.NewGuid());
        var firstCommandId = Guid.NewGuid();
        var secondCommandId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        var posts = 0;
        using var http = CreateClient(request =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(Ok(new WorkTaskPage { Items = [first, second] }));

            Assert.Contains(first.TaskId.ToString(), request.RequestUri!.AbsolutePath);
            posts++;
            HttpResponseMessage response = new CancelOnDisposeResponse(
                forbidden ? HttpStatusCode.Forbidden : HttpStatusCode.OK, cancellation.Cancel);
            if (!forbidden)
            {
                response.Content = JsonContent.Create(new WorkTaskReceipt
                {
                    CommandId = firstCommandId, Outcome = "accepted",
                    Task = first with { Status = "in_progress", Version = 4 }
                });
            }

            return Task.FromResult(response);
        });

        var adapter = await OpenAsync(http, Scope);
        await adapter.RefreshWithAssetsAsync((_, task, _) => Task.FromResult<AssignedAssetSummary?>(
            task.AssetId is { } assetId
                ? new(assetId, null, JsonSerializer.SerializeToElement(new { name = assetId }))
                : null));
        await adapter.QueueTransitionAsync(first.TaskId, new WorkTaskTransitionRequest
        {
            CommandId = firstCommandId, ExpectedVersion = first.Version, Action = "start"
        });
        await adapter.QueueTransitionAsync(second.TaskId, new WorkTaskTransitionRequest
        {
            CommandId = secondCommandId, ExpectedVersion = second.Version, Action = "start"
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.ReplayAsync(cancellation.Token));

        var reopened = await (await OpenAsync(http, Scope)).ReadAsync();
        Assert.Equal(1, posts);
        var outcome = Assert.Single(reopened.Commands, item => item.CommandId == firstCommandId);
        Assert.Equal(forbidden ? AssignedCommandStatus.Rejected : AssignedCommandStatus.Confirmed, outcome.Status);
        Assert.Equal(AssignedCommandStatus.Pending,
            Assert.Single(reopened.Commands, item => item.CommandId == secondCommandId).Status);
        if (forbidden)
        {
            Assert.Equal((int)HttpStatusCode.Forbidden, outcome.HttpStatus);
            Assert.DoesNotContain(reopened.Tasks, item => item.TaskId == first.TaskId);
            Assert.DoesNotContain(first.TaskId, reopened.AssetSummaries.Keys);
        }
        else
        {
            Assert.Equal(firstCommandId, outcome.Receipt?.CommandId);
            Assert.Equal(4, Assert.Single(reopened.Tasks, item => item.TaskId == first.TaskId).Version);
        }
    }

    private sealed class CancelOnDisposeResponse(HttpStatusCode statusCode, Action cancel)
        : HttpResponseMessage(statusCode)
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                cancel();
        }
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
