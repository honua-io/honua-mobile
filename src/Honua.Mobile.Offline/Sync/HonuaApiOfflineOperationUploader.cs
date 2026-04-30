using System.Globalization;
using System.Net;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Models;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Uploads offline edit operations to the Honua API, supporting both FeatureServer (Esri-style)
/// and OGC Features API protocols.
/// </summary>
public sealed class HonuaApiOfflineOperationUploader : IOfflineOperationUploader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HonuaMobileClient _client;

    /// <summary>
    /// Initializes a new <see cref="HonuaApiOfflineOperationUploader"/>.
    /// </summary>
    /// <param name="client">The Honua mobile client used to make API calls.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is <see langword="null"/>.</exception>
    public HonuaApiOfflineOperationUploader(HonuaMobileClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
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
        catch (JsonException ex)
        {
            return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = $"Invalid offline payload: {ex.Message}" };
        }
        catch (InvalidOperationException ex)
        {
            return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = $"Invalid offline payload: {ex.Message}" };
        }
        catch (ArgumentNullException ex) when (string.Equals(ex.ParamName, "source", StringComparison.Ordinal))
        {
            return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = "applyEdits response payload is malformed." };
        }
        catch (ArgumentException ex)
        {
            return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = $"Invalid offline payload: {ex.Message}" };
        }
        catch (HonuaMobileApiException ex)
        {
            return FromStatusCode(ex.StatusCode, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            return new UploadResult { Outcome = UploadOutcome.RetryableFailure, Message = ex.Message };
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
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

        var request = new FeatureEditRequest
        {
            Source = new FeatureSource
            {
                ServiceId = payload.ServiceId,
                LayerId = payload.LayerId,
            },
            Adds = operation.OperationType == OfflineOperationType.Add
                ? ResolveFeatureServerFeatures(payload.AddsJson, payload.Feature, "Add operation requires feature payload.")
                : [],
            Updates = operation.OperationType == OfflineOperationType.Update
                ? ResolveFeatureServerFeatures(payload.UpdatesJson, payload.Feature, "Update operation requires feature payload.")
                : [],
            DeleteObjectIds = operation.OperationType == OfflineOperationType.Delete
                ? ResolveFeatureServerDeleteObjectIds(payload)
                : [],
            RollbackOnFailure = false,
            ForceWrite = forceWrite,
        };

        var response = await _client.ApplyEditsAsync(request, ct).ConfigureAwait(false);
        return ToUploadResult(response);
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

        switch (operation.OperationType)
        {
            case OfflineOperationType.Add:
                {
                    var response = await _client.ApplyEditsAsync(new FeatureEditRequest
                    {
                        Source = new FeatureSource { CollectionId = payload.CollectionId },
                        Adds = [ToOgcFeatureEditFeature(payload.Feature, null, "Add operation requires feature payload.")],
                        RollbackOnFailure = false,
                    }, ct).ConfigureAwait(false);
                    return ToUploadResult(response);
                }

            case OfflineOperationType.Update:
                if (!string.IsNullOrWhiteSpace(payload.FeatureId) && payload.Patch is not null)
                {
                    using var patchResponse = await _client.PatchOgcItemAsync(new OgcPatchItemRequest
                    {
                        CollectionId = payload.CollectionId,
                        FeatureId = payload.FeatureId,
                        Patch = payload.Patch.Value,
                    }, ct).ConfigureAwait(false);

                    if (TryReadError(patchResponse.RootElement, out var code, out var message))
                    {
                        return FromErrorCode(code, message);
                    }

                    return new UploadResult { Outcome = UploadOutcome.Success };
                }

                var updateResponse = await _client.ApplyEditsAsync(new FeatureEditRequest
                {
                    Source = new FeatureSource { CollectionId = payload.CollectionId },
                    Updates =
                    [
                        ToOgcFeatureEditFeature(
                            payload.Feature,
                            payload.FeatureId ?? throw new InvalidOperationException("Update operation requires featureId."),
                            "Update operation requires feature payload.")
                    ],
                    RollbackOnFailure = false,
                }, ct).ConfigureAwait(false);
                return ToUploadResult(updateResponse);

            case OfflineOperationType.Delete:
                var deleteResponse = await _client.ApplyEditsAsync(new FeatureEditRequest
                {
                    Source = new FeatureSource { CollectionId = payload.CollectionId },
                    DeleteIds = [payload.FeatureId ?? throw new InvalidOperationException("Delete operation requires featureId.")],
                    RollbackOnFailure = false,
                }, ct).ConfigureAwait(false);
                return ToUploadResult(deleteResponse);

            default:
                return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = "Unsupported OGC operation." };
        }
    }

    private static UploadResult ToUploadResult(FeatureEditResponse response)
    {
        if (response.Error is not null)
        {
            return FromErrorCode(response.Error.Code, response.Error.Message);
        }

        var editResults = (response.AddResults ?? [])
            .Concat(response.UpdateResults ?? [])
            .Concat(response.DeleteResults ?? [])
            .ToArray();
        if (editResults.Length == 0)
        {
            return new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = "applyEdits response is missing edit result arrays." };
        }

        foreach (var result in editResults)
        {
            if (result.Succeeded)
            {
                continue;
            }

            return result.Error is not null
                ? FromErrorCode(result.Error.Code, result.Error.Message)
                : new UploadResult { Outcome = UploadOutcome.FatalFailure, Message = "applyEdits result reported failure." };
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

    private static IReadOnlyList<FeatureEditFeature> ResolveFeatureServerFeatures(
        string? featuresJson,
        JsonElement? singleFeature,
        string errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(featuresJson))
        {
            return ReadFeatureServerFeatures(featuresJson);
        }

        return [ToFeatureServerFeatureEditFeature(singleFeature, errorMessage)];
    }

    private static IReadOnlyList<FeatureEditFeature> ReadFeatureServerFeatures(string featuresJson)
    {
        using var document = JsonDocument.Parse(featuresJson);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().Select(feature => ToFeatureServerFeatureEditFeature(feature, "Feature payload is required.")).ToArray()
            : [ToFeatureServerFeatureEditFeature(document.RootElement, "Feature payload is required.")];
    }

    private static IReadOnlyList<long> ResolveFeatureServerDeleteObjectIds(OfflineOperationPayload payload)
    {
        if (payload.DeleteObjectIds is { Count: > 0 })
        {
            return payload.DeleteObjectIds;
        }

        if (!string.IsNullOrWhiteSpace(payload.DeletesCsv))
        {
            var values = payload.DeletesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var objectIds = new long[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                if (!long.TryParse(values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId))
                {
                    throw new InvalidOperationException($"Delete operation contains an invalid object id '{values[index]}'.");
                }

                objectIds[index] = objectId;
            }

            return objectIds;
        }

        throw new InvalidOperationException("Delete operation requires deleteObjectIds or deletesCsv.");
    }

    private static FeatureEditFeature ToFeatureServerFeatureEditFeature(JsonElement? feature, string errorMessage)
    {
        if (feature is null)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return ToFeatureEditFeature(feature.Value, fallbackId: null, includeFeatureId: false, includeObjectId: true);
    }

    private static FeatureEditFeature ToOgcFeatureEditFeature(JsonElement? feature, string? fallbackId, string errorMessage)
    {
        if (feature is null)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return ToFeatureEditFeature(feature.Value, fallbackId, includeFeatureId: true, includeObjectId: true);
    }

    private static FeatureEditFeature ToFeatureEditFeature(
        JsonElement feature,
        string? fallbackId,
        bool includeFeatureId,
        bool includeObjectId)
    {
        var id = fallbackId;
        if (includeFeatureId && feature.TryGetProperty("id", out var idElement))
        {
            id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : idElement.GetRawText();
        }

        var attributes = feature.TryGetProperty("attributes", out var featureServerAttributes)
            ? ReadJsonObject(featureServerAttributes)
            : feature.TryGetProperty("properties", out var geoJsonProperties)
                ? ReadJsonObject(geoJsonProperties)
                : new Dictionary<string, JsonElement>();
        var objectId = includeObjectId ? TryReadObjectId(attributes) : null;

        JsonElement? geometry = null;
        if (feature.TryGetProperty("geometry", out var geometryElement) && geometryElement.ValueKind != JsonValueKind.Null)
        {
            geometry = geometryElement.Clone();
        }

        return new FeatureEditFeature
        {
            Id = id,
            ObjectId = objectId,
            Attributes = attributes,
            Geometry = geometry,
        };
    }

    private static long? TryReadObjectId(IReadOnlyDictionary<string, JsonElement> attributes)
    {
        foreach (var key in new[] { "OBJECTID", "objectid", "ObjectID", "FID" })
        {
            if (attributes.TryGetValue(key, out var objectId) && objectId.TryGetInt64(out var parsedObjectId))
            {
                return parsedObjectId;
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadJsonObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>();
        }

        return element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
    }
}

