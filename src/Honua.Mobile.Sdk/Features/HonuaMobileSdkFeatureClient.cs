using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Honua.Mobile.Sdk.Models;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Sdk.Features;

/// <summary>
/// Adapts <see cref="HonuaMobileClient"/> to the SDK feature query and edit abstractions.
/// </summary>
public sealed class HonuaMobileSdkFeatureClient : IHonuaFeatureQueryClient, IHonuaFeatureEditClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HonuaMobileClient _client;

    /// <summary>
    /// Initializes a new <see cref="HonuaMobileSdkFeatureClient"/>.
    /// </summary>
    /// <param name="client">Mobile client used for server calls.</param>
    public HonuaMobileSdkFeatureClient(HonuaMobileClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    /// <inheritdoc />
    public string ProviderName => "honua-mobile";

    /// <inheritdoc />
    public FeatureEditCapabilities EditCapabilities { get; } = new()
    {
        SupportsAdds = true,
        SupportsUpdates = true,
        SupportsDeletes = true,
        SupportsRollbackOnFailure = true,
        NativeSurface = "HonuaMobileClient",
    };

    /// <inheritdoc />
    public async Task<FeatureQueryResult> QueryAsync(FeatureQueryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.Source.CollectionId))
        {
            using var response = await _client.GetOgcItemsAsync(ToOgcRequest(request), ct).ConfigureAwait(false);
            return ParseQueryResult(response.RootElement, request, "ogcfeatures");
        }

        if (!string.IsNullOrWhiteSpace(request.Source.ServiceId) && request.Source.LayerId.HasValue)
        {
            using var response = await _client.QueryFeaturesAsync(ToFeatureServerRequest(request), ct).ConfigureAwait(false);
            return ParseQueryResult(response.RootElement, request, "featureserver");
        }

        throw new InvalidOperationException("Feature query requires either an OGC collection ID or FeatureServer service/layer identifiers.");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FeatureQueryResult> QueryPagesAsync(
        FeatureQueryRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var offset = request.Offset ?? 0;
        var limit = request.Limit;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await QueryAsync(request with { Offset = offset, Limit = limit }, ct).ConfigureAwait(false);
            yield return page;

            if (!page.HasMoreResults || limit is null || page.NumberReturned == 0)
            {
                yield break;
            }

            offset += page.NumberReturned;
        }
    }

    /// <inheritdoc />
    public async Task<FeatureEditResponse> ApplyEditsAsync(FeatureEditRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Source.CollectionId))
            {
                return await ApplyOgcEditsAsync(request, ct).ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(request.Source.ServiceId) && request.Source.LayerId.HasValue)
            {
                using var response = await _client.ApplyEditsAsync(ToFeatureServerEditRequest(request), ct).ConfigureAwait(false);
                return ParseEditResponse(response.RootElement, "featureserver");
            }
        }
        catch (HonuaMobileApiException ex)
        {
            return new FeatureEditResponse
            {
                ProviderName = ProviderName,
                Error = new FeatureEditError { Code = (int)ex.StatusCode, Message = ex.Message },
            };
        }

        return new FeatureEditResponse
        {
            ProviderName = ProviderName,
            Error = new FeatureEditError { Message = "Feature edit requires either an OGC collection ID or FeatureServer service/layer identifiers." },
        };
    }

    private async Task<FeatureEditResponse> ApplyOgcEditsAsync(FeatureEditRequest request, CancellationToken ct)
    {
        var addResults = new List<FeatureEditResult>();
        var updateResults = new List<FeatureEditResult>();
        var deleteResults = new List<FeatureEditResult>();
        var collectionId = request.Source.CollectionId ?? throw new InvalidOperationException("OGC edit request requires collection ID.");

        foreach (var add in request.Adds)
        {
            using var response = await _client.CreateOgcItemAsync(new OgcCreateItemRequest
            {
                CollectionId = collectionId,
                Feature = ToGeoJsonFeature(add),
            }, ct).ConfigureAwait(false);
            addResults.Add(ToOgcSuccessResult(response.RootElement, add.Id, add.ObjectId));
        }

        foreach (var update in request.Updates)
        {
            if (string.IsNullOrWhiteSpace(update.Id))
            {
                updateResults.Add(new FeatureEditResult
                {
                    Succeeded = false,
                    ObjectId = update.ObjectId,
                    Error = new FeatureEditError { Message = "OGC update requires feature ID." },
                });
                continue;
            }

            using var response = await _client.ReplaceOgcItemAsync(new OgcReplaceItemRequest
            {
                CollectionId = collectionId,
                FeatureId = update.Id,
                Feature = ToGeoJsonFeature(update),
            }, ct).ConfigureAwait(false);
            updateResults.Add(ToOgcSuccessResult(response.RootElement, update.Id, update.ObjectId));
        }

        foreach (var deleteId in request.DeleteIds)
        {
            using var response = await _client.DeleteOgcItemAsync(new OgcDeleteItemRequest
            {
                CollectionId = collectionId,
                FeatureId = deleteId,
            }, ct).ConfigureAwait(false);
            deleteResults.Add(ToOgcSuccessResult(response.RootElement, deleteId, null));
        }

        return new FeatureEditResponse
        {
            ProviderName = "ogcfeatures",
            AddResults = addResults,
            UpdateResults = updateResults,
            DeleteResults = deleteResults,
        };
    }

    private static QueryFeaturesRequest ToFeatureServerRequest(FeatureQueryRequest request)
        => new()
        {
            ServiceId = request.Source.ServiceId ?? throw new InvalidOperationException("FeatureServer query requires service ID."),
            LayerId = request.Source.LayerId ?? throw new InvalidOperationException("FeatureServer query requires layer ID."),
            Where = request.Filter ?? "1=1",
            ObjectIds = request.ObjectIds,
            OutFields = request.OutFields,
            ReturnGeometry = request.ReturnGeometry ?? true,
            ResultOffset = request.Offset,
            ResultRecordCount = request.Limit,
            OrderBy = request.OrderBy,
        };

    private static OgcItemsRequest ToOgcRequest(FeatureQueryRequest request)
        => new()
        {
            CollectionId = request.Source.CollectionId ?? throw new InvalidOperationException("OGC query requires collection ID."),
            CqlFilter = request.Filter,
            PropertyNames = request.OutFields,
            Limit = request.Limit,
            Offset = request.Offset,
        };

    private static ApplyEditsRequest ToFeatureServerEditRequest(FeatureEditRequest request)
        => new()
        {
            ServiceId = request.Source.ServiceId ?? throw new InvalidOperationException("FeatureServer edit requires service ID."),
            LayerId = request.Source.LayerId ?? throw new InvalidOperationException("FeatureServer edit requires layer ID."),
            AddsJson = request.Adds.Count == 0 ? null : JsonSerializer.Serialize(request.Adds.Select(ToFeatureServerFeature), JsonOptions),
            UpdatesJson = request.Updates.Count == 0 ? null : JsonSerializer.Serialize(request.Updates.Select(ToFeatureServerFeature), JsonOptions),
            DeletesCsv = request.DeleteObjectIds.Count > 0
                ? string.Join(',', request.DeleteObjectIds)
                : request.DeleteIds.Count == 0 ? null : string.Join(',', request.DeleteIds),
            RollbackOnFailure = request.RollbackOnFailure,
            ForceWrite = request.ForceWrite,
        };

    private static FeatureQueryResult ParseQueryResult(JsonElement root, FeatureQueryRequest request, string providerName)
    {
        var features = root.TryGetProperty("features", out var featuresElement) && featuresElement.ValueKind == JsonValueKind.Array
            ? featuresElement.EnumerateArray().Select(ParseFeatureRecord).ToArray()
            : [];

        var matched = ReadInt64(root, "numberMatched") ?? ReadInt64(root, "count");
        var exceededTransferLimit = root.TryGetProperty("exceededTransferLimit", out var exceeded) && exceeded.ValueKind == JsonValueKind.True;
        var hasNextLink = HasNextLink(root);
        var inferredHasMore = matched.HasValue && request.Offset.HasValue && request.Limit.HasValue
            ? request.Offset.Value + features.Length < matched.Value
            : false;

        return new FeatureQueryResult
        {
            ProviderName = providerName,
            Features = features,
            NumberMatched = matched,
            NumberReturned = features.Length,
            HasMoreResults = exceededTransferLimit || hasNextLink || inferredHasMore,
            ObjectIdFieldName = root.TryGetProperty("objectIdFieldName", out var objectIdField) && objectIdField.ValueKind == JsonValueKind.String
                ? objectIdField.GetString()
                : null,
        };
    }

    private static FeatureRecord ParseFeatureRecord(JsonElement feature)
    {
        var attributes = feature.TryGetProperty("attributes", out var featureServerAttributes)
            ? ReadJsonObject(featureServerAttributes)
            : feature.TryGetProperty("properties", out var geoJsonProperties)
                ? ReadJsonObject(geoJsonProperties)
                : new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>());

        JsonElement? geometry = null;
        if (feature.TryGetProperty("geometry", out var geometryElement) && geometryElement.ValueKind != JsonValueKind.Null)
        {
            geometry = geometryElement.Clone();
        }

        return new FeatureRecord
        {
            Id = ReadFeatureId(feature, attributes),
            Attributes = attributes,
            Geometry = geometry,
        };
    }

    private static FeatureEditResponse ParseEditResponse(JsonElement root, string providerName)
        => new()
        {
            ProviderName = providerName,
            AddResults = ParseEditResults(root, "addResults"),
            UpdateResults = ParseEditResults(root, "updateResults"),
            DeleteResults = ParseEditResults(root, "deleteResults"),
            Error = TryReadError(root, out var error) ? error : null,
        };

    private static IReadOnlyList<FeatureEditResult> ParseEditResults(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return results.EnumerateArray().Select(result => new FeatureEditResult
        {
            Id = ReadString(result, "id") ?? ReadString(result, "globalId"),
            ObjectId = ReadInt64(result, "objectId"),
            Succeeded = result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True,
            Error = TryReadError(result, out var error) ? error : null,
        }).ToArray();
    }

    private static FeatureEditResult ToOgcSuccessResult(JsonElement root, string? fallbackId, long? objectId)
    {
        if (TryReadError(root, out var error))
        {
            return new FeatureEditResult
            {
                Id = fallbackId,
                ObjectId = objectId,
                Succeeded = false,
                Error = error,
            };
        }

        return new FeatureEditResult
        {
            Id = ReadString(root, "id") ?? fallbackId,
            ObjectId = objectId,
            Succeeded = true,
        };
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

    private static IReadOnlyDictionary<string, JsonElement> ReadJsonObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new ReadOnlyDictionary<string, JsonElement>(new Dictionary<string, JsonElement>());
        }

        return new ReadOnlyDictionary<string, JsonElement>(
            element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone()));
    }

    private static bool TryReadError(JsonElement element, out FeatureEditError? error)
    {
        error = null;
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("error", out var nestedError))
        {
            return TryReadError(nestedError, out error);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var code = ReadInt32(element, "code");
        var message = ReadString(element, "message");
        if (code is null && string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        error = new FeatureEditError
        {
            Code = code,
            Message = message ?? "Provider reported an edit error.",
        };
        return true;
    }

    private static string? ReadFeatureId(JsonElement feature, IReadOnlyDictionary<string, JsonElement> attributes)
    {
        if (feature.TryGetProperty("id", out var id))
        {
            return id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
        }

        foreach (var key in new[] { "OBJECTID", "objectid", "ObjectID", "FID" })
        {
            if (attributes.TryGetValue(key, out var objectId))
            {
                return objectId.ValueKind == JsonValueKind.String ? objectId.GetString() : objectId.GetRawText();
            }
        }

        return null;
    }

    private static bool HasNextLink(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return links.EnumerateArray().Any(link =>
            link.TryGetProperty("rel", out var rel) &&
            rel.ValueKind == JsonValueKind.String &&
            string.Equals(rel.GetString(), "next", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static long? ReadInt64(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var parsed) ? parsed : null;
}
