using System.Globalization;
using System.Text.Json;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// HTTP-based client for the server-side replica sync API (createReplica, extractChanges,
/// synchronizeReplica, unRegisterReplica).
/// </summary>
public sealed class ReplicaSyncClient : IReplicaSyncClient
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new <see cref="ReplicaSyncClient"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client configured with the base address of the feature server.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> is <see langword="null"/>.</exception>
    public ReplicaSyncClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<CreateReplicaResult> CreateReplicaAsync(string serviceId, string replicaName, int[]? layerIds = null, CancellationToken ct = default)
    {
        var url = $"rest/services/{serviceId}/FeatureServer/createReplica";
        var parameters = new Dictionary<string, string>
        {
            ["replicaName"] = replicaName,
            ["syncModel"] = "perLayer",
            ["f"] = "json",
        };

        if (layerIds is { Length: > 0 })
        {
            parameters["layers"] = string.Join(',', layerIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        }

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);

        var root = doc.RootElement;
        ThrowIfError(root);

        var replicaId = GetRequiredString(root, "replicaID", "replicaId", "replica_id");
        var serverGen = GetCreateReplicaServerGen(root);

        return new CreateReplicaResult(replicaId, serverGen);
    }

    /// <inheritdoc />
    public async Task<ExtractChangesResult> ExtractChangesAsync(string serviceId, string replicaId, CancellationToken ct = default)
    {
        var url = $"rest/services/{serviceId}/FeatureServer/extractChanges";
        var parameters = new Dictionary<string, string>
        {
            ["replicaID"] = replicaId,
            ["f"] = "json",
        };

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);

        var root = doc.RootElement;
        ThrowIfError(root);

        var serverGen = GetRequiredInt64(root, "serverGen", "serverGeneration", "server_generation");
        var layerChanges = new List<LayerChangeSet>();

        if (root.TryGetProperty("layerChanges", out var layerChangesElement) && layerChangesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var layerElement in layerChangesElement.EnumerateArray())
            {
                layerChanges.Add(ParseLayerChangeSet(layerElement));
            }
        }

        return new ExtractChangesResult
        {
            LayerChanges = layerChanges.ToArray(),
            ServerGen = serverGen,
        };
    }

    /// <inheritdoc />
    public async Task<SynchronizeResult> SynchronizeReplicaAsync(string serviceId, string replicaId, string syncDirection = "download", CancellationToken ct = default)
    {
        var url = $"rest/services/{serviceId}/FeatureServer/synchronizeReplica";
        var parameters = new Dictionary<string, string>
        {
            ["replicaID"] = replicaId,
            ["syncDirection"] = syncDirection,
            ["f"] = "json",
        };

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);

        var root = doc.RootElement;
        ThrowIfError(root);

        var serverGen = GetRequiredInt64(root, "serverGen", "serverGeneration", "server_generation");

        return new SynchronizeResult(replicaId, serverGen);
    }

    /// <inheritdoc />
    public async Task UnRegisterReplicaAsync(string serviceId, string replicaId, CancellationToken ct = default)
    {
        var url = $"rest/services/{serviceId}/FeatureServer/unRegisterReplica";
        var parameters = new Dictionary<string, string>
        {
            ["replicaID"] = replicaId,
            ["f"] = "json",
        };

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(url, content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);

        ThrowIfError(doc.RootElement);
    }

    private static LayerChangeSet ParseLayerChangeSet(JsonElement element)
    {
        var layerId = element.GetProperty("id").GetInt32();
        string[]? adds = null;
        string[]? updates = null;
        long[]? deletes = null;

        if (element.TryGetProperty("addFeatures", out var addFeatures) && addFeatures.ValueKind == JsonValueKind.Array)
        {
            adds = addFeatures.EnumerateArray()
                .Select(f => f.GetRawText())
                .ToArray();
        }

        if (element.TryGetProperty("updateFeatures", out var updateFeatures) && updateFeatures.ValueKind == JsonValueKind.Array)
        {
            updates = updateFeatures.EnumerateArray()
                .Select(f => f.GetRawText())
                .ToArray();
        }

        if (element.TryGetProperty("deleteIds", out var deleteIds) && deleteIds.ValueKind == JsonValueKind.Array)
        {
            deletes = deleteIds.EnumerateArray()
                .Select(d => d.GetInt64())
                .ToArray();
        }

        return new LayerChangeSet
        {
            LayerId = layerId,
            AddFeaturesJson = adds,
            UpdateFeaturesJson = updates,
            DeleteIds = deletes,
        };
    }

    private static void ThrowIfError(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var message = error.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String
                ? msg.GetString()
                : "Unknown server error";

            var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : (int?)null;

            throw new InvalidOperationException($"Replica sync error{(code.HasValue ? $" ({code.Value})" : "")}: {message}");
        }
    }

    private static string GetRequiredString(JsonElement element, params string[] propertyNames)
    {
        if (TryGetProperty(element, out var property, propertyNames) && property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException($"Server did not return a required replica sync property: {string.Join(" or ", propertyNames)}.");
    }

    private static long GetRequiredInt64(JsonElement element, params string[] propertyNames)
    {
        if (TryGetInt64(element, out var value, propertyNames))
        {
            return value;
        }

        throw new InvalidOperationException($"Server did not return a required replica sync property: {string.Join(" or ", propertyNames)}.");
    }

    private static long GetCreateReplicaServerGen(JsonElement root)
    {
        if (TryGetInt64(root, out var serverGen, "serverGen", "serverGeneration", "server_generation"))
        {
            return serverGen;
        }

        if (TryGetProperty(root, out var layers, "layers") && layers.ValueKind == JsonValueKind.Array)
        {
            foreach (var layer in layers.EnumerateArray())
            {
                if (layer.ValueKind == JsonValueKind.Object &&
                    TryGetInt64(layer, out serverGen, "serverGen", "serverGeneration", "server_generation"))
                {
                    return serverGen;
                }
            }
        }

        throw new InvalidOperationException("Server did not return a required replica sync property: serverGen or layers[].serverGen.");
    }

    private static bool TryGetInt64(JsonElement element, out long value, params string[] propertyNames)
    {
        if (TryGetProperty(element, out var property, propertyNames))
        {
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.String &&
                long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, out JsonElement property, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }

        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out property))
            {
                return true;
            }
        }

        foreach (var candidate in element.EnumerateObject())
        {
            if (propertyNames.Any(name => string.Equals(name, candidate.Name, StringComparison.OrdinalIgnoreCase)))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