/// <summary>
/// Deserialized payload from an <see cref="OfflineEditOperation.PayloadJson"/>,
/// describing the target service and feature data for upload.
/// </summary>
public sealed class OfflineOperationPayload
{
    /// <summary>
    /// SDK offline package identifier when the payload was produced from <c>Honua.Sdk.Offline.Abstractions</c>.
    /// </summary>
    public string? PackageId { get; init; }

    /// <summary>
    /// SDK offline source identifier when the payload was produced from <c>Honua.Sdk.Offline.Abstractions</c>.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Provider sync token observed when the local edit was queued.
    /// </summary>
    public string? BaseSyncToken { get; init; }

    /// <summary>
    /// API protocol to use: <c>"FeatureServer"</c>/<c>"esri"</c> or <c>"ogcfeatures"</c>/<c>"ogc"</c>. Defaults to <c>"FeatureServer"</c>.
    /// </summary>
    public string Protocol { get; init; } = "FeatureServer";

    /// <summary>
    /// Feature service ID (required for FeatureServer protocol).
    /// </summary>
    public string? ServiceId { get; init; }

    /// <summary>
    /// Layer ID within the feature service (required for FeatureServer protocol).
    /// </summary>
    public int? LayerId { get; init; }

    /// <summary>
    /// OGC collection ID (required for OGC protocol).
    /// </summary>
    public string? CollectionId { get; init; }

    /// <summary>
    /// Feature ID for update/delete OGC operations.
    /// </summary>
    public string? FeatureId { get; init; }

    /// <summary>
    /// Full GeoJSON feature for add or replace operations.
    /// </summary>
    public JsonElement? Feature { get; init; }

    /// <summary>
    /// JSON Merge Patch document for OGC PATCH operations.
    /// </summary>
    public JsonElement? Patch { get; init; }

    /// <summary>
    /// Pre-serialized adds JSON array for FeatureServer applyEdits.
    /// </summary>
    public string? AddsJson { get; init; }

    /// <summary>
    /// Pre-serialized updates JSON array for FeatureServer applyEdits.
    /// </summary>
    public string? UpdatesJson { get; init; }

    /// <summary>
    /// Comma-separated object IDs for FeatureServer deletes.
    /// </summary>
    public string? DeletesCsv { get; init; }

    /// <summary>
    /// Typed list of object IDs for delete operations (alternative to <see cref="DeletesCsv"/>).
    /// </summary>
    public IReadOnlyList<long>? DeleteObjectIds { get; init; }

    /// <summary>
    /// SDK or application metadata associated with the queued edit.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
