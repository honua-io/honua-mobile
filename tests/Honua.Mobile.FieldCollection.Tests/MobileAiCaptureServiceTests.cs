using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services;
using Honua.Mobile.FieldCollection.Services.Ai;
using Honua.Sdk.Field.Forms;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class MobileAiCaptureServiceTests
{
    [Fact]
    public async Task RequestFieldSuggestionsAsync_WhenProviderUnavailable_QueuesSanitizedIntent()
    {
        var settings = new InMemorySettingsService();
        var queue = new SettingsMobileAiCaptureQueue(settings);
        var service = new MobileAiCaptureCoordinator(new RecordingAiProvider { Available = false }, queue);

        var result = await service.RequestFieldSuggestionsAsync(new MobileAiCaptureRequest
        {
            Policy = new MobileAiCapturePolicy { IsEnabled = true },
            LayerId = 4,
            FeatureId = "asset-1",
            FormId = "inspection",
            VoiceTranscript = "replace pump seal",
            CurrentValues = new Dictionary<string, object?>
            {
                ["apiKey"] = "secret-value",
                ["notes"] = "visible to provider only"
            },
            Fields =
            [
                new MobileAiFormFieldDescriptor
                {
                    TargetKey = "condition",
                    FieldId = "condition",
                    Label = "Condition",
                    FieldType = FormFieldType.Text,
                    IsRequired = true
                }
            ],
            Attachments =
            [
                new MobileAiAttachmentDescriptor
                {
                    AttachmentId = "photo-1",
                    FileName = "asset.jpg",
                    ContentType = "image/jpeg",
                    PayloadKind = AttachmentPayloadKind.Photo,
                    LocalPath = "/private/mobile/photo.jpg"
                }
            ],
            Capabilities = new HashSet<MobileAiCaptureCapability>
            {
                MobileAiCaptureCapability.VoiceToFields,
                MobileAiCaptureCapability.PhotoToFields
            }
        });

        Assert.Equal(MobileAiCaptureStatus.Queued, result.Status);
        var item = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal(["condition"], item.TargetKeys);
        Assert.Equal(["photo-1"], item.AttachmentIds);

        var queuedJson = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("replace pump seal", queuedJson);
        Assert.DoesNotContain("secret-value", queuedJson);
        Assert.DoesNotContain("/private/mobile/photo.jpg", queuedJson);
    }

    [Fact]
    public async Task RequestFieldSuggestionsAsync_WithProvider_ReturnsSuggestions()
    {
        var provider = new RecordingAiProvider
        {
            Available = true,
            FieldResult = new MobileAiCaptureResult
            {
                Status = MobileAiCaptureStatus.Completed,
                Suggestions =
                [
                    new MobileAiFieldSuggestion
                    {
                        TargetKey = "condition",
                        SuggestedValue = "needs repair",
                        Confidence = 0.84,
                        Reason = "photo"
                    }
                ]
            }
        };
        var service = new MobileAiCaptureCoordinator(provider, new SettingsMobileAiCaptureQueue(new InMemorySettingsService()));

        var result = await service.RequestFieldSuggestionsAsync(new MobileAiCaptureRequest
        {
            Policy = new MobileAiCapturePolicy { IsEnabled = true },
            Fields =
            [
                new MobileAiFormFieldDescriptor
                {
                    TargetKey = "condition",
                    FieldId = "condition",
                    Label = "Condition",
                    FieldType = FormFieldType.Text,
                    IsRequired = false
                }
            ]
        });

        Assert.Equal(MobileAiCaptureStatus.Completed, result.Status);
        Assert.Equal("needs repair", Assert.Single(result.Suggestions).SuggestedValue);
        Assert.Single(provider.FieldRequests);
    }

    [Fact]
    public async Task RequestMediaEnrichmentAsync_WhenProviderUnavailable_QueuesMediaState()
    {
        var settings = new InMemorySettingsService();
        var queue = new SettingsMobileAiCaptureQueue(settings);
        var service = new MobileAiCaptureCoordinator(new RecordingAiProvider { Available = false }, queue);

        var state = await service.RequestMediaEnrichmentAsync(new MobileAiMediaRequest
        {
            Policy = new MobileAiCapturePolicy { IsEnabled = true },
            LayerId = 4,
            FeatureId = "asset-1",
            Attachment = new MobileAiAttachmentDescriptor
            {
                AttachmentId = "photo-1",
                FileName = "asset.jpg",
                ContentType = "image/jpeg",
                PayloadKind = AttachmentPayloadKind.Photo,
                LocalPath = "/private/mobile/photo.jpg"
            }
        });

        Assert.Equal(MobileAiMediaProcessingStatus.Queued, state.RedactionStatus);
        Assert.Equal(MobileAiMediaProcessingStatus.Queued, state.EnrichmentStatus);
        var item = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal(["photo-1"], item.AttachmentIds);
        Assert.Contains(MobileAiCaptureCapability.MediaRedaction, item.Capabilities);
    }

    [Fact]
    public async Task SettingsMobileAiCaptureQueue_RoundTripsThroughJsonSettings()
    {
        var queue = new SettingsMobileAiCaptureQueue(new JsonSettingsService());

        await queue.EnqueueAsync(new MobileAiCaptureQueueItem
        {
            QueueItemId = "queued-1",
            LayerId = 4,
            FeatureId = "asset-1",
            FormId = "inspection",
            TargetKeys = ["condition"],
            AttachmentIds = ["photo-1"],
            Capabilities =
            [
                MobileAiCaptureCapability.VoiceToFields,
                MobileAiCaptureCapability.MediaRedaction
            ]
        });

        var item = Assert.Single(await queue.GetPendingAsync());
        Assert.Equal("queued-1", item.QueueItemId);
        Assert.Equal(["condition"], item.TargetKeys);
        Assert.Equal(["photo-1"], item.AttachmentIds);
        Assert.Contains(MobileAiCaptureCapability.MediaRedaction, item.Capabilities);
    }

    [Fact]
    public void MobileAiMediaState_DoesNotPersistComputedSummary()
    {
        var json = JsonSerializer.Serialize(new MobileAiMediaState
        {
            RedactionStatus = MobileAiMediaProcessingStatus.Queued,
            EnrichmentStatus = MobileAiMediaProcessingStatus.Completed,
            RequiresFaceBlur = true
        });

        Assert.DoesNotContain("Summary", json);
    }

    private sealed class RecordingAiProvider : IMobileAiCaptureProvider
    {
        public bool Available { get; init; }
        public bool IsAvailable => Available;
        public MobileAiCaptureResult FieldResult { get; init; } =
            new() { Status = MobileAiCaptureStatus.Completed };
        public MobileAiMediaState MediaState { get; init; } =
            new()
            {
                RedactionStatus = MobileAiMediaProcessingStatus.Completed,
                EnrichmentStatus = MobileAiMediaProcessingStatus.Completed
            };
        public List<MobileAiCaptureRequest> FieldRequests { get; } = [];
        public List<MobileAiMediaRequest> MediaRequests { get; } = [];

        public ValueTask<MobileAiCaptureResult> RequestFieldSuggestionsAsync(
            MobileAiCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            FieldRequests.Add(request);
            return ValueTask.FromResult(FieldResult);
        }

        public ValueTask<MobileAiMediaState> RequestMediaEnrichmentAsync(
            MobileAiMediaRequest request,
            CancellationToken cancellationToken = default)
        {
            MediaRequests.Add(request);
            return ValueTask.FromResult(MediaState);
        }
    }

    private sealed class JsonSettingsService : ISettingsService
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                return Task.FromResult(defaultValue);
            }

            return Task.FromResult(JsonSerializer.Deserialize<T>(value) ?? defaultValue);
        }

        public Task SetSettingAsync<T>(string key, T value)
        {
            _values[key] = JsonSerializer.Serialize(value);
            return Task.CompletedTask;
        }

        public Task RemoveSettingAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> HasSettingAsync(string key)
        {
            return Task.FromResult(_values.ContainsKey(key));
        }
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public Task<T> GetSettingAsync<T>(string key, T defaultValue = default!)
        {
            return Task.FromResult(_values.TryGetValue(key, out var value) && value is T typed
                ? typed
                : defaultValue);
        }

        public Task SetSettingAsync<T>(string key, T value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveSettingAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> HasSettingAsync(string key)
        {
            return Task.FromResult(_values.ContainsKey(key));
        }
    }
}
