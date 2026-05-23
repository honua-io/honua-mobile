using System.Text.Json.Serialization;
using Honua.Mobile.FieldCollection.Models;
using Honua.Sdk.Field.Forms;

namespace Honua.Mobile.FieldCollection.Services.Ai;

public enum MobileAiCaptureCapability
{
    VoiceToFields,
    PhotoToFields,
    MediaRedaction
}

public enum MobileAiCaptureStatus
{
    Disabled,
    Unavailable,
    Queued,
    Completed,
    Failed
}

public enum MobileAiSuggestionDecision
{
    Pending,
    Applied,
    Rejected
}

public enum MobileAiMediaProcessingStatus
{
    NotRequested,
    Queued,
    Processing,
    Completed,
    Failed,
    Skipped
}

public sealed record MobileAiCapturePolicy
{
    public bool IsEnabled { get; init; }
    public bool AllowVoiceToFields { get; init; } = true;
    public bool AllowPhotoToFields { get; init; } = true;
    public bool AllowMediaRedaction { get; init; } = true;
    public bool QueueWhenProviderUnavailable { get; init; } = true;
}

public sealed record MobileAiFormFieldDescriptor
{
    public required string TargetKey { get; init; }
    public required string FieldId { get; init; }
    public required string Label { get; init; }
    public FormFieldType FieldType { get; init; }
    public bool IsRequired { get; init; }
    public IReadOnlyList<string> Choices { get; init; } = [];
}

public sealed record MobileAiAttachmentDescriptor
{
    public required string AttachmentId { get; init; }
    public string? FieldId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public AttachmentPayloadKind PayloadKind { get; init; }
    public long SizeBytes { get; init; }
    public string? LocalPath { get; init; }
    public FieldLocationCaptureEvidence? CaptureLocation { get; init; }
    public MobileAiMediaState? AiState { get; init; }

    public static MobileAiAttachmentDescriptor FromAttachment(AttachmentInfo attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new MobileAiAttachmentDescriptor
        {
            AttachmentId = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            PayloadKind = attachment.PayloadKind,
            SizeBytes = attachment.SizeBytes,
            LocalPath = attachment.LocalPath,
            CaptureLocation = attachment.CaptureLocation,
            AiState = attachment.AiMediaState
        };
    }
}

public sealed record MobileAiCaptureRequest
{
    public MobileAiCapturePolicy Policy { get; init; } = new();
    public int LayerId { get; init; }
    public string FeatureId { get; init; } = string.Empty;
    public string? FormId { get; init; }
    public IReadOnlyDictionary<string, object?> CurrentValues { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<MobileAiFormFieldDescriptor> Fields { get; init; } = [];
    public IReadOnlyList<MobileAiAttachmentDescriptor> Attachments { get; init; } = [];
    public string? VoiceTranscript { get; init; }
    public IReadOnlySet<MobileAiCaptureCapability> Capabilities { get; init; } =
        new HashSet<MobileAiCaptureCapability>();
}

public sealed record MobileAiFieldSuggestion
{
    public required string TargetKey { get; init; }
    public string? FieldId { get; init; }
    public object? SuggestedValue { get; init; }
    public double? Confidence { get; init; }
    public string? Reason { get; init; }
    public MobileAiSuggestionDecision Decision { get; init; } = MobileAiSuggestionDecision.Pending;
}

public sealed record MobileAiCaptureResult
{
    public MobileAiCaptureStatus Status { get; init; }
    public string? Message { get; init; }
    public string? QueueItemId { get; init; }
    public IReadOnlyList<MobileAiFieldSuggestion> Suggestions { get; init; } = [];

    public static MobileAiCaptureResult Disabled()
        => new()
        {
            Status = MobileAiCaptureStatus.Disabled,
            Message = "AI assistance is disabled."
        };

    public static MobileAiCaptureResult Queued(string queueItemId)
        => new()
        {
            Status = MobileAiCaptureStatus.Queued,
            QueueItemId = queueItemId,
            Message = "AI assistance is queued until a provider is available."
        };

