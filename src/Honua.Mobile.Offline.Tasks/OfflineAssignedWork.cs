// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Sdk.Field.Tasks;

namespace Honua.Mobile.Offline.Tasks;

/// <summary>Identifies the authenticated account and service that own the local work.</summary>
public sealed record AssignedWorkScope(string Server, string AccountSubject, string ServiceId, string Assignee);

/// <summary>The durable outcome of replaying an assigned-work command.</summary>
public enum AssignedCommandStatus
{
    Pending,
    Confirmed,
    Conflict,
    Rejected
}

/// <summary>A queued command and any authoritative replay evidence.</summary>
public sealed record AssignedCommand(
    Guid TaskId,
    Guid CommandId,
    WorkTaskTransitionRequest? Transition,
    WorkTaskCompletionRequest? Completion,
    AssignedCommandStatus Status = AssignedCommandStatus.Pending,
    WorkTaskReceipt? Receipt = null,
    int? HttpStatus = null,
    long? CurrentVersion = null,
    string? Reason = null);

/// <summary>The last fully downloaded assignment list and the local command journal.</summary>
public sealed record AssignedWorkSnapshot(IReadOnlyList<WorkTask> Tasks, IReadOnlyList<AssignedCommand> Commands)
{
    /// <summary>The local revision of the authoritative task list, used when publishing refreshes.</summary>
    public long TaskRevision { get; init; }
}

/// <summary>Persists and replays assigned work for one authenticated account in the initialized offline store.</summary>
public sealed class OfflineAssignedWork
{
    private const string Collection = "honua.assigned-work.v1";
    private readonly GeoPackageSyncStore _store;
    private readonly WorkTaskClient _client;
    private readonly AssignedWorkScope _scope;
    private readonly string _id;

