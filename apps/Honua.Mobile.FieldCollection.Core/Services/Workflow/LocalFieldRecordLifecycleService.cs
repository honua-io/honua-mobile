using System.Globalization;
using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Sdk.Field.Projects;
using Honua.Sdk.Field.Records;

namespace Honua.Mobile.FieldCollection.Services.Workflow;

public sealed class LocalFieldRecordLifecycleService
{
    public const string StatusAttribute = "honua_record_status";
    public const string SubmittedAtAttribute = "honua_submitted_at_utc";
    public const string CompletedAtAttribute = "honua_completed_at_utc";
    public const string LifecycleUpdatedAtAttribute = "honua_lifecycle_updated_at_utc";
    public const string LifecycleActorIdAttribute = "honua_lifecycle_actor_id";
    public const string LifecycleActorRoleAttribute = "honua_lifecycle_actor_role";
    public const string LifecycleNoteAttribute = "honua_lifecycle_note";

    private readonly IGeoPackageStorageService _storage;

    public LocalFieldRecordLifecycleService(IGeoPackageStorageService storage)
    {
        _storage = storage;
    }

    public async Task<LocalFieldRecordLifecycleTransitionResult> TransitionAsync(
        int layerId,
        string featureId,
        RecordStatus targetStatus,
        FieldRecordLifecyclePolicy? policy = null,
        string? actorId = null,
        string? actorRole = null,
        string? note = null,
        DateTimeOffset? transitionTimeUtc = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var feature = await _storage.GetFeatureAsync(featureId, layerId).ConfigureAwait(false);
        if (feature is null)
        {
            return new LocalFieldRecordLifecycleTransitionResult
            {
                Succeeded = false,
                ReasonCode = "record-not-found",
                Reason = $"Record '{featureId}' was not found in layer {layerId}.",
                FromStatus = RecordStatus.Draft,
                ToStatus = targetStatus
            };
        }

        var fromStatus = GetStatus(feature);
        var transition = FindTransition(fromStatus, targetStatus, policy);
        if (transition is null || !RoleMatches(transition, actorRole) || !RecordWorkflow.CanTransition(fromStatus, targetStatus))
        {
            return new LocalFieldRecordLifecycleTransitionResult
            {
                Succeeded = false,
                ReasonCode = "invalid-transition",
                Reason = $"Transition from {fromStatus} to {targetStatus} is not allowed by the lifecycle policy.",
                FromStatus = fromStatus,
                ToStatus = targetStatus,
                Feature = feature
            };
        }

        var timestamp = transitionTimeUtc ?? DateTimeOffset.UtcNow;
        var record = ToFieldRecord(feature, fromStatus);
        RecordWorkflow.Transition(record, targetStatus, timestamp);
        ApplyLifecycleAttributes(feature, record, timestamp, actorId, actorRole, note);
        await _storage.UpdateFeatureAsync(feature).ConfigureAwait(false);

        return new LocalFieldRecordLifecycleTransitionResult
        {
            Succeeded = true,
            FromStatus = fromStatus,
            ToStatus = targetStatus,
            Feature = feature
        };
    }

    public static bool CanEdit(Feature feature, FieldRecordLifecyclePolicy? policy = null)
        => CanEdit(GetStatus(feature), policy);

    public static bool CanEdit(RecordStatus status, FieldRecordLifecyclePolicy? policy = null)
    {
        policy ??= FieldRecordLifecyclePolicy.Default;
        return status switch
        {
            RecordStatus.Draft or RecordStatus.ReadyToSubmit or RecordStatus.Reopened => true,
            RecordStatus.Rejected => policy.AllowRejectedEdit,
            RecordStatus.Submitted or RecordStatus.Approved => !policy.ProtectSubmittedRecords,
            RecordStatus.Deleted => false,
            _ => false
        };
    }

    public static RecordStatus GetStatus(Feature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return TryGetAttribute(feature.Attributes, StatusAttribute, out var value) &&
            Enum.TryParse<RecordStatus>(ReadString(value), ignoreCase: true, out var status)
                ? status
                : RecordStatus.Draft;
    }

