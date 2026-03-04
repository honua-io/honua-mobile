using System.Net;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Models;

namespace Honua.Mobile.Offline.Sync;

public sealed class HonuaApiOfflineOperationUploader : IOfflineOperationUploader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HonuaMobileClient _client;

    public HonuaApiOfflineOperationUploader(HonuaMobileClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<UploadResult> UploadAsync(OfflineEditOperation operation, bool forceWrite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        OfflineOperationPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<OfflineOperationPayload>(operation.PayloadJson, JsonOptions)
                      ?? throw new InvalidOperationException("Payload cannot be null.");
        }
        catch (Exception ex)
        {
            return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = $"Invalid offline payload: {ex.Message}" };
        }

        try
        {
            return payload.Protocol.ToLowerInvariant() switch
            {
                "featureserver" or "esri" => await UploadFeatureServerAsync(operation, payload, forceWrite, ct).ConfigureAwait(false),
                "ogcfeatures" or "ogc" => await UploadOgcAsync(operation, payload, ct).ConfigureAwait(false),
                _ => new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = $"Unsupported protocol '{payload.Protocol}'." },
            };
        }
        catch (HonuaMobileApiException ex)
        {
            return FromStatusCode(ex.StatusCode, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new UploadResult { Outcome = UploadOutcome.RetryableFailure, Message = ex.Message };
        }
        catch (TaskCanceledException ex)
        {
            return new UploadResult { Outcome = UploadOutcome.RetryableFailure, Message = ex.Message };
        }
    }

    private async Task<UploadResult> UploadFeatureServerAsync(
        OfflineEditOperation operation,
        OfflineOperationPayload payload,
        bool forceWrite,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload.ServiceId) || payload.LayerId is null)
        {
            return new UploadResult
            {
                Outcome = UploadOutcome.FatalFailure,
                Message = "FeatureServer payload requires serviceId and layerId.",
            };
        }

        var addsJson = payload.AddsJson;
        var updatesJson = payload.UpdatesJson;
        var deletesCsv = payload.DeletesCsv;

        if (operation.OperationType == OfflineOperationType.Add && string.IsNullOrWhiteSpace(addsJson))
        {
            addsJson = WrapSingleFeature(payload.Feature, "Add operation requires feature payload.");
        }

        if (operation.OperationType == OfflineOperationType.Update && string.IsNullOrWhiteSpace(updatesJson))
        {
            updatesJson = WrapSingleFeature(payload.Feature, "Update operation requires feature payload.");
        }

        if (operation.OperationType == OfflineOperationType.Delete && string.IsNullOrWhiteSpace(deletesCsv))
        {
            deletesCsv = payload.DeleteObjectIds is { Count: > 0 }
                ? string.Join(',', payload.DeleteObjectIds)
                : throw new InvalidOperationException("Delete operation requires deleteObjectIds or deletesCsv.");
        }

        using var response = await _client.ApplyEditsAsync(new ApplyEditsRequest
        {
            ServiceId = payload.ServiceId,
            LayerId = payload.LayerId.Value,
            AddsJson = addsJson,
            UpdatesJson = updatesJson,
            DeletesCsv = deletesCsv,
            RollbackOnFailure = false,
            ForceWrite = forceWrite,
        }, ct).ConfigureAwait(false);

        return ParseApplyEditsResponse(response.RootElement);
    }

    private async Task<UploadResult> UploadOgcAsync(
        OfflineEditOperation operation,
        OfflineOperationPayload payload,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload.CollectionId))
        {
            return new UploadResult
            {
                Outcome = UploadOutcome.FatalFailure,
                Message = "OGC payload requires collectionId.",
            };
        }

        JsonDocument response;
        switch (operation.OperationType)
        {
            case OfflineOperationType.Add:
                response = await _client.CreateOgcItemAsync(new OgcCreateItemRequest
                {
                    CollectionId = payload.CollectionId,
                    Feature = payload.Feature ?? throw new InvalidOperationException("Add operation requires feature payload."),
                }, ct).ConfigureAwait(false);
                break;

            case OfflineOperationType.Update:
                if (!string.IsNullOrWhiteSpace(payload.FeatureId) && payload.Patch is not null)
                {
                    response = await _client.PatchOgcItemAsync(new OgcPatchItemRequest
                    {
                        CollectionId = payload.CollectionId,
                        FeatureId = payload.FeatureId,
                        Patch = payload.Patch.Value,
                    }, ct).ConfigureAwait(false);
                    break;
                }

                response = await _client.ReplaceOgcItemAsync(new OgcReplaceItemRequest
                {
                    CollectionId = payload.CollectionId,
                    FeatureId = payload.FeatureId ?? throw new InvalidOperationException("Update operation requires featureId."),
                    Feature = payload.Feature ?? throw new InvalidOperationException("Update operation requires feature payload."),
                }, ct).ConfigureAwait(false);
                break;

            case OfflineOperationType.Delete:
                response = await _client.DeleteOgcItemAsync(new OgcDeleteItemRequest
                {
                    CollectionId = payload.CollectionId,
                    FeatureId = payload.FeatureId ?? throw new InvalidOperationException("Delete operation requires featureId."),
                }, ct).ConfigureAwait(false);
                break;

            default:
                return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = "Unsupported OGC operation." };
        }

        using (response)
        {
            if (TryReadError(response.RootElement, out var code, out var message))
            {
                return FromErrorCode(code, message);
            }
        }

        return new UploadResult { Outcome = UploadOutcome.Success };
    }

    private static UploadResult ParseApplyEditsResponse(JsonElement root)
    {
        if (TryReadError(root, out var topLevelCode, out var topLevelMessage))
        {
            return FromErrorCode(topLevelCode, topLevelMessage);
        }

        var resultArrays = new[] { "addResults", "updateResults", "deleteResults" };
        foreach (var propertyName in resultArrays)
        {
            if (!root.TryGetProperty(propertyName, out var results) || results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                if (result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                if (result.TryGetProperty("error", out var error) && TryReadError(error, out var code, out var message))
                {
                    return FromErrorCode(code, message);
                }

                return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = "applyEdits result reported failure." };
            }
        }

        return new UploadResult { Outcome = UploadOutcome.Success };
    }

    private static UploadResult FromStatusCode(HttpStatusCode statusCode, string message)
    {
        if (statusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            return new UploadResult { Outcome = UploadOutcome.Conflict, Message = message };
        }

        if (statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 ||
            (int)statusCode >= 500)
        {
            return new UploadResult { Outcome = UploadOutcome.RetryableFailure, Message = message };
        }

        return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = message };
    }

    private static UploadResult FromErrorCode(int? code, string? message)
    {
        if (code is 409 or 412)
        {
            return new UploadResult { Outcome = UploadOutcome.Conflict, Message = message ?? "Conflict" };
        }

        if (code is 408 or 429 || (code.HasValue && code.Value >= 500))
        {
            return new UploadResult { Outcome = UploadOutcome.RetryableFailure, Message = message ?? "Retryable error" };
        }

        return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = message ?? "Fatal error" };
    }

    private static bool TryReadError(JsonElement element, out int? code, out string? message)
    {
        code = null;
        message = null;

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("error", out var nestedError))
        {
            return TryReadError(nestedError, out code, out message);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty("code", out var codeNode) && codeNode.TryGetInt32(out var parsedCode))
        {
            code = parsedCode;
        }

        if (element.TryGetProperty("message", out var messageNode) && messageNode.ValueKind == JsonValueKind.String)
        {
            message = messageNode.GetString();
        }

        return code.HasValue || !string.IsNullOrWhiteSpace(message);
    }

    private static string WrapSingleFeature(JsonElement? feature, string errorMessage)
    {
        if (feature is null)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return JsonSerializer.Serialize(new[] { feature.Value });
    }
}

public sealed class OfflineOperationPayload
{
    public string Protocol { get; init; } = "FeatureServer";

    public string? ServiceId { get; init; }

    public int? LayerId { get; init; }

    public string? CollectionId { get; init; }

    public string? FeatureId { get; init; }

    public JsonElement? Feature { get; init; }

    public JsonElement? Patch { get; init; }

    public string? AddsJson { get; init; }

    public string? UpdatesJson { get; init; }

    public string? DeletesCsv { get; init; }

    public IReadOnlyList<long>? DeleteObjectIds { get; init; }
}
