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

/// <summary>A locally cached asset summary read under the active assignment credentials.</summary>
public sealed record AssignedAssetSummary(string AssetId, long? Revision, JsonElement Attributes);

/// <summary>References to captured files managed by the caller's offline attachment store.</summary>
public sealed record AssignedAttachmentReference(string LocalId);

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
    string? Reason = null)
{
    /// <summary>Local evidence references retained independently of the server receipt.</summary>
    public IReadOnlyList<AssignedAttachmentReference> Attachments { get; init; } = [];
}

/// <summary>The last fully downloaded assignment list and the local command journal.</summary>
public sealed record AssignedWorkSnapshot(IReadOnlyList<WorkTask> Tasks, IReadOnlyList<AssignedCommand> Commands)
{
    /// <summary>The local revision of the authoritative task list, used when publishing refreshes.</summary>
    public long TaskRevision { get; init; }

    /// <summary>Authorized asset summaries keyed by current task ID.</summary>
    public IReadOnlyDictionary<Guid, AssignedAssetSummary> AssetSummaries { get; init; } =
        new Dictionary<Guid, AssignedAssetSummary>();
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

    /// <summary>Publishes a complete assignment list. An explicit access denial clears the cached list.</summary>
    public Task RefreshAsync(CancellationToken ct = default) => RefreshCoreAsync(null, ct);

    /// <summary>
    /// Publishes a complete assignment list and summaries loaded using the same authenticated scope.
    /// Return null for an absent or inaccessible asset and throw for an incomplete read.
    /// </summary>
    public Task RefreshWithAssetsAsync(
        Func<AssignedWorkScope, WorkTask, CancellationToken, Task<AssignedAssetSummary?>> readAssetSummary,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(readAssetSummary);
        return RefreshCoreAsync(readAssetSummary, ct);
    }

