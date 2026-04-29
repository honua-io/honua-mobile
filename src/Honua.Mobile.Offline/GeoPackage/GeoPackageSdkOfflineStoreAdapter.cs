using System.Globalization;
using System.Text.Json;
using Honua.Mobile.Offline.Sync;
using Honua.Sdk.Abstractions.Features;
using Honua.Sdk.Offline.Abstractions;

namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>
/// Adapts the mobile GeoPackage sync store to the platform-neutral Honua SDK offline storage contracts.
/// </summary>
public sealed class GeoPackageSdkOfflineStoreAdapter :
    IOfflineFeatureStore,
    IOfflineChangeJournal,
    IOfflineSyncCheckpointStore,
    IOfflineSyncStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IGeoPackageSyncStore _store;

    /// <summary>
    /// Initializes a new <see cref="GeoPackageSdkOfflineStoreAdapter"/>.
    /// </summary>
    /// <param name="store">Mobile GeoPackage sync store.</param>
    public GeoPackageSdkOfflineStoreAdapter(IGeoPackageSyncStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task SaveFeaturesAsync(OfflineFeaturePage page, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        await _store.InitializeAsync(ct).ConfigureAwait(false);
        foreach (var feature in page.Result.Features)
        {
            var featureJson = SerializeFeatureRecord(feature, page.Result.ObjectIdFieldName);
            await _store.UpsertFeatureAsync(page.SourceId, featureJson, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteFeaturesAsync(
        string packageId,
        string sourceId,
        IReadOnlyList<string> featureIds,
        IReadOnlyList<long> objectIds,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        await _store.InitializeAsync(ct).ConfigureAwait(false);
        foreach (var objectId in objectIds)
        {
            await _store.DeleteFeatureAsync(sourceId, objectId, ct).ConfigureAwait(false);
        }

        foreach (var featureId in featureIds)
        {
            if (long.TryParse(featureId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId))
            {
                await _store.DeleteFeatureAsync(sourceId, objectId, ct).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(OfflineChangeJournalEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _store.InitializeAsync(ct).ConfigureAwait(false);
        await _store.EnqueueAsync(ToMobileOperation(entry), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfflineChangeJournalEntry>> GetPendingAsync(
        string packageId,
        int maxCount,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        await _store.InitializeAsync(ct).ConfigureAwait(false);
        var pending = await _store.GetPendingAsync(maxCount, ct).ConfigureAwait(false);
        return pending.Select(operation => ToSdkEntry(packageId, operation)).ToArray();
    }

    /// <inheritdoc />
    public Task MarkSucceededAsync(string operationId, CancellationToken ct = default)
        => _store.MarkSucceededAsync(operationId, ct);

    /// <inheritdoc />
    public Task MarkPendingAsync(string operationId, CancellationToken ct = default)
        => _store.MarkPendingAsync(operationId, ct);

    /// <inheritdoc />
    public Task MarkRetryAsync(OfflineRetryCheckpoint checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return _store.MarkFailedAsync(checkpoint.OperationId, checkpoint.Reason ?? "retryable sync failure", retryable: true, ct);
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(string operationId, string reason, CancellationToken ct = default)
        => _store.MarkFailedAsync(operationId, reason, retryable: false, ct);

    /// <inheritdoc />
    public Task MarkConflictAsync(OfflineConflictEnvelope conflict, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conflict);
        return _store.MarkFailedAsync(
            conflict.OperationId,
            conflict.Reason ?? "conflict requires manual review",
            retryable: false,
            ct);
    }

    /// <inheritdoc />
    public async Task<OfflineSyncCheckpoint?> GetCheckpointAsync(string packageId, string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        await _store.InitializeAsync(ct).ConfigureAwait(false);
        var value = await _store.GetSyncCursorAsync(GetCheckpointKey(packageId, sourceId), ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : JsonSerializer.Deserialize<OfflineSyncCheckpoint>(value, JsonOptions);
    }

    /// <inheritdoc />
    public async Task SaveCheckpointAsync(OfflineSyncCheckpoint checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        await _store.InitializeAsync(ct).ConfigureAwait(false);
        await _store.SetSyncCursorAsync(
            GetCheckpointKey(checkpoint.PackageId, checkpoint.SourceId),
            JsonSerializer.Serialize(checkpoint, JsonOptions),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OfflineSyncState?> GetStateAsync(string packageId, string? sourceId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        await _store.InitializeAsync(ct).ConfigureAwait(false);
        var value = await _store.GetSyncCursorAsync(GetStateKey(packageId, sourceId), ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(value)
            ? null
            : JsonSerializer.Deserialize<OfflineSyncState>(value, JsonOptions);
    }

    /// <inheritdoc />
    public async Task SaveStateAsync(OfflineSyncState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _store.InitializeAsync(ct).ConfigureAwait(false);
        await _store.SetSyncCursorAsync(
            GetStateKey(state.PackageId, state.SourceId),
            JsonSerializer.Serialize(state, JsonOptions),
            ct).ConfigureAwait(false);
    }

    private static OfflineEditOperation ToMobileOperation(OfflineChangeJournalEntry entry)
    {
        var payload = ToPayload(entry);
        return new OfflineEditOperation
        {
            OperationId = entry.OperationId,
            LayerKey = entry.SourceId,
            TargetCollection = payload.CollectionId ?? payload.ServiceId ?? entry.SourceId,
            OperationType = entry.OperationKind switch
            {
                OfflineEditOperationKind.Add => OfflineOperationType.Add,
                OfflineEditOperationKind.Update => OfflineOperationType.Update,
                OfflineEditOperationKind.Delete => OfflineOperationType.Delete,
                _ => throw new InvalidOperationException($"Unsupported SDK offline operation kind '{entry.OperationKind}'."),
            },
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            CreatedAtUtc = entry.CreatedAtUtc,
            AttemptCount = entry.AttemptCount,
        };
    }

    private static OfflineOperationPayload ToPayload(OfflineChangeJournalEntry entry)
    {
        var isOgc = !string.IsNullOrWhiteSpace(entry.Source.CollectionId);
        return new OfflineOperationPayload
        {
            PackageId = entry.PackageId,
            SourceId = entry.SourceId,
            BaseSyncToken = entry.BaseSyncToken,
            Protocol = isOgc ? "ogcfeatures" : "FeatureServer",
            ServiceId = entry.Source.ServiceId,
            LayerId = entry.Source.LayerId,
            CollectionId = entry.Source.CollectionId,
            FeatureId = entry.Feature?.Id ?? entry.DeleteIds.FirstOrDefault(),
            Feature = entry.Feature is null
                ? null
                : isOgc ? ToGeoJsonFeature(entry.Feature) : ToFeatureServerFeature(entry.Feature),
            DeleteObjectIds = entry.DeleteObjectIds,
            DeletesCsv = entry.DeleteObjectIds.Count == 0 ? null : string.Join(',', entry.DeleteObjectIds),
            Metadata = entry.Metadata,
        };
    }

    private static OfflineChangeJournalEntry ToSdkEntry(string fallbackPackageId, OfflineEditOperation operation)
    {
        var payload = DeserializePayload(operation.PayloadJson);
        var source = new FeatureSource
        {
            ServiceId = payload.ServiceId,
            LayerId = payload.LayerId,
            CollectionId = payload.CollectionId,
        };

        return new OfflineChangeJournalEntry
        {
            OperationId = operation.OperationId,
            PackageId = payload.PackageId ?? fallbackPackageId,
            SourceId = payload.SourceId ?? operation.LayerKey,
            Source = source,
            OperationKind = operation.OperationType switch
            {
                OfflineOperationType.Add => OfflineEditOperationKind.Add,
                OfflineOperationType.Update => OfflineEditOperationKind.Update,
                OfflineOperationType.Delete => OfflineEditOperationKind.Delete,
                _ => throw new InvalidOperationException($"Unsupported mobile offline operation type '{operation.OperationType}'."),
            },
            Feature = ToFeatureEditFeature(payload, operation.OperationType),
            DeleteIds = payload.FeatureId is null ? [] : [payload.FeatureId],
            DeleteObjectIds = payload.DeleteObjectIds ?? ParseDeleteCsv(payload.DeletesCsv),
            BaseSyncToken = payload.BaseSyncToken,
            CreatedAtUtc = operation.CreatedAtUtc,
            AttemptCount = operation.AttemptCount,
            Metadata = payload.Metadata ?? new Dictionary<string, string>(),
        };
    }

    private static OfflineOperationPayload DeserializePayload(string payloadJson)
        => JsonSerializer.Deserialize<OfflineOperationPayload>(payloadJson, JsonOptions)
           ?? throw new InvalidOperationException("Offline operation payload cannot be null.");

    private static FeatureEditFeature? ToFeatureEditFeature(OfflineOperationPayload payload, OfflineOperationType operationType)
    {
        if (operationType == OfflineOperationType.Delete)
        {
            return null;
        }

        var feature = payload.Feature;
        if (feature is null && !string.IsNullOrWhiteSpace(payload.AddsJson))
        {
            feature = ReadFirstFeature(payload.AddsJson);
        }

        if (feature is null && !string.IsNullOrWhiteSpace(payload.UpdatesJson))
        {
            feature = ReadFirstFeature(payload.UpdatesJson);
        }

        return feature is null ? null : ToFeatureEditFeature(feature.Value, payload.FeatureId);
    }

    private static JsonElement? ReadFirstFeature(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
        {
            return doc.RootElement[0].Clone();
        }

        return doc.RootElement.Clone();
    }

    private static FeatureEditFeature ToFeatureEditFeature(JsonElement feature, string? fallbackId)
    {
        IReadOnlyDictionary<string, JsonElement> attributes = new Dictionary<string, JsonElement>();
        JsonElement? geometry = null;
        var id = fallbackId;
        long? objectId = null;

        if (feature.TryGetProperty("id", out var idElement))
        {
            id = idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : idElement.GetRawText();
        }

        if (feature.TryGetProperty("properties", out var properties))
        {
            attributes = ReadJsonObject(properties);
        }
        else if (feature.TryGetProperty("attributes", out var featureAttributes))
        {
            attributes = ReadJsonObject(featureAttributes);
        }

        if (feature.TryGetProperty("geometry", out var geometryElement) && geometryElement.ValueKind != JsonValueKind.Null)
        {
            geometry = geometryElement.Clone();
        }

        if (attributes.TryGetValue("objectid", out var objectIdElement) && objectIdElement.TryGetInt64(out var parsedObjectId))
        {
            objectId = parsedObjectId;
        }
        else if (attributes.TryGetValue("OBJECTID", out objectIdElement) && objectIdElement.TryGetInt64(out parsedObjectId))
        {
            objectId = parsedObjectId;
        }

        return new FeatureEditFeature
        {
            Id = id,
            ObjectId = objectId,
            Attributes = attributes,
            Geometry = geometry,
        };
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadJsonObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>();
        }

        return element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private static IReadOnlyList<long> ParseDeleteCsv(string? deletesCsv)
    {
        if (string.IsNullOrWhiteSpace(deletesCsv))
        {
            return [];
        }

        return deletesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => long.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static string SerializeFeatureRecord(FeatureRecord feature, string? objectIdFieldName)
    {
        var attributes = new Dictionary<string, JsonElement>(feature.Attributes, StringComparer.Ordinal);
        var objectIdField = string.IsNullOrWhiteSpace(objectIdFieldName) ? "objectid" : objectIdFieldName;
        if (!attributes.ContainsKey(objectIdField) &&
            !attributes.ContainsKey("objectid") &&
            !attributes.ContainsKey("OBJECTID") &&
            long.TryParse(feature.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var objectId))
        {
            attributes[objectIdField] = JsonSerializer.SerializeToElement(objectId);
        }

        var payload = new Dictionary<string, object?>
        {
            ["attributes"] = attributes,
        };

        if (feature.Geometry is not null)
        {
            payload["geometry"] = feature.Geometry.Value;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static JsonElement ToFeatureServerFeature(FeatureEditFeature feature)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["attributes"] = feature.Attributes,
            ["geometry"] = feature.Geometry,
        }, JsonOptions);

    private static JsonElement ToGeoJsonFeature(FeatureEditFeature feature)
        => JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["type"] = "Feature",
            ["id"] = feature.Id,
            ["properties"] = feature.Attributes,
            ["geometry"] = feature.Geometry,
        }, JsonOptions);

    private static string GetCheckpointKey(string packageId, string sourceId)
        => $"sdk-checkpoint:{packageId}:{sourceId}";

    private static string GetStateKey(string packageId, string? sourceId)
        => string.IsNullOrWhiteSpace(sourceId)
            ? $"sdk-state:{packageId}"
            : $"sdk-state:{packageId}:{sourceId}";
}
