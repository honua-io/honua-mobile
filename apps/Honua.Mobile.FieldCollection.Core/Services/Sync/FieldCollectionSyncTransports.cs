using System.Globalization;
using System.Net;
using System.Text.Json;
using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;
using Honua.Mobile.Sdk;
using Honua.Mobile.Sdk.Auth;
using Honua.Sdk.Abstractions.Features;
using Microsoft.Extensions.Logging;
using StorageChangeRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeRecord;
using StorageChangeOperation = Honua.Mobile.FieldCollection.Services.Storage.Models.ChangeOperation;
using StorageConflictRecord = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictRecord;
using StorageConflictType = Honua.Mobile.FieldCollection.Services.Storage.Models.ConflictType;

namespace Honua.Mobile.FieldCollection.Services.Sync;

public interface IFieldCollectionFeatureSyncClient
{
    bool IsConfigured { get; }

    /// <summary>
    /// Applies feature edits, optionally attaching a stable <paramref name="idempotencyKey"/> so the
    /// server dedupes a retried upload (at-most-once, honua-server #2250).
    /// </summary>
    Task<FeatureEditResponse> ApplyEditsAsync(
        FeatureEditRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default);

    Task<FeatureQueryResult> QueryAsync(FeatureQueryRequest request, CancellationToken cancellationToken = default);
}