    private async Task RefreshCoreAsync(
        Func<AssignedWorkScope, WorkTask, CancellationToken, Task<AssignedAssetSummary?>>? readAssetSummary,
        CancellationToken ct)
    {
        while (true)
        {
            var revision = (await ReadAsync(ct).ConfigureAwait(false)).TaskRevision;
            var tasks = new Dictionary<Guid, WorkTask>();
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            do
            {
                WorkTaskPage page;
                try
                {
                    page = await _client.ListAssignedAsync(new AssignedWorkTaskQuery
                    {
                        ServiceId = _scope.ServiceId,
                        Assignee = _scope.Assignee,
                        Cursor = cursor
                    }, ct).ConfigureAwait(false);
                }
                catch (WorkTaskApiException ex) when (ex.HttpStatus == (int)HttpStatusCode.Forbidden)
                {
                    await UpdateAsync(state => state with
                    {
                        Tasks = [], AssetSummaries = new Dictionary<Guid, AssignedAssetSummary>(),
                        TaskRevision = state.TaskRevision + 1
                    }, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
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

            var summaries = new Dictionary<Guid, AssignedAssetSummary>();
            var deniedAssets = new Dictionary<Guid, string>();
            try
            {
                if (readAssetSummary is not null)
                    await ReadSummariesAsync(summaries, deniedAssets, tasks.Values, readAssetSummary, ct).ConfigureAwait(false);
            }
            catch (Exception) when (deniedAssets.Count > 0)
            {
                await InvalidateDeniedSummariesAsync(deniedAssets).ConfigureAwait(false);
                throw;
            }

            if (await UpdateAsync(state => state.TaskRevision != revision
                    ? null
                    : state with
                    {
                        Tasks = tasks.Values.ToArray(), AssetSummaries = summaries, TaskRevision = revision + 1
                    }, deniedAssets.Count > 0 ? CancellationToken.None : ct).ConfigureAwait(false))
                return;

            if (deniedAssets.Count > 0)
                await InvalidateDeniedSummariesAsync(deniedAssets).ConfigureAwait(false);
        }
    }

    private Task InvalidateDeniedSummariesAsync(Dictionary<Guid, string> deniedAssets)
        => UpdateAsync(state =>
        {
            var summaries = state.AssetSummaries.Where(item =>
                !deniedAssets.TryGetValue(item.Key, out var assetId) ||
                !string.Equals(item.Value.AssetId, assetId, StringComparison.Ordinal)).ToDictionary();
            return summaries.Count == state.AssetSummaries.Count
                ? null
                : state with { AssetSummaries = summaries, TaskRevision = state.TaskRevision + 1 };
        }, CancellationToken.None);

    private async Task ReadSummariesAsync(
        Dictionary<Guid, AssignedAssetSummary> summaries,
        Dictionary<Guid, string> deniedAssets,
        IEnumerable<WorkTask> tasks,
        Func<AssignedWorkScope, WorkTask, CancellationToken, Task<AssignedAssetSummary?>> readAssetSummary,
        CancellationToken ct)
    {
        foreach (var task in tasks)
        {
            if (task.AssetId is not { } assetId)
                continue;

            AssignedAssetSummary? summary;
            try
            {
                summary = await readAssetSummary(_scope, task, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                summary = null;
            }

            if (summary is null)
            {
                deniedAssets.Add(task.TaskId, assetId);
                continue;
            }

            if (!string.Equals(summary.AssetId, assetId, StringComparison.Ordinal) ||
                summary.Attributes.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The asset summary does not match the assigned asset.");

            summaries.Add(task.TaskId, summary with { Attributes = summary.Attributes.Clone() });
        }
    }

    /// <summary>Persists a transition before upload. Use a fresh command ID for a newly chosen action.</summary>
    public Task QueueTransitionAsync(Guid taskId, WorkTaskTransitionRequest request, CancellationToken ct = default)
        => QueueAsync(new(taskId, request.CommandId, request, null), request.ExpectedVersion, ct);

    /// <summary>Persists a completion and its captured form answers before upload.</summary>
    public Task QueueCompletionAsync(Guid taskId, WorkTaskCompletionRequest request, CancellationToken ct = default)
        => QueueAsync(new(taskId, request.CommandId, null, request), request.ExpectedVersion, ct);

    /// <summary>Persists a completion and caller-owned durable attachment references in one transaction.</summary>
    public Task QueueCompletionWithAttachmentsAsync(Guid taskId, WorkTaskCompletionRequest request,
        IReadOnlyList<AssignedAttachmentReference> attachments, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        var evidence = attachments.ToArray();
        foreach (var attachment in evidence)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            ArgumentException.ThrowIfNullOrWhiteSpace(attachment.LocalId);
        }

        return QueueAsync(new(taskId, request.CommandId, null, request) { Attachments = evidence },
            request.ExpectedVersion, ct);
    }

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

            var task = state.Tasks.FirstOrDefault(item => item.TaskId == command.TaskId && item.Version == expectedVersion);
            if (task is null)
                throw new InvalidOperationException("Refresh the assigned task before queuing a command for this version.");
            if (command.Completion is { } completion && task.Form != completion.Form)
                throw new InvalidOperationException("Use the pinned form version on the assigned task.");
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
            catch (WorkTaskApiException ex) when (ex.InnerException is HttpRequestException
                { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict
                    or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity })
            {
                outcome = command with { Status = AssignedCommandStatus.Rejected, HttpStatus = ex.HttpStatus };
            }

            // A definitive response must survive cancellation before another command is dispatched.
            await UpdateAsync(latest => ApplyOutcome(latest, outcome), CancellationToken.None).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
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
                AssetSummaries = WithoutSummary(state, outcome.TaskId),
                TaskRevision = state.TaskRevision + 1
            };
        }

        if (cached is null)
            return outcome.Receipt is null ? next : next with { TaskRevision = state.TaskRevision + 1 };
        if (outcome.Receipt?.Task is { } task && task.Version >= cached.Version)
        {
            var stillAssigned = string.Equals(task.Assignee, _scope.Assignee, StringComparison.Ordinal);
            return next with
            {
                Tasks = stillAssigned
                    ? state.Tasks.Select(item => item.TaskId == task.TaskId ? task : item).ToArray()
                    : state.Tasks.Where(item => item.TaskId != task.TaskId).ToArray(),
                AssetSummaries = stillAssigned && task.AssetId == cached.AssetId
                    ? state.AssetSummaries : WithoutSummary(state, task.TaskId),
                TaskRevision = state.TaskRevision + 1
            };
        }

        return next;
    }

    private static IReadOnlyDictionary<Guid, AssignedAssetSummary> WithoutSummary(AssignedWorkSnapshot state, Guid taskId)
        => state.AssetSummaries.Where(item => item.Key != taskId).ToDictionary();

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