    public static bool CanTransition(
        RecordStatus from,
        RecordStatus to,
        FieldRecordLifecyclePolicy? policy = null,
        string? actorRole = null)
        => FindTransition(from, to, policy) is { } transition &&
            RoleMatches(transition, actorRole) &&
            RecordWorkflow.CanTransition(from, to);

    private static FieldRecordLifecycleTransition? FindTransition(
        RecordStatus from,
        RecordStatus to,
        FieldRecordLifecyclePolicy? policy)
    {
        policy ??= FieldRecordLifecyclePolicy.Default;
        if (policy.AllowedStatuses.Count > 0 &&
            (!policy.AllowedStatuses.Contains(from) || !policy.AllowedStatuses.Contains(to)))
        {
            return null;
        }

        return policy.AllowedTransitions.FirstOrDefault(transition =>
            transition.From == from && transition.To == to) ??
            (RecordWorkflow.CanTransition(from, to) && policy.AllowedTransitions.Count == 0
                ? new FieldRecordLifecycleTransition { From = from, To = to }
                : null);
    }

    private static bool RoleMatches(FieldRecordLifecycleTransition transition, string? actorRole)
        => string.IsNullOrWhiteSpace(transition.RequiredActorRole) ||
            string.Equals(transition.RequiredActorRole, actorRole, StringComparison.OrdinalIgnoreCase);

    private static FieldRecord ToFieldRecord(Feature feature, RecordStatus status)
        => new()
        {
            RecordId = feature.Id,
            FormId = feature.LayerId.ToString(CultureInfo.InvariantCulture),
            Values = new Dictionary<string, object?>(feature.Attributes, StringComparer.OrdinalIgnoreCase),
            Status = status,
            CreatedAtUtc = feature.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : new DateTimeOffset(DateTime.SpecifyKind(feature.CreatedAt, DateTimeKind.Utc)),
            SubmittedAtUtc = TryReadDateTimeOffset(feature.Attributes, SubmittedAtAttribute, out var submittedAt)
                ? submittedAt
                : null,
            CompletedAtUtc = TryReadDateTimeOffset(feature.Attributes, CompletedAtAttribute, out var completedAt)
                ? completedAt
                : null
        };

    private static void ApplyLifecycleAttributes(
        Feature feature,
        FieldRecord record,
        DateTimeOffset updatedAtUtc,
        string? actorId,
        string? actorRole,
        string? note)
    {
        feature.Attributes[StatusAttribute] = record.Status.ToString();
        feature.Attributes[LifecycleUpdatedAtAttribute] = FormatDateTimeOffset(updatedAtUtc);
        SetOptional(feature.Attributes, SubmittedAtAttribute, record.SubmittedAtUtc);
        SetOptional(feature.Attributes, CompletedAtAttribute, record.CompletedAtUtc);
        SetOptional(feature.Attributes, LifecycleActorIdAttribute, actorId);
        SetOptional(feature.Attributes, LifecycleActorRoleAttribute, actorRole);
        SetOptional(feature.Attributes, LifecycleNoteAttribute, note);
        feature.UpdatedAt = updatedAtUtc.UtcDateTime;
        feature.ModifiedAt = updatedAtUtc.UtcDateTime;
    }

    private static void SetOptional(
        IDictionary<string, object?> attributes,
        string key,
        DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            attributes[key] = FormatDateTimeOffset(value.Value);
        }
        else
        {
            attributes.Remove(key);
        }
    }

    private static void SetOptional(
        IDictionary<string, object?> attributes,
        string key,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            attributes.Remove(key);
        }
        else
        {
            attributes[key] = value.Trim();
        }
    }

    private static bool TryGetAttribute(
        IReadOnlyDictionary<string, object?> attributes,
        string key,
        out object? value)
    {
        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = attribute.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static bool TryReadDateTimeOffset(
        IReadOnlyDictionary<string, object?> attributes,
        string key,
        out DateTimeOffset value)
    {
        value = default;
        return TryGetAttribute(attributes, key, out var raw) &&
            DateTimeOffset.TryParse(
                ReadString(raw),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out value);
    }

    private static string? ReadString(object? value)
        => value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => element.ToString(),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

    private static string FormatDateTimeOffset(DateTimeOffset value)
        => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