    /// <summary>Use a task client configured for the given stable server and authenticated account scope.</summary>
    public OfflineAssignedWork(GeoPackageSyncStore store, WorkTaskClient client, AssignedWorkScope scope)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Server);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.AccountSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.ServiceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Assignee);
        _store = store;
        _client = client;
        _scope = scope with { ServiceId = scope.ServiceId.Trim(), Assignee = scope.Assignee.Trim() };
        _id = JsonSerializer.Serialize(_scope, AssignedWorkJson.Default.AssignedWorkScope);
    }

    /// <summary>Reads cached assignments and replay evidence without the network.</summary>
    public async Task<AssignedWorkSnapshot> ReadAsync(CancellationToken ct = default)
        => Read(await _store.GetDocumentAsync(Collection, _id, ct).ConfigureAwait(false));

    /// <summary>Downloads all pages and publishes a complete replacement. Failed refreshes preserve the previous list.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        while (true)
        {
            var revision = (await ReadAsync(ct).ConfigureAwait(false)).TaskRevision;
            var tasks = new Dictionary<Guid, WorkTask>();
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            do
            {
                var page = await _client.ListAssignedAsync(new AssignedWorkTaskQuery
                {
                    ServiceId = _scope.ServiceId,
                    Assignee = _scope.Assignee,
                    Cursor = cursor
                }, ct).ConfigureAwait(false);
                foreach (var task in page.Items)
                {
                    if (!string.Equals(task.ServiceId, _scope.ServiceId, StringComparison.Ordinal) ||
                        !string.Equals(task.Assignee, _scope.Assignee, StringComparison.Ordinal))
                        throw new InvalidDataException("The server returned a task outside the assigned-work scope.");

                    if (!tasks.TryGetValue(task.TaskId, out var prior) || task.Version >= prior.Version)
                        tasks[task.TaskId] = task;
                }

                cursor = page.NextCursor;
                if (cursor is not null && !cursors.Add(cursor))
                    throw new InvalidDataException("The server repeated an assigned-work cursor.");
            } while (cursor is not null);

            if (await UpdateAsync(state => state.TaskRevision != revision
                    ? null
                    : state with { Tasks = tasks.Values.ToArray(), TaskRevision = revision + 1 }, ct).ConfigureAwait(false))
                return;
        }
    }

    /// <summary>Persists a transition before upload. Use a fresh command ID for a newly chosen action.</summary>
    public Task QueueTransitionAsync(Guid taskId, WorkTaskTransitionRequest request, CancellationToken ct = default)
        => QueueAsync(new(taskId, request.CommandId, request, null), request.ExpectedVersion, ct);

    /// <summary>Persists a completion and its captured form answers before upload.</summary>
    public Task QueueCompletionAsync(Guid taskId, WorkTaskCompletionRequest request, CancellationToken ct = default)
        => QueueAsync(new(taskId, request.CommandId, null, request), request.ExpectedVersion, ct);

    private Task QueueAsync(AssignedCommand command, long expectedVersion, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(command.TaskId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(command.CommandId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion);
        return UpdateAsync(state =>
        {
            var existing = state.Commands.FirstOrDefault(item => item.CommandId == command.CommandId);
            if (existing is not null)
            {
                var original = existing with
                {
                    Status = AssignedCommandStatus.Pending, Receipt = null, HttpStatus = null,
                    CurrentVersion = null, Reason = null
                };
                if (Serialize(original) != Serialize(command))
                    throw new InvalidOperationException("The command ID is already bound to a different request.");
                return null;
            }

            if (!state.Tasks.Any(task => task.TaskId == command.TaskId && task.Version == expectedVersion))
                throw new InvalidOperationException("Refresh the assigned task before queuing a command for this version.");
            if (state.Commands.Any(item => item.TaskId == command.TaskId && item.Status == AssignedCommandStatus.Pending))
                throw new InvalidOperationException("This task already has a pending command.");

            return state with { Commands = [.. state.Commands, command] };
        }, ct);
    }

    /// <summary>Uploads the pending commands with their original IDs and expected versions.</summary>
    public async Task ReplayAsync(CancellationToken ct = default)
    {
        var state = await ReadAsync(ct).ConfigureAwait(false);
        foreach (var command in state.Commands.Where(item => item.Status == AssignedCommandStatus.Pending))
        {
            ct.ThrowIfCancellationRequested();
            AssignedCommand outcome;
            try
            {
                var receipt = command.Transition is { } transition
                    ? await _client.TransitionAsync(command.TaskId, transition, ct).ConfigureAwait(false)
                    : await _client.CompleteAsync(command.TaskId, command.Completion!, ct).ConfigureAwait(false);
                if (!string.Equals(receipt.Task.ServiceId, _scope.ServiceId, StringComparison.Ordinal))
                    throw new InvalidDataException("The task receipt refers to another service.");
                outcome = command with { Status = AssignedCommandStatus.Confirmed, Receipt = receipt };
            }
            catch (WorkTaskVersionConflictException ex)
            {
                outcome = command with
                {
                    Status = AssignedCommandStatus.Conflict, HttpStatus = 409,
                    CurrentVersion = ex.CurrentVersion, Reason = ex.Message
                };
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.BadRequest
                or HttpStatusCode.UnprocessableEntity)
            {
                outcome = command with { Status = AssignedCommandStatus.Rejected, HttpStatus = (int?)ex.StatusCode };
            }

            await UpdateAsync(latest => ApplyOutcome(latest, outcome), ct).ConfigureAwait(false);
        }
    }

    private AssignedWorkSnapshot? ApplyOutcome(AssignedWorkSnapshot state, AssignedCommand outcome)
    {
        if (!state.Commands.Any(item => item.CommandId == outcome.CommandId && item.Status == AssignedCommandStatus.Pending))
            return null;

        var next = state with
        {
            Commands = state.Commands.Select(item => item.CommandId == outcome.CommandId ? outcome : item).ToArray()
        };
        // A receipt may represent an older assignment after a refresh or duplicate replay.
        var cached = state.Tasks.FirstOrDefault(task => task.TaskId == outcome.TaskId);
        if (outcome.HttpStatus is (int)HttpStatusCode.Forbidden or (int)HttpStatusCode.NotFound ||
            (outcome.Status == AssignedCommandStatus.Conflict &&
             (outcome.CurrentVersion is null || cached?.Version < outcome.CurrentVersion)))
        {
            return next with
            {
                Tasks = state.Tasks.Where(item => item.TaskId != outcome.TaskId).ToArray(),
                TaskRevision = state.TaskRevision + 1
            };
        }

        if (cached is null)
            return outcome.Receipt is null ? next : next with { TaskRevision = state.TaskRevision + 1 };
        if (outcome.Receipt?.Task is { } task && task.Version >= cached.Version)
        {
            return next with
            {
                Tasks = string.Equals(task.Assignee, _scope.Assignee, StringComparison.Ordinal)
                    ? state.Tasks.Select(item => item.TaskId == task.TaskId ? task : item).ToArray()
                    : state.Tasks.Where(item => item.TaskId != task.TaskId).ToArray(),
                TaskRevision = state.TaskRevision + 1
            };
        }

        return next;
    }

    private async Task<bool> UpdateAsync(Func<AssignedWorkSnapshot, AssignedWorkSnapshot?> update, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var document = await _store.GetDocumentAsync(Collection, _id, ct).ConfigureAwait(false);
            var next = update(Read(document));
            if (next is null)
                return false;
            if (await _store.CompareExchangeDocumentAsync(
                Collection, new OfflineDocument(_id, JsonSerializer.Serialize(next, AssignedWorkJson.Default.AssignedWorkSnapshot)),
                document?.Json, ct).ConfigureAwait(false))
                return true;
        }
    }

    private static AssignedWorkSnapshot Read(OfflineDocument? document)
        => document is null
            ? new([], [])
            : JsonSerializer.Deserialize(document.Json, AssignedWorkJson.Default.AssignedWorkSnapshot)
                ?? throw new InvalidDataException("The assigned-work document is empty.");

    private static string Serialize(AssignedCommand command)
        => JsonSerializer.Serialize(command, AssignedWorkJson.Default.AssignedCommand);
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AssignedWorkScope))]
[JsonSerializable(typeof(AssignedWorkSnapshot))]
[JsonSerializable(typeof(AssignedCommand))]
internal sealed partial class AssignedWorkJson : JsonSerializerContext;
