// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Sdk.Field.Tasks;

namespace Honua.Mobile.Offline.Tasks.Tests;

public sealed class OfflineAssignedWorkLiveTests
{
    private const string ConfigVariable = "HONUA_MOBILE_ASSIGNED_WORK_LIVE_CONFIG";
    private const string TasksPath = "/api/v1/tasks";

    // Run against a disposable migrated server with a published form, a planner, and two scoped crew bearer tokens.
    // Point ConfigVariable to a private JSON file with server, serviceId, formId, answers,
    // plannerKeyFile, crewTokenFile, and alternateCrewTokenFile. Trust the server's TLS certificate
    // through the host trust store. Tasks created by the test are kept for server-side readback.
    [LiveServerFact]
    public async Task ReplayAcrossEncryptedReopenUsesAuthoritativeReceipts()
    {
        using var configDocument = JsonDocument.Parse(File.ReadAllText(
            Environment.GetEnvironmentVariable(ConfigVariable)!));
        var config = configDocument.RootElement;
        string Value(string property) => config.GetProperty(property).GetString()!;
        var server = new Uri(Value("server"));
        Assert.Equal(Uri.UriSchemeHttps, server.Scheme);
        var interrupt = new LoseCompletionReceiptHandler();
        using var crew = CreateClient(server, Value("crewTokenFile"), bearer: true, handler: interrupt);
        using var planner = CreateClient(server, Value("plannerKeyFile"));
        using var alternateCrew = CreateClient(server, Value("alternateCrewTokenFile"), bearer: true);
        var crewTasks = new WorkTaskClient(crew);
        var plannerTasks = new WorkTaskClient(planner);
        var directory = Directory.CreateTempSubdirectory("honua-live-tasks-");
        var encryptionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        try
        {
            var assigned = new List<WorkTask>();
            for (var index = 0; index < 5; index++)
            {
                var created = await CreateAsync(crew, Value("serviceId"), Value("formId"));
                var worker = created.GetProperty("createdBy").GetString()!;
                assigned.Add((await plannerTasks.TransitionAsync(created.GetProperty("taskId").GetGuid(),
                    Transition("assign", created.GetProperty("version").GetInt64(), worker))).Task);
            }

            var reassigned = assigned[3];
            var canceled = assigned[4];
            var scope = new AssignedWorkScope(server.AbsoluteUri, assigned[0].Assignee!, assigned[0].ServiceId,
                assigned[0].Assignee!);
            var database = Path.Combine(directory.FullName, "crew.gpkg");
            var secondDatabase = Path.Combine(directory.FullName, "second-device.gpkg");

            async Task<OfflineAssignedWork> OpenAsync(string path, WorkTaskClient client, AssignedWorkScope storeScope)
            {
                var store = new GeoPackageSyncStore(new GeoPackageSyncStoreOptions
                {
                    DatabasePath = path, EncryptionKey = encryptionKey,
                });
                await store.InitializeAsync();
                return new OfflineAssignedWork(store, client, storeScope);
            }

            var device = await OpenAsync(database, crewTasks, scope);
            var secondDevice = await OpenAsync(secondDatabase, crewTasks, scope);
            await device.RefreshAsync();
            await secondDevice.RefreshAsync();
            var alternate = await CreateAsync(alternateCrew, Value("serviceId"), Value("formId"));
            var alternateSubject = alternate.GetProperty("createdBy").GetString()!;
            var alternateTasks = new WorkTaskClient(alternateCrew);
            var unrelated = await alternateTasks.ListAssignedAsync(
                new AssignedWorkTaskQuery { ServiceId = scope.ServiceId, Assignee = alternateSubject });
            Assert.DoesNotContain(unrelated.Items, task => task.TaskId == reassigned.TaskId);

            var first = assigned[0];
            var firstStart = Transition("start", first.Version);
            var secondStart = Transition("start", first.Version);
            await device.QueueTransitionAsync(first.TaskId, firstStart);
            await secondDevice.QueueTransitionAsync(first.TaskId, secondStart);
            await Task.WhenAll(device.ReplayAsync(), secondDevice.ReplayAsync());
            var startRecords = new[]
            {
                Assert.Single((await device.ReadAsync()).Commands),
                Assert.Single((await secondDevice.ReadAsync()).Commands)
            };
            var winner = Assert.Single(startRecords, command => command.Status == AssignedCommandStatus.Confirmed);
            var loser = Assert.Single(startRecords, command => command.Status == AssignedCommandStatus.Conflict);
            Assert.Equal(first.Version + 1, loser.CurrentVersion);
            var repeat = await crewTasks.TransitionAsync(first.TaskId, winner.Transition!);
            Assert.Equal("duplicate", repeat.Outcome);
            Assert.Equal(first.Version + 1, repeat.Task.Version);
            var changed = await Assert.ThrowsAsync<WorkTaskApiException>(() =>
                crewTasks.TransitionAsync(first.TaskId, winner.Transition! with { Reason = "different command body" }));
            Assert.Equal((int)HttpStatusCode.Conflict, changed.HttpStatus);

            foreach (var task in assigned.Skip(1))
                await crewTasks.TransitionAsync(task.TaskId, Transition("start", task.Version));
            await device.RefreshAsync();
            var captured = new Dictionary<Guid, (WorkTask Task, WorkTaskCompletionRequest Request, AssignedAttachmentReference Evidence)>();
            foreach (var task in (await device.ReadAsync()).Tasks.Where(task => assigned.Any(item => item.TaskId == task.TaskId)))
            {
                var completion = new WorkTaskCompletionRequest
                {
                    CommandId = Guid.NewGuid(),
                    ExpectedVersion = task.Version,
                    ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                    Form = task.Form,
                    Answers = config.GetProperty("answers").Clone()
                };
                var evidence = new AssignedAttachmentReference($"local-photo-{task.TaskId:N}");
                await device.QueueCompletionWithAttachmentsAsync(task.TaskId, completion, [evidence]);
                captured.Add(completion.CommandId, (task, completion, evidence));
            }

            Assert.Equal(5, captured.Count);
            var reopened = await OpenAsync(database, crewTasks, scope);
            Assert.Equal(5, (await reopened.ReadAsync()).Commands.Count(command => command.Status == AssignedCommandStatus.Pending));
            Assert.False((await File.ReadAllBytesAsync(database)).AsSpan().StartsWith("SQLite format 3"u8));
            var otherAccount = await OpenAsync(database, new WorkTaskClient(alternateCrew),
                scope with { AccountSubject = alternateSubject, Assignee = alternateSubject });
            Assert.Empty((await otherAccount.ReadAsync()).Commands);

            await plannerTasks.TransitionAsync(reassigned.TaskId, Transition("reassign", reassigned.Version + 1, alternateSubject));
            await plannerTasks.TransitionAsync(canceled.TaskId, Transition("cancel", canceled.Version + 1));

            // Drop the first successful completion response after the real server commits.
            interrupt.Armed = true;
            await Assert.ThrowsAsync<WorkTaskApiException>(() => reopened.ReplayAsync());
            var committed = Assert.IsType<WorkTaskReceipt>(interrupt.Dropped);
            reopened = await OpenAsync(database, crewTasks, scope);
            Assert.Equal(AssignedCommandStatus.Pending, Assert.Single(
                (await reopened.ReadAsync()).Commands, command => command.CommandId == committed.CommandId).Status);
            await reopened.ReplayAsync();

            var final = await (await OpenAsync(database, crewTasks, scope)).ReadAsync();
            var completed = final.Commands.Where(command =>
                command.Completion is not null && command.Status == AssignedCommandStatus.Confirmed).ToArray();
            Assert.Equal(3, completed.Length);
            Assert.Equal(3, completed.Select(command => Assert.IsType<Guid>(command.Receipt?.ServiceRecordId)).Distinct().Count());
            foreach (var command in final.Commands.Where(command => command.Completion is not null))
            {
                var entry = captured[command.CommandId];
                Assert.Equal(entry.Evidence, Assert.Single(command.Attachments));
                Assert.True(JsonElement.DeepEquals(entry.Request.Answers!.Value, command.Completion!.Answers!.Value));
                Assert.Equal(entry.Request.Form, command.Completion?.Form);
                if (command.Status == AssignedCommandStatus.Confirmed)
                {
                    var replay = await crewTasks.CompleteAsync(entry.Task.TaskId, entry.Request);
                    Assert.Equal("duplicate", replay.Outcome);
                    Assert.Equal(command.Receipt!.ServiceRecordId, replay.ServiceRecordId);
                    Assert.Equal(entry.Task.Version + 1, replay.Task.Version);
                }
            }

            var recovered = Assert.Single(completed, command => command.CommandId == committed.CommandId);
            Assert.Equal("duplicate", recovered.Receipt?.Outcome);
            Assert.Equal(committed.ServiceRecordId, recovered.Receipt?.ServiceRecordId);
            var denied = Assert.Single(final.Commands, command => command.TaskId == reassigned.TaskId);
            Assert.Equal(AssignedCommandStatus.Rejected, denied.Status);
            Assert.Equal(403, denied.HttpStatus);
            var stale = Assert.Single(final.Commands, command => command.TaskId == canceled.TaskId);
            Assert.Equal(AssignedCommandStatus.Conflict, stale.Status);
            Assert.Equal(canceled.Version + 2, stale.CurrentVersion);
            Assert.DoesNotContain(final.Tasks, task => task.TaskId == reassigned.TaskId || task.TaskId == canceled.TaskId);
            var newAssignment = await alternateTasks.ListAssignedAsync(
                new AssignedWorkTaskQuery { ServiceId = scope.ServiceId, Assignee = alternateSubject });
            Assert.Contains(newAssignment.Items, task => task.TaskId == reassigned.TaskId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static HttpClient CreateClient(Uri server, string credentialsFile, bool bearer = false,
        HttpMessageHandler? handler = null)
    {
        var client = new HttpClient(handler ?? new HttpClientHandler()) { BaseAddress = server };
        var credentials = File.ReadAllText(credentialsFile).Trim();
        if (bearer)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials);
        else
            client.DefaultRequestHeaders.Add("X-API-Key", credentials);
        return client;
    }

    private static WorkTaskTransitionRequest Transition(string action, long expectedVersion, string? assignee = null)
        => new()
        {
            CommandId = Guid.NewGuid(),
            ExpectedVersion = expectedVersion,
            Action = action,
            Assignee = assignee,
            Reason = action == "cancel" ? "Testing queued completion after cancellation" : null
        };

    private static async Task<JsonElement> CreateAsync(HttpClient client, string serviceId, string formId)
    {
        using var response = await client.PostAsJsonAsync(TasksPath, new
        {
            serviceId,
            formId,
            commandId = Guid.NewGuid(),
            taskId = Guid.NewGuid(),
            type = "request",
            title = "Native encrypted replay verification",
            location = new { type = "Point", coordinates = new[] { -156.47, 20.89 } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("task");
    }

    private sealed class LoseCompletionReceiptHandler() : DelegatingHandler(new HttpClientHandler())
    {
        public bool Armed { get; set; }
        public WorkTaskReceipt? Dropped { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (!Armed || !response.IsSuccessStatusCode ||
                !request.RequestUri!.AbsolutePath.EndsWith("/completion", StringComparison.Ordinal))
                return response;

            using (response)
                Dropped = await response.Content.ReadFromJsonAsync<WorkTaskReceipt>(ct);
            Armed = false;
            throw new HttpRequestException("Test dropped the successful completion response.");
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