    public static MobileAiCaptureResult Unavailable()
        => new()
        {
            Status = MobileAiCaptureStatus.Unavailable,
            Message = "No AI capture provider is available."
        };
}

public sealed record MobileAiMediaRequest
{
    public MobileAiCapturePolicy Policy { get; init; } = new();
    public int LayerId { get; init; }
    public string FeatureId { get; init; } = string.Empty;
    public required MobileAiAttachmentDescriptor Attachment { get; init; }
}

public sealed record MobileAiMediaState
{
    public MobileAiMediaProcessingStatus RedactionStatus { get; init; } = MobileAiMediaProcessingStatus.NotRequested;
    public MobileAiMediaProcessingStatus EnrichmentStatus { get; init; } = MobileAiMediaProcessingStatus.NotRequested;
    public bool RequiresFaceBlur { get; init; }
    public string? ProviderId { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string Summary
    {
        get
        {
            if (RequiresFaceBlur && RedactionStatus != MobileAiMediaProcessingStatus.Completed)
            {
                return $"AI redaction {RedactionStatus}";
            }

            if (RedactionStatus != MobileAiMediaProcessingStatus.NotRequested)
            {
                return $"AI redaction {RedactionStatus}";
            }

            if (EnrichmentStatus != MobileAiMediaProcessingStatus.NotRequested)
            {
                return $"AI enrichment {EnrichmentStatus}";
            }

            return string.Empty;
        }
    }

    public static MobileAiMediaState Queued()
        => new()
        {
            RedactionStatus = MobileAiMediaProcessingStatus.Queued,
            EnrichmentStatus = MobileAiMediaProcessingStatus.Queued,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

    public static MobileAiMediaState Disabled()
        => new()
        {
            RedactionStatus = MobileAiMediaProcessingStatus.NotRequested,
            EnrichmentStatus = MobileAiMediaProcessingStatus.NotRequested,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
}

public sealed record MobileAiCaptureQueueItem
{
    public required string QueueItemId { get; init; }
    public int LayerId { get; init; }
    public string FeatureId { get; init; } = string.Empty;
    public string? FormId { get; init; }
    public IReadOnlyList<string> TargetKeys { get; init; } = [];
    public IReadOnlyList<string> AttachmentIds { get; init; } = [];
    public IReadOnlyList<MobileAiCaptureCapability> Capabilities { get; init; } = [];
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Reason { get; init; } = "provider-unavailable";

    public static MobileAiCaptureQueueItem FromFormRequest(MobileAiCaptureRequest request)
    {
        return new MobileAiCaptureQueueItem
        {
            QueueItemId = Guid.NewGuid().ToString("N"),
            LayerId = request.LayerId,
            FeatureId = request.FeatureId,
            FormId = request.FormId,
            TargetKeys = request.Fields
                .Select(field => field.TargetKey)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            AttachmentIds = request.Attachments
                .Select(attachment => attachment.AttachmentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Capabilities = request.Capabilities.Distinct().ToArray()
        };
    }

    public static MobileAiCaptureQueueItem FromMediaRequest(MobileAiMediaRequest request)
    {
        return new MobileAiCaptureQueueItem
        {
            QueueItemId = Guid.NewGuid().ToString("N"),
            LayerId = request.LayerId,
            FeatureId = request.FeatureId,
            AttachmentIds = [request.Attachment.AttachmentId],
            Capabilities =
            [
                MobileAiCaptureCapability.MediaRedaction,
                MobileAiCaptureCapability.PhotoToFields
            ]
        };
    }
}

public interface IMobileAiCaptureProvider
{
    bool IsAvailable { get; }
    ValueTask<MobileAiCaptureResult> RequestFieldSuggestionsAsync(
        MobileAiCaptureRequest request,
        CancellationToken cancellationToken = default);
    ValueTask<MobileAiMediaState> RequestMediaEnrichmentAsync(
        MobileAiMediaRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMobileAiCaptureQueue
{
    ValueTask EnqueueAsync(MobileAiCaptureQueueItem item, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<MobileAiCaptureQueueItem>> GetPendingAsync(CancellationToken cancellationToken = default);
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

public interface IMobileAiCaptureService
{
    ValueTask<MobileAiCaptureResult> RequestFieldSuggestionsAsync(
        MobileAiCaptureRequest request,
        CancellationToken cancellationToken = default);
    ValueTask<MobileAiMediaState> RequestMediaEnrichmentAsync(
        MobileAiMediaRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class NullMobileAiCaptureProvider : IMobileAiCaptureProvider
{
    public bool IsAvailable => false;

    public ValueTask<MobileAiCaptureResult> RequestFieldSuggestionsAsync(
        MobileAiCaptureRequest request,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(MobileAiCaptureResult.Unavailable());

    public ValueTask<MobileAiMediaState> RequestMediaEnrichmentAsync(
        MobileAiMediaRequest request,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(MobileAiMediaState.Queued());
}

public sealed class MobileAiCaptureCoordinator : IMobileAiCaptureService
{
    private readonly IMobileAiCaptureProvider _provider;
    private readonly IMobileAiCaptureQueue _queue;

    public MobileAiCaptureCoordinator(IMobileAiCaptureProvider provider, IMobileAiCaptureQueue queue)
    {
        _provider = provider;
        _queue = queue;
    }

    public async ValueTask<MobileAiCaptureResult> RequestFieldSuggestionsAsync(
        MobileAiCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.Policy.IsEnabled)
        {
            return MobileAiCaptureResult.Disabled();
        }

        if (!_provider.IsAvailable)
        {
            return await QueueOrUnavailableAsync(request, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await _provider.RequestFieldSuggestionsAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new MobileAiCaptureResult
            {
                Status = MobileAiCaptureStatus.Failed,
                Message = "AI assistance failed without exposing capture payload details."
            };
        }
    }

    public async ValueTask<MobileAiMediaState> RequestMediaEnrichmentAsync(
        MobileAiMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!request.Policy.IsEnabled || !request.Policy.AllowMediaRedaction)
        {
            return MobileAiMediaState.Disabled();
        }

        if (!_provider.IsAvailable)
        {
            if (request.Policy.QueueWhenProviderUnavailable)
            {
                await _queue.EnqueueAsync(
                    MobileAiCaptureQueueItem.FromMediaRequest(request),
                    cancellationToken).ConfigureAwait(false);
            }

            return MobileAiMediaState.Queued();
        }

        try
        {
            return await _provider.RequestMediaEnrichmentAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new MobileAiMediaState
            {
                RedactionStatus = MobileAiMediaProcessingStatus.Failed,
                EnrichmentStatus = MobileAiMediaProcessingStatus.Failed,
                LastError = "AI media processing failed without exposing capture payload details.",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private async ValueTask<MobileAiCaptureResult> QueueOrUnavailableAsync(
        MobileAiCaptureRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Policy.QueueWhenProviderUnavailable)
        {
            return MobileAiCaptureResult.Unavailable();
        }

        var item = MobileAiCaptureQueueItem.FromFormRequest(request);
        await _queue.EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
        return MobileAiCaptureResult.Queued(item.QueueItemId);
    }
}

public sealed class SettingsMobileAiCaptureQueue : IMobileAiCaptureQueue
{
    private const string QueueKey = "field-collection:ai-capture-queue";

    private readonly ISettingsService _settingsService;

    public SettingsMobileAiCaptureQueue(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async ValueTask EnqueueAsync(
        MobileAiCaptureQueueItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        var pending = await LoadAsync().ConfigureAwait(false);
        pending.RemoveAll(existing => string.Equals(existing.QueueItemId, item.QueueItemId, StringComparison.Ordinal));
        pending.Add(item);
        await _settingsService.SetSettingAsync(QueueKey, pending).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<MobileAiCaptureQueueItem>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await LoadAsync().ConfigureAwait(false);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask(_settingsService.RemoveSettingAsync(QueueKey));
    }

    private async Task<List<MobileAiCaptureQueueItem>> LoadAsync()
    {
        return await _settingsService
            .GetSettingAsync(QueueKey, new List<MobileAiCaptureQueueItem>())
            .ConfigureAwait(false) ?? [];
    }
}