public interface IFieldCollectionAttachmentSyncClient
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsAsync(
        FeatureAttachmentListRequest request,
        CancellationToken cancellationToken = default);
    Task<FeatureAttachmentContent> DownloadAttachmentAsync(
        FeatureAttachmentDownloadRequest request,
        CancellationToken cancellationToken = default);
    Task<FeatureAttachmentResult> AddAttachmentAsync(
        FeatureAttachmentAddRequest request,
        CancellationToken cancellationToken = default);
    Task<FeatureAttachmentResult> DeleteAttachmentAsync(
        FeatureAttachmentDeleteRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class HonuaSdkFieldCollectionFeatureSyncClient :
    IFieldCollectionFeatureSyncClient,
    IFieldCollectionAttachmentSyncClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuthenticationService _authenticationService;
    private readonly ILogger<HonuaSdkFieldCollectionFeatureSyncClient>? _logger;

    public HonuaSdkFieldCollectionFeatureSyncClient(
        IHttpClientFactory httpClientFactory,
        IAuthenticationService authenticationService,
        ILogger<HonuaSdkFieldCollectionFeatureSyncClient>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _authenticationService = authenticationService;
        _logger = logger;
    }

    public bool IsConfigured => TryCreateOptions(out _);

    public async Task<FeatureEditResponse> ApplyEditsAsync(
        FeatureEditRequest request,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        return await client.ApplyEditsAsync(request, idempotencyKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeatureQueryResult> QueryAsync(
        FeatureQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        return await client.QueryAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FeatureAttachmentInfo>> ListAttachmentsAsync(
        FeatureAttachmentListRequest request,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        return await client.ListAttachmentsAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeatureAttachmentContent> DownloadAttachmentAsync(
        FeatureAttachmentDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        return await client.DownloadAttachmentAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeatureAttachmentResult> AddAttachmentAsync(
        FeatureAttachmentAddRequest request,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        return await client.AddAttachmentAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeatureAttachmentResult> DeleteAttachmentAsync(
        FeatureAttachmentDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        return await client.DeleteAttachmentAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private HonuaMobileClient CreateClient()
    {
        if (!TryCreateOptions(out var options))
        {
            throw new InvalidOperationException("Honua server URL and authentication are required for field sync.");
        }

        var httpClient = _httpClientFactory.CreateClient("HonuaFieldSync");
        return new HonuaMobileClient(
            httpClient,
            options,
            new FieldCollectionAuthTokenProvider(_authenticationService));
    }

    private bool TryCreateOptions(out HonuaMobileClientOptions options)
    {
        options = null!;

        if (string.IsNullOrWhiteSpace(_authenticationService.ServerUrl) ||
            !_authenticationService.IsAuthenticated ||
            !Uri.TryCreate(_authenticationService.ServerUrl.Trim(), UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        if (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback))
        {
            _logger?.LogWarning(
                "Field sync refused non-HTTPS Honua endpoint {Endpoint}",
                baseUri.GetLeftPart(UriPartial.Authority));
            return false;
        }

        options = new HonuaMobileClientOptions
        {
            BaseUri = baseUri,
            ApiKey = _authenticationService.ApiKey,
            AuthTokenProvider = new FieldCollectionAuthTokenProvider(_authenticationService),
            AllowInsecureTransportForDevelopment = baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback,
            PreferGrpcForFeatureQueries = false,
            PreferGrpcForFeatureEdits = false,
            AllowRestFallbackOnGrpcFailure = true
        };

        return true;
    }
}

public sealed class HonuaFieldCollectionChangeUploader :
    IFieldCollectionChangeUploader,
    IFieldCollectionRemoteSyncCapability
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Attribute that carries the client-generated idempotency token for an Insert.
    /// Sent with the Add so the server persists it, and used to dedupe a retried Insert
    /// after a lost ack so the same captured feature can never be double-created (#305).
    /// </summary>
    internal const string GlobalIdAttribute = "globalid";

    private static readonly string[] GlobalIdFieldCandidates =
        ["globalid", "GlobalID", "GLOBALID", "global_id"];

    private readonly GeoPackageStorageService _storage;
    private readonly IFieldCollectionMetadataService _metadataService;
    private readonly IFieldCollectionFeatureSyncClient _featureClient;
    private readonly ILogger<HonuaFieldCollectionChangeUploader>? _logger;

    public HonuaFieldCollectionChangeUploader(
        GeoPackageStorageService storage,
        IFieldCollectionMetadataService metadataService,
        IFieldCollectionFeatureSyncClient featureClient,
        ILogger<HonuaFieldCollectionChangeUploader>? logger = null)
    {
        _storage = storage;
        _metadataService = metadataService;
        _featureClient = featureClient;
        _logger = logger;
    }

    public bool IsRemoteSyncConfigured => _featureClient.IsConfigured;

    public async Task<bool> UploadChangeAsync(
        StorageChangeRecord change,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsRemoteSyncConfigured)
        {
            return false;
        }

        var layer = await ResolveLayerAsync(change.LayerId, cancellationToken).ConfigureAwait(false);
        var serviceId = FieldCollectionSyncLayer.ResolveServiceId(layer);
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            _logger?.LogWarning("Cannot upload change {ChangeId}; layer {LayerId} has no service id", change.Id, change.LayerId);
            return false;
        }

        Feature? localFeature = null;
        IReadOnlyList<FeatureEditFeature> adds = [];
        IReadOnlyList<FeatureEditFeature> updates = [];
        IReadOnlyList<long> deleteObjectIds = [];
        switch (change.Operation)
        {
            case StorageChangeOperation.Insert:
                localFeature = await _storage.GetFeatureAsync(change.FeatureId, change.LayerId).ConfigureAwait(false);
                if (localFeature == null)
                {
                    return false;
                }

                // Idempotency guard (#305): an Insert resent after a lost ack must not
                // create a server duplicate. Stamp a stable client-generated GlobalID and
                // reconcile against any feature the server already created under it before
                // re-adding.
                await EnsureGlobalIdAsync(localFeature).ConfigureAwait(false);
                if (await TryReconcileExistingInsertAsync(change, localFeature, serviceId, cancellationToken).ConfigureAwait(false))
                {
                    return true;
                }

                adds = [ToEditFeature(localFeature)];
                break;

            case StorageChangeOperation.Update:
                localFeature = await _storage.GetFeatureAsync(change.FeatureId, change.LayerId).ConfigureAwait(false);
                if (localFeature == null)
                {
                    return false;
                }

                updates = [ToEditFeature(localFeature)];
                break;

            case StorageChangeOperation.Delete:
                if (!TryResolveDeleteObjectId(change, out var objectId))
                {
                    localFeature = await _storage.GetFeatureAsync(change.FeatureId, change.LayerId).ConfigureAwait(false);
                    if (localFeature == null || !TryReadObjectId(localFeature, out objectId))
                    {
                        _logger?.LogWarning(
                            "Cannot upload delete change {ChangeId}; no FeatureServer object id was available for feature {FeatureId}",
                            change.Id,
                            change.FeatureId);
                        return false;
                    }
                }

                deleteObjectIds = [objectId];
                break;
        }

        var request = new FeatureEditRequest
        {
            Source = new FeatureSource { ServiceId = serviceId, LayerId = change.LayerId },
            Adds = adds,
            Updates = updates,
            DeleteObjectIds = deleteObjectIds,
            RollbackOnFailure = false,
            ForceWrite = false
        };

        // The change id is durable until the change is marked synced, so a re-upload after a lost
        // ack carries the same key and the server replays the original result instead of applying
        // the edit twice (at-most-once, #2250). This complements the insert GlobalID reconcile above
        // and extends at-most-once to updates and deletes.
        var idempotencyKey = $"fieldchange:{change.Id}";
        var response = await _featureClient.ApplyEditsAsync(request, idempotencyKey, cancellationToken).ConfigureAwait(false);
        if (response.Succeeded)
        {
            await UpdateLocalFeatureAfterSuccessfulAddAsync(change, localFeature, response).ConfigureAwait(false);
            return true;
        }

        if (IsConflict(response))
        {
            await StoreConflictAsync(change, localFeature, response).ConfigureAwait(false);
        }

        _logger?.LogWarning(
            "Field sync upload failed for change {ChangeId}: {Message}",
            change.Id,
            GetFailureMessage(response));
        return false;
    }

    private async Task<LayerInfo> ResolveLayerAsync(int layerId, CancellationToken cancellationToken)
    {
        var layers = await _metadataService.GetLayersAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return layers.FirstOrDefault(layer => layer.Id == layerId)
            ?? throw new InvalidOperationException($"Layer {layerId} is not available in field metadata.");
    }

    private async Task UpdateLocalFeatureAfterSuccessfulAddAsync(
        StorageChangeRecord change,
        Feature? localFeature,
        FeatureEditResponse response)
    {
        if (change.Operation != StorageChangeOperation.Insert || localFeature == null)
        {
            return;
        }

        var addResult = response.AddResults.FirstOrDefault(result => result.Succeeded);
        if (addResult?.ObjectId is not { } objectId)
        {
            return;
        }

        localFeature.Attributes["objectid"] = objectId;
        localFeature.Attributes["OBJECTID"] = objectId;
        await _storage.ApplyRemoteFeatureAsync(localFeature).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures the feature carries a stable client-generated GlobalID. New captures are
    /// stamped at storage time (<see cref="GeoPackageStorageService.StoreFeatureAsync"/>);
    /// this is a defensive fallback for legacy rows that predate the durable token. When a
    /// fallback token is minted it is persisted so it survives subsequent upload retries (#305).
    /// </summary>
    private async Task EnsureGlobalIdAsync(Feature feature)
    {
        if (!string.IsNullOrWhiteSpace(ReadGlobalId(feature)))
        {
            return;
        }

        feature.Attributes[GlobalIdAttribute] = Guid.NewGuid().ToString("N");

        // Persist the token without re-queuing a change so retries reuse the same id.
        await _storage.ApplyRemoteFeatureAsync(feature).ConfigureAwait(false);
    }

    /// <summary>
    /// Reconciles a pending Insert against a feature the server already created under the
    /// same GlobalID (a prior attempt whose ack was lost). When found, stamps the server
    /// OBJECTID locally and reports success so the Insert is NOT re-sent — preventing a
    /// duplicate (#305). Returns false when no such server feature exists.
    /// </summary>
    private async Task<bool> TryReconcileExistingInsertAsync(
        StorageChangeRecord change,
        Feature localFeature,
        string serviceId,
        CancellationToken cancellationToken)
    {
        var globalId = ReadGlobalId(localFeature);
        if (string.IsNullOrWhiteSpace(globalId))
        {
            return false;
        }

        // If the local row already has an OBJECTID the Insert clearly landed; nothing to do.
        if (TryReadObjectId(localFeature, out _))
        {
            return false;
        }

        FeatureQueryResult result;
        try
        {
            result = await _featureClient.QueryAsync(
                new FeatureQueryRequest
                {
                    Source = new FeatureSource { ServiceId = serviceId, LayerId = change.LayerId },
                    Filter = BuildGlobalIdFilter(globalId),
                    OutFields = ["*"],
                    ReturnGeometry = false
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A failed reconcile query must not double-create: fall back to the normal
            // add path only if it is safe. Here we cannot confirm the server state, so we
            // surface the failure and leave the change pending for a later retry.
            _logger?.LogWarning(
                ex,
                "Idempotency reconcile query failed for insert change {ChangeId}; leaving change pending",
                change.Id);
            throw;
        }

        var existing = result.Features.FirstOrDefault(record =>
            string.Equals(ReadGlobalId(record), globalId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return false;
        }

        if (!TryResolveServerObjectId(existing, result.ObjectIdFieldName, out var objectId))
        {
            // Server has the feature but we cannot resolve its OBJECTID; do not re-add.
            _logger?.LogWarning(
                "Insert change {ChangeId} already exists on the server under GlobalID {GlobalId} but no object id could be resolved; leaving change pending",
                change.Id,
                globalId);
            return false;
        }

        localFeature.Attributes["objectid"] = objectId;
        localFeature.Attributes["OBJECTID"] = objectId;
        await _storage.ApplyRemoteFeatureAsync(localFeature).ConfigureAwait(false);

        _logger?.LogInformation(
            "Reconciled retried insert change {ChangeId} to existing server feature {ObjectId} via GlobalID {GlobalId}",
            change.Id,
            objectId,
            globalId);
        return true;
    }

    private static string? ReadGlobalId(Feature feature)
    {
        foreach (var candidate in GlobalIdFieldCandidates)
        {
            if (feature.Attributes.TryGetValue(candidate, out var value) &&
                value is not null &&
                !string.IsNullOrWhiteSpace(value.ToString()))
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static string? ReadGlobalId(FeatureRecord record)
    {
        foreach (var candidate in GlobalIdFieldCandidates)
        {
            if (record.Attributes.TryGetValue(candidate, out var value))
            {
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim('"');
                }
            }
        }

        return null;
    }

    private static string BuildGlobalIdFilter(string globalId)
    {
        var escaped = globalId.Replace("'", "''", StringComparison.Ordinal);
        return $"{GlobalIdAttribute} = '{escaped}'";
    }

    private static bool TryResolveServerObjectId(FeatureRecord record, string? objectIdFieldName, out long objectId)
    {
        objectId = 0;
        IEnumerable<string> candidates = string.IsNullOrWhiteSpace(objectIdFieldName)
            ? ["objectid", "OBJECTID", "FID"]
            : [objectIdFieldName, "objectid", "OBJECTID", "FID"];

        foreach (var fieldName in candidates)
        {
            if (record.Attributes.TryGetValue(fieldName, out var value) && TryConvertInt64(value, out objectId))
            {
                return true;
            }
        }

        return long.TryParse(record.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId);
    }

    private async Task StoreConflictAsync(
        StorageChangeRecord change,
        Feature? localFeature,
        FeatureEditResponse response)
    {
        var conflict = new StorageConflictRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            FeatureId = change.FeatureId,
            LayerId = change.LayerId,
            ConflictType = change.Operation == StorageChangeOperation.Delete
                ? StorageConflictType.DeleteUpdate
                : StorageConflictType.UpdateUpdate,
            LocalVersion = localFeature?.Version ?? 0,
            ServerVersion = localFeature?.Version + 1 ?? 1,
            LocalData = change.ChangeData ?? (localFeature == null ? "{}" : JsonSerializer.Serialize(localFeature, JsonOptions)),
            ServerData = JsonSerializer.Serialize(
                new
                {
                    error = response.Error,
                    addResults = response.AddResults,
                    updateResults = response.UpdateResults,
                    deleteResults = response.DeleteResults
                },
                JsonOptions),
            CreatedAt = DateTime.UtcNow
        };

        await _storage.StoreConflictAsync(conflict).ConfigureAwait(false);
    }

    private static bool IsConflict(FeatureEditResponse response)
    {
        if (IsConflictError(response.Error))
        {
            return true;
        }

        return response.AddResults
            .Concat(response.UpdateResults)
            .Concat(response.DeleteResults)
            .Any(result => IsConflictError(result.Error));
    }

    private static bool IsConflictError(FeatureEditError? error)
    {
        if (error == null)
        {
            return false;
        }

        return error.Code is 409 or 1000 or 1003 ||
            (!string.IsNullOrWhiteSpace(error.Message) &&
             error.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetFailureMessage(FeatureEditResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Error?.Message))
        {
            return response.Error.Message;
        }

        var resultError = response.AddResults
            .Concat(response.UpdateResults)
            .Concat(response.DeleteResults)
            .Select(result => result.Error?.Message)
            .FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));

        return resultError ?? "Feature edit response did not report success.";
    }

    private static FeatureEditFeature ToEditFeature(Feature feature)
    {
        TryReadObjectId(feature, out var objectId);

        return new FeatureEditFeature
        {
            Id = feature.Id,
            ObjectId = objectId <= 0 ? null : objectId,
            Attributes = feature.Attributes.ToDictionary(
                attribute => attribute.Key,
                attribute => JsonSerializer.SerializeToElement(attribute.Value, JsonOptions),
                StringComparer.OrdinalIgnoreCase),
            Geometry = feature.Geometry == null ? null : ToFeatureServerGeometry(feature.Geometry)
        };
    }

    private static JsonElement ToFeatureServerGeometry(Geometry geometry)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            switch (geometry)
            {
                case Point point:
                    writer.WriteNumber("x", point.Longitude);
                    writer.WriteNumber("y", point.Latitude);
                    if (point.Altitude.HasValue)
                    {
                        writer.WriteNumber("z", point.Altitude.Value);
                    }
                    break;

                case LineString line:
                    writer.WritePropertyName("paths");
                    writer.WriteStartArray();
                    WriteCoordinateArray(writer, line.Coordinates);
                    writer.WriteEndArray();
                    break;

                case Polygon polygon:
                    writer.WritePropertyName("rings");
                    writer.WriteStartArray();
                    foreach (var ring in polygon.Coordinates)
                    {
                        WriteCoordinateArray(writer, ring);
                    }
                    writer.WriteEndArray();
                    break;

                default:
                    throw new NotSupportedException($"Geometry type {geometry.Type} is not supported by FeatureServer sync.");
            }

            writer.WritePropertyName("spatialReference");
            writer.WriteStartObject();
            writer.WriteNumber("wkid", geometry.SRID <= 0 ? 4326 : geometry.SRID);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static void WriteCoordinateArray(Utf8JsonWriter writer, IEnumerable<Point> points)
    {
        writer.WriteStartArray();
        foreach (var point in points)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(point.Longitude);
            writer.WriteNumberValue(point.Latitude);
            if (point.Altitude.HasValue)
            {
                writer.WriteNumberValue(point.Altitude.Value);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }

    private static bool TryResolveDeleteObjectId(StorageChangeRecord change, out long objectId)
    {
        objectId = 0;
        if (!string.IsNullOrWhiteSpace(change.ChangeData))
        {
            try
            {
                using var document = JsonDocument.Parse(change.ChangeData);
                if (TryReadInt64(document.RootElement, "objectId", out objectId) ||
                    TryReadInt64(document.RootElement, "objectid", out objectId) ||
                    TryReadInt64(document.RootElement, "OBJECTID", out objectId))
                {
                    return true;
                }

                if (document.RootElement.TryGetProperty("attributes", out var attributes) &&
                    (TryReadInt64(attributes, "objectId", out objectId) ||
                     TryReadInt64(attributes, "objectid", out objectId) ||
                     TryReadInt64(attributes, "OBJECTID", out objectId)))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return long.TryParse(change.FeatureId, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId);
    }

    private static bool TryReadObjectId(Feature feature, out long objectId)
    {
        objectId = 0;
        foreach (var attribute in feature.Attributes)
        {
            if (!attribute.Key.Equals("objectid", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("objectId", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("FID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryConvertInt64(attribute.Value, out objectId))
            {
                return true;
            }
        }

        return long.TryParse(feature.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId);
    }

    private static bool TryReadInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    internal static bool TryConvertInt64(object? value, out long objectId)
    {
        objectId = 0;
        return value switch
        {
            long longValue => Set(longValue, out objectId),
            int intValue => Set(intValue, out objectId),
            double doubleValue when Math.Abs(doubleValue % 1) < double.Epsilon => Set((long)doubleValue, out objectId),
            decimal decimalValue when decimalValue == Math.Truncate(decimalValue) => Set((long)decimalValue, out objectId),
            string stringValue => long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId),
            JsonElement { ValueKind: JsonValueKind.Number } element => element.TryGetInt64(out objectId),
            JsonElement { ValueKind: JsonValueKind.String } element => long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId),
            _ => false
        };

        static bool Set(long value, out long target)
        {
            target = value;
            return true;
        }
    }
}

public sealed class HonuaFieldCollectionChangePuller :
    IFieldCollectionChangePuller,
    IFieldCollectionRemoteSyncCapability
{
    private const string CursorKeyPrefix = "fieldcollection.sync.serverGeneration";

    private readonly IFieldCollectionMetadataService _metadataService;
    private readonly IFieldCollectionFeatureSyncClient _featureClient;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<HonuaFieldCollectionChangePuller>? _logger;

    public HonuaFieldCollectionChangePuller(
        IFieldCollectionMetadataService metadataService,
        IFieldCollectionFeatureSyncClient featureClient,
        ISettingsService settingsService,
        ILogger<HonuaFieldCollectionChangePuller>? logger = null)
    {
        _metadataService = metadataService;
        _featureClient = featureClient;
        _settingsService = settingsService;
        _logger = logger;
    }

    public bool IsRemoteSyncConfigured => _featureClient.IsConfigured;

    public async Task<IReadOnlyList<ServerChange>> GetChangesAsync(
        long sinceGeneration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsRemoteSyncConfigured)
        {
            return [];
        }

        var layers = await _metadataService.GetLayersAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var changes = new List<ServerChange>();

        foreach (var layer in layers.Where(layer => layer.IsEditable))
        {
            var serviceId = FieldCollectionSyncLayer.ResolveServiceId(layer);
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                continue;
            }

            // Push the delta down to the server so the pull is O(changes) instead of
            // scanning the whole layer every sync (#S2). When the layer schema exposes a
            // version column we filter on it server-side; otherwise we fall back to the
            // full scan and rely on the client-side guard below to discard already-seen
            // rows. The client-side guard is also kept as defense-in-depth for layers whose
            // version is derived from a timestamp (no comparable column to push down).
            var deltaFilter = BuildDeltaFilter(layer, sinceGeneration, out var versionField);

            var result = await _featureClient.QueryAsync(
                new FeatureQueryRequest
                {
                    Source = new FeatureSource { ServiceId = serviceId, LayerId = layer.Id },
                    Filter = deltaFilter,
                    OutFields = ["*"],
                    ReturnGeometry = true,
                    // Oldest deltas first so the cursor advances monotonically if the
                    // server caps the result set; only ordered when a real version
                    // column was resolved.
                    OrderBy = versionField is null ? null : $"{versionField} ASC"
                },
                cancellationToken).ConfigureAwait(false);

            foreach (var record in result.Features)
            {
                var feature = Feature.FromSdkFeatureRecord(record, layer.Id);
                feature.Id = ResolveFeatureId(record, result.ObjectIdFieldName);
                feature.Version = ResolveFeatureVersion(feature, record);
                feature.UpdatedAt = ResolveFeatureTimestamp(feature, record) ?? DateTime.UtcNow;
                feature.ModifiedAt = feature.UpdatedAt;

                if (feature.Version <= sinceGeneration)
                {
                    continue;
                }

                changes.Add(new ServerChange
                {
                    FeatureId = feature.Id,
                    LayerId = layer.Id,
                    Operation = IsDeletedFeature(feature) ? StorageChangeOperation.Delete : StorageChangeOperation.Update,
                    Version = feature.Version,
                    Feature = feature,
                    Timestamp = feature.UpdatedAt ?? DateTime.UtcNow
                });
            }
        }

        return changes;
    }

    public async Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cursorKey = await GetCursorKeyAsync(cancellationToken).ConfigureAwait(false);
        return await _settingsService.GetSettingAsync<long>(cursorKey, 0L).ConfigureAwait(false);
    }

    public async Task CommitSyncedGenerationAsync(long generation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cursorKey = await GetCursorKeyAsync(cancellationToken).ConfigureAwait(false);

        // The cursor must only ever move forward. Advancing to a generation that was
        // actually applied (computed by the sync service) keeps un-applied/conflicted
        // changes within the next pull window so they are re-downloaded (#304).
        var current = await _settingsService.GetSettingAsync<long>(cursorKey, 0L).ConfigureAwait(false);
        if (generation > current)
        {
            await _settingsService.SetSettingAsync(cursorKey, generation).ConfigureAwait(false);
        }
    }

    public async Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cursorKey = await GetCursorKeyAsync(cancellationToken).ConfigureAwait(false);
        return await _settingsService.GetSettingAsync<long>(cursorKey, 0L).ConfigureAwait(false);
    }

    private async Task<string> GetCursorKeyAsync(CancellationToken cancellationToken)
    {
        var selectedProject = await _metadataService.GetSelectedProjectAsync(cancellationToken).ConfigureAwait(false);
        var serviceId = string.IsNullOrWhiteSpace(selectedProject?.ServiceId)
            ? "default"
            : selectedProject.ServiceId.Trim();
        return $"{CursorKeyPrefix}:{serviceId}";
    }

    private static string ResolveFeatureId(FeatureRecord record, string? objectIdFieldName)
    {
        if (!string.IsNullOrWhiteSpace(record.Id))
        {
            return record.Id;
        }

        foreach (var fieldName in CandidateObjectIdFields(objectIdFieldName))
        {
            if (record.Attributes.TryGetValue(fieldName, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? string.Empty;
                }

                return value.GetRawText();
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    // Integer version columns, in the precedence used to derive a feature's version.
    // The delta filter pushed to the server must resolve to the same column so the
    // server-side predicate matches the value compared client-side.
    private static readonly string[] VersionFieldCandidates =
        { "sync_version", "version", "server_version", "edit_version" };

    private static long ResolveFeatureVersion(Feature feature, FeatureRecord record)
    {
        foreach (var fieldName in VersionFieldCandidates)
        {
            if (TryReadInt64(feature.Attributes, fieldName, out var version) ||
                (record.Attributes.TryGetValue(fieldName, out var value) && TryReadInt64(value, out version)))
            {
                return Math.Max(1, version);
            }
        }

        if (ResolveFeatureTimestamp(feature, record) is { } timestamp)
        {
            return Math.Max(1, timestamp.Ticks);
        }

        return 1;
    }

    /// <summary>
    /// Builds the server-side filter for an incremental pull. When the layer schema
    /// exposes one of the <see cref="VersionFieldCandidates"/> columns, the delta is
    /// pushed down as a <c>&gt; sinceGeneration</c> predicate so only changed rows are
    /// returned. When no version column is known the filter falls back to <c>1=1</c>
    /// (and <paramref name="versionField"/> is <see langword="null"/>), in which case the
    /// caller's client-side guard still discards already-seen rows.
    /// </summary>
    /// <remarks>
    /// Honua Server currently has no generic incremental-change query parameter on this
    /// feature read path, so the delta is expressed as a WHERE predicate on the version
    /// column. A dedicated server-side change-generation parameter (cross-repo:
    /// honua-server) would let timestamp-versioned layers also pull O(delta).
    /// </remarks>
    private static string BuildDeltaFilter(LayerInfo layer, long sinceGeneration, out string? versionField)
    {
        versionField = ResolveVersionFieldName(layer);
        if (versionField is null)
        {
            return "1=1";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{versionField} > {sinceGeneration}");
    }

    private static string? ResolveVersionFieldName(LayerInfo layer)
    {
        if (layer.Schema.Count == 0)
        {
            return null;
        }

        // Honour the same precedence as ResolveFeatureVersion and only emit a column
        // name drawn from the known candidate list (acts as a whitelist for the filter).
        foreach (var candidate in VersionFieldCandidates)
        {
            foreach (var field in layer.Schema)
            {
                var column = string.IsNullOrWhiteSpace(field.SourceFieldName)
                    ? field.FieldId
                    : field.SourceFieldName;

                if (string.Equals(column, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static DateTime? ResolveFeatureTimestamp(Feature feature, FeatureRecord record)
    {
        foreach (var fieldName in new[] { "updated_at", "modified_at", "last_edited_date", "editdate" })
        {
            if (TryReadDateTime(feature.Attributes, fieldName, out var timestamp) ||
                (record.Attributes.TryGetValue(fieldName, out var value) && TryReadDateTime(value, out timestamp)))
            {
                return timestamp;
            }
        }

        return null;
    }

    private static bool IsDeletedFeature(Feature feature)
    {
        foreach (var fieldName in new[] { "deleted", "is_deleted", "honua_deleted" })
        {
            if (feature.Attributes.TryGetValue(fieldName, out var value) && TryReadBoolean(value, out var deleted))
            {
                return deleted;
            }
        }

        return false;
    }

    private static IEnumerable<string> CandidateObjectIdFields(string? objectIdFieldName)
    {
        if (!string.IsNullOrWhiteSpace(objectIdFieldName))
        {
            yield return objectIdFieldName;
        }

        yield return "objectid";
        yield return "OBJECTID";
        yield return "objectId";
        yield return "FID";
    }

    private static bool TryReadInt64(IReadOnlyDictionary<string, object?> attributes, string fieldName, out long value)
    {
        value = 0;
        foreach (var attribute in attributes)
        {
            if (attribute.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase) &&
                HonuaFieldCollectionChangeUploader.TryConvertInt64(attribute.Value, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadInt64(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryReadDateTime(IReadOnlyDictionary<string, object?> attributes, string fieldName, out DateTime value)
    {
        value = default;
        foreach (var attribute in attributes)
        {
            if (!attribute.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (attribute.Value is DateTime dateTime)
            {
                value = dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime();
                return true;
            }

            if (attribute.Value is string stringValue &&
                DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
            {
                value = value.ToUniversalTime();
                return true;
            }

            if (attribute.Value is JsonElement json && TryReadDateTime(json, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDateTime(JsonElement element, out DateTime value)
    {
        value = default;
        if (element.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            value = value.ToUniversalTime();
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var epochMillis))
        {
            value = DateTimeOffset.FromUnixTimeMilliseconds(epochMillis).UtcDateTime;
            return true;
        }

        return false;
    }

    private static bool TryReadBoolean(object? value, out bool result)
    {
        result = false;
        return value switch
        {
            bool boolValue => Set(boolValue, out result),
            string stringValue => bool.TryParse(stringValue, out result),
            JsonElement { ValueKind: JsonValueKind.True } => Set(true, out result),
            JsonElement { ValueKind: JsonValueKind.False } => Set(false, out result),
            _ => false
        };

        static bool Set(bool value, out bool target)
        {
            target = value;
            return true;
        }
    }
}

public sealed class HonuaFieldCollectionAttachmentSynchronizer :
    IFieldCollectionAttachmentSynchronizer,
    IFieldCollectionRemoteSyncCapability
{
    private readonly GeoPackageStorageService _storage;
    private readonly IAttachmentService _attachmentService;
    private readonly IFieldCollectionMetadataService _metadataService;
    private readonly IFieldCollectionAttachmentSyncClient _attachmentClient;
    private readonly ILogger<HonuaFieldCollectionAttachmentSynchronizer>? _logger;

    public HonuaFieldCollectionAttachmentSynchronizer(
        GeoPackageStorageService storage,
        IAttachmentService attachmentService,
        IFieldCollectionMetadataService metadataService,
        IFieldCollectionAttachmentSyncClient attachmentClient,
        ILogger<HonuaFieldCollectionAttachmentSynchronizer>? logger = null)
    {
        _storage = storage;
        _attachmentService = attachmentService;
        _metadataService = metadataService;
        _attachmentClient = attachmentClient;
        _logger = logger;
    }

    public bool IsRemoteSyncConfigured => _attachmentClient.IsConfigured;

    public async Task<AttachmentSyncResult> PushPendingAttachmentsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRemoteSyncConfigured)
        {
            return new AttachmentSyncResult();
        }

        var result = new AttachmentSyncResult();
        var layers = await _metadataService.GetLayersAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var pendingAttachments = await _storage.GetPendingAttachmentChangesAsync().ConfigureAwait(false);

        foreach (var attachment in pendingAttachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attachment.SyncStatus is AttachmentSyncStatus.PendingDownload or AttachmentSyncStatus.DownloadFailed)
            {
                continue;
            }

            var layer = layers.FirstOrDefault(layer => layer.Id == attachment.LayerId);
            var serviceId = layer == null ? null : FieldCollectionSyncLayer.ResolveServiceId(layer);
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                await MarkAttachmentFailureAsync(
                    attachment,
                    attachment.IsDeleted ? AttachmentSyncStatus.DeleteFailed : AttachmentSyncStatus.UploadFailed,
                    $"Layer {attachment.LayerId} has no FeatureServer service id.").ConfigureAwait(false);
                result.Failed++;
                continue;
            }

            var objectId = await ResolveObjectIdAsync(attachment).ConfigureAwait(false);
            if (!objectId.HasValue)
            {
                await MarkAttachmentFailureAsync(
                    attachment,
                    attachment.IsDeleted ? AttachmentSyncStatus.DeleteFailed : AttachmentSyncStatus.UploadFailed,
                    $"Feature {attachment.FeatureId} does not have a server object id yet.").ConfigureAwait(false);
                result.Failed++;
                continue;
            }

            try
            {
                if (attachment.IsDeleted ||
                    attachment.SyncStatus is AttachmentSyncStatus.PendingDelete or AttachmentSyncStatus.DeleteFailed)
                {
                    if (await PushAttachmentDeleteAsync(attachment, serviceId, objectId.Value, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        result.Deleted++;
                    }
                    else
                    {
                        result.Failed++;
                    }
                }
                else if (await PushAttachmentUploadAsync(attachment, serviceId, objectId.Value, cancellationToken)
                             .ConfigureAwait(false))
                {
                    result.Uploaded++;
                }
                else
                {
                    result.Failed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await MarkAttachmentFailureAsync(
                    attachment,
                    attachment.IsDeleted ? AttachmentSyncStatus.DeleteFailed : AttachmentSyncStatus.UploadFailed,
                    ex.Message).ConfigureAwait(false);
                _logger?.LogWarning(
                    ex,
                    "Attachment sync failed for local attachment {AttachmentId}",
                    attachment.Id);
                result.Failed++;
            }
        }

        return result;
    }

    public async Task<AttachmentSyncResult> PullRemoteAttachmentsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsRemoteSyncConfigured)
        {
            return new AttachmentSyncResult();
        }

        var result = new AttachmentSyncResult();
        var layers = await _metadataService.GetLayersAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var layer in layers.Where(layer => layer.IsEditable))
        {
            var serviceId = FieldCollectionSyncLayer.ResolveServiceId(layer);
            if (string.IsNullOrWhiteSpace(serviceId))
            {
                continue;
            }

            var features = await _storage.QueryFeaturesAsync(layer.Id).ConfigureAwait(false);
            foreach (var feature in features)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryReadObjectId(feature, out var objectId))
                {
                    continue;
                }

                IReadOnlyList<FeatureAttachmentInfo> remoteAttachments;
                try
                {
                    remoteAttachments = await _attachmentClient.ListAttachmentsAsync(
                        new FeatureAttachmentListRequest
                        {
                            Source = new FeatureSource { ServiceId = serviceId, LayerId = layer.Id },
                            ObjectId = objectId
                        },
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogWarning(
                        ex,
                        "Failed to list attachments for feature {FeatureId} in layer {LayerId}",
                        feature.Id,
                        layer.Id);
                    result.Failed++;
                    continue;
                }

                foreach (var remoteAttachment in remoteAttachments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var remoteAttachmentId = remoteAttachment.AttachmentId.GetValueOrDefault();
                    if (remoteAttachmentId <= 0)
                    {
                        continue;
                    }

                    var existing = await _storage.GetAttachmentByRemoteIdAsync(layer.Id, feature.Id, remoteAttachmentId)
                        .ConfigureAwait(false);

                    if (existing is { IsDeleted: true } &&
                        existing.SyncStatus is AttachmentSyncStatus.PendingDelete or AttachmentSyncStatus.DeleteFailed)
                    {
                        continue;
                    }

                    if (existing is { IsDeleted: false } &&
                        await _attachmentService.AttachmentContentExistsAsync(existing.Id).ConfigureAwait(false))
                    {
                        continue;
                    }

                    try
                    {
                        var content = await _attachmentClient.DownloadAttachmentAsync(
                            new FeatureAttachmentDownloadRequest
                            {
                                Source = new FeatureSource { ServiceId = serviceId, LayerId = layer.Id },
                                ObjectId = objectId,
                                AttachmentId = remoteAttachmentId
                            },
                            cancellationToken).ConfigureAwait(false);
                        using var contentStream = content.Content;

                        var info = content.Info.AttachmentId > 0
                            ? content.Info
                            : remoteAttachment;
                        await _attachmentService.SaveDownloadedAttachmentAsync(
                            layer.Id,
                            feature.Id,
                            info,
                            contentStream,
                            cancellationToken).ConfigureAwait(false);
                        result.Downloaded++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        await StoreDownloadFailureAsync(layer.Id, feature.Id, remoteAttachment, ex.Message)
                            .ConfigureAwait(false);
                        _logger?.LogWarning(
                            ex,
                            "Attachment download failed for remote attachment {AttachmentId} on feature {FeatureId}",
                            remoteAttachment.AttachmentId,
                            feature.Id);
                        result.Failed++;
                    }
                }
            }
        }

        return result;
    }

    private async Task<bool> PushAttachmentUploadAsync(
        AttachmentInfo attachment,
        string serviceId,
        long objectId,
        CancellationToken cancellationToken)
    {
        if (!await _attachmentService.AttachmentContentExistsAsync(attachment.Id).ConfigureAwait(false))
        {
            await MarkAttachmentFailureAsync(
                attachment,
                AttachmentSyncStatus.UploadFailed,
                "Attachment content file is missing.").ConfigureAwait(false);
            return false;
        }

        using var content = await _attachmentService.GetAttachmentAsync(attachment.Id).ConfigureAwait(false);
        var response = await _attachmentClient.AddAttachmentAsync(
            new FeatureAttachmentAddRequest
            {
                Source = new FeatureSource { ServiceId = serviceId, LayerId = attachment.LayerId },
                ObjectId = objectId,
                Name = attachment.FileName,
                ContentType = string.IsNullOrWhiteSpace(attachment.ContentType)
                    ? "application/octet-stream"
                    : attachment.ContentType,
                Content = content,
                Keywords = FieldLocationMetadataMapper.ToAttachmentKeywords(
                    attachment.Description,
                    attachment.CaptureLocation)
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Succeeded)
        {
            var remoteAttachmentId = response.AttachmentId.GetValueOrDefault();
            if (remoteAttachmentId <= 0)
            {
                await MarkAttachmentFailureAsync(
                    attachment,
                    AttachmentSyncStatus.UploadFailed,
                    "Attachment upload response did not include a remote attachment id.").ConfigureAwait(false);
                return false;
            }

            await _storage.MarkAttachmentUploadedAsync(
                attachment.Id,
                remoteAttachmentId,
                response.GlobalId,
                DateTime.UtcNow).ConfigureAwait(false);
            return true;
        }

        await MarkAttachmentFailureAsync(
            attachment,
            AttachmentSyncStatus.UploadFailed,
            GetFailureMessage(response, "Attachment upload failed.")).ConfigureAwait(false);
        return false;
    }

    private async Task<bool> PushAttachmentDeleteAsync(
        AttachmentInfo attachment,
        string serviceId,
        long objectId,
        CancellationToken cancellationToken)
    {
        if (!attachment.RemoteAttachmentId.HasValue || attachment.RemoteAttachmentId.Value <= 0)
        {
            await _storage.MarkAttachmentDeletedSyncedAsync(attachment.Id).ConfigureAwait(false);
            return true;
        }

        var response = await _attachmentClient.DeleteAttachmentAsync(
            new FeatureAttachmentDeleteRequest
            {
                Source = new FeatureSource { ServiceId = serviceId, LayerId = attachment.LayerId },
                ObjectId = objectId,
                AttachmentId = attachment.RemoteAttachmentId.Value
            },
            cancellationToken).ConfigureAwait(false);

        if (response.Succeeded)
        {
            await _storage.MarkAttachmentDeletedSyncedAsync(attachment.Id).ConfigureAwait(false);
            return true;
        }

        await MarkAttachmentFailureAsync(
            attachment,
            AttachmentSyncStatus.DeleteFailed,
            GetFailureMessage(response, "Attachment delete failed.")).ConfigureAwait(false);
        return false;
    }

    private async Task StoreDownloadFailureAsync(
        int layerId,
        string featureId,
        FeatureAttachmentInfo remoteAttachment,
        string errorMessage)
    {
        var remoteAttachmentId = remoteAttachment.AttachmentId.GetValueOrDefault();
        var existing = remoteAttachmentId > 0
            ? await _storage.GetAttachmentByRemoteIdAsync(layerId, featureId, remoteAttachmentId)
                .ConfigureAwait(false)
            : null;
        if (existing == null)
        {
            var now = DateTime.UtcNow;
            existing = new AttachmentInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                LayerId = layerId,
                FeatureId = featureId,
                RemoteAttachmentId = remoteAttachmentId <= 0 ? null : remoteAttachmentId,
                RemoteGlobalId = remoteAttachment.GlobalId,
                FileName = string.IsNullOrWhiteSpace(remoteAttachment.Name)
                    ? "attachment.bin"
                    : remoteAttachment.Name,
                ContentType = string.IsNullOrWhiteSpace(remoteAttachment.ContentType)
                    ? "application/octet-stream"
                    : remoteAttachment.ContentType,
                PayloadKind = !string.IsNullOrWhiteSpace(remoteAttachment.ContentType) &&
                    remoteAttachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    ? AttachmentPayloadKind.Photo
                    : AttachmentPayloadKind.File,
                SizeBytes = remoteAttachment.Size.GetValueOrDefault(),
                CreatedAt = now,
                UpdatedAt = now,
                UploadedAt = now,
                SyncStatus = AttachmentSyncStatus.PendingDownload
            };
            await _storage.StoreAttachmentMetadataAsync(existing).ConfigureAwait(false);
        }

        await _storage.MarkAttachmentSyncFailedAsync(
            existing.Id,
            AttachmentSyncStatus.DownloadFailed,
            errorMessage).ConfigureAwait(false);
    }

    private Task MarkAttachmentFailureAsync(
        AttachmentInfo attachment,
        AttachmentSyncStatus failedStatus,
        string errorMessage)
    {
        return _storage.MarkAttachmentSyncFailedAsync(attachment.Id, failedStatus, errorMessage);
    }

    private async Task<long?> ResolveObjectIdAsync(AttachmentInfo attachment)
    {
        var feature = await _storage.GetFeatureAsync(attachment.FeatureId, attachment.LayerId).ConfigureAwait(false);
        if (feature != null && TryReadObjectId(feature, out var objectId))
        {
            return objectId;
        }

        return long.TryParse(attachment.FeatureId, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId)
            ? objectId
            : null;
    }

    private static bool TryReadObjectId(Feature feature, out long objectId)
    {
        objectId = 0;
        foreach (var attribute in feature.Attributes)
        {
            if (!attribute.Key.Equals("objectid", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("objectId", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) &&
                !attribute.Key.Equals("FID", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (HonuaFieldCollectionChangeUploader.TryConvertInt64(attribute.Value, out objectId))
            {
                return true;
            }
        }

        return long.TryParse(feature.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId);
    }

    private static string GetFailureMessage(FeatureAttachmentResult response, string fallback)
    {
        return string.IsNullOrWhiteSpace(response.Error?.Message)
            ? fallback
            : response.Error.Message;
    }
}

public sealed class QueuedFieldCollectionChangeUploader :
    IFieldCollectionChangeUploader,
    IFieldCollectionRemoteSyncCapability
{
    private readonly ILogger<QueuedFieldCollectionChangeUploader>? _logger;

    public QueuedFieldCollectionChangeUploader(ILogger<QueuedFieldCollectionChangeUploader>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRemoteSyncConfigured => false;

    public Task<bool> UploadChangeAsync(StorageChangeRecord change, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger?.LogWarning(
            "Field collection change {ChangeId} for feature {FeatureId} in layer {LayerId} remains queued because remote field sync is not configured",
            change.Id,
            change.FeatureId,
            change.LayerId);

        return Task.FromResult(false);
    }
}

internal sealed class FieldCollectionAuthTokenProvider : IAuthTokenProvider
{
    private readonly IAuthenticationService _authenticationService;

    public FieldCollectionAuthTokenProvider(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    public ValueTask<HonuaAuthToken?> GetTokenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _authenticationService.GetAuthTokenAsync(ct);
    }

    public async ValueTask<HonuaAuthToken?> RefreshTokenAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await _authenticationService.RefreshTokenAsync(ct).ConfigureAwait(false)
            ? await _authenticationService.GetAuthTokenAsync(ct).ConfigureAwait(false)
            : null;
    }

    public ValueTask StoreTokenAsync(HonuaAuthToken token, CancellationToken ct = default)
        => ValueTask.CompletedTask;

    public ValueTask ClearTokenAsync(CancellationToken ct = default)
        => ValueTask.CompletedTask;
}

public sealed class LocalOnlyFieldCollectionChangePuller :
    IFieldCollectionChangePuller,
    IFieldCollectionRemoteSyncCapability
{
    private readonly ILogger<LocalOnlyFieldCollectionChangePuller>? _logger;

    public LocalOnlyFieldCollectionChangePuller(ILogger<LocalOnlyFieldCollectionChangePuller>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRemoteSyncConfigured => false;

    public Task<IReadOnlyList<ServerChange>> GetChangesAsync(
        long sinceGeneration,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger?.LogDebug(
            "Field collection pull requested from generation {Generation}; remote field sync is local-only",
            sinceGeneration);
        return Task.FromResult<IReadOnlyList<ServerChange>>([]);
    }

    public Task<long> GetLatestServerGenerationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }

    public Task<long> GetLastSyncedGenerationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0L);
    }

    public Task CommitSyncedGenerationAsync(long generation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Local-only pulls have no durable server cursor to advance.
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shared helpers for resolving sync routing information from a <see cref="LayerInfo"/>.
/// </summary>
internal static class FieldCollectionSyncLayer
{
    private const string FeatureServerSeparator = "/FeatureServer/";

    /// <summary>
    /// Resolves the feature service id a layer belongs to, preferring the explicit
    /// <see cref="LayerInfo.ServiceId"/> and otherwise parsing it from the
    /// <see cref="LayerInfo.SourceId"/> (the segment before <c>/FeatureServer/</c>).
    /// This is the single source of truth used by upload, pull, and attachment sync so the
    /// three transports cannot diverge on how routing is derived.
    /// </summary>
    public static string? ResolveServiceId(LayerInfo layer)
    {
        if (!string.IsNullOrWhiteSpace(layer.ServiceId))
        {
            return layer.ServiceId;
        }

        if (!string.IsNullOrWhiteSpace(layer.SourceId) &&
            layer.SourceId.IndexOf(FeatureServerSeparator, StringComparison.OrdinalIgnoreCase) is var index and >= 0)
        {
            return layer.SourceId[..index];
        }

        return null;
    }
}
