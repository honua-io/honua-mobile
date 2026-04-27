using System.Globalization;
using System.Text.Json;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// HTTP-based client for the server-side replica sync API (createReplica, extractChanges,
/// synchronizeReplica, unRegisterReplica).
/// </summary>
public sealed class ReplicaSyncClient : IReplicaSyncClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

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

        var replicaId = root.GetProperty("replicaID").GetString()
            ?? throw new InvalidOperationException("Server did not return a replicaID.");
        var serverGen = root.GetProperty("serverGen").GetInt64();

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

        var serverGen = root.GetProperty("serverGen").GetInt64();
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

        var serverGen = root.GetProperty("serverGen").GetInt64();

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
}
