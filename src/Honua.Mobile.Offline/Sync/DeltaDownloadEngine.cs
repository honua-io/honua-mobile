using System.Globalization;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.Sync;

public sealed class DeltaDownloadEngine
{
    private readonly IGeoPackageSyncStore _store;
    private readonly IReplicaSyncClient _replicaClient;
    private readonly DeltaDownloadOptions _options;

    public DeltaDownloadEngine(IGeoPackageSyncStore store, IReplicaSyncClient replicaClient, DeltaDownloadOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _replicaClient = replicaClient ?? throw new ArgumentNullException(nameof(replicaClient));
        _options = options ?? new DeltaDownloadOptions();
    }

    public async Task<DeltaDownloadResult> DownloadAsync(string serviceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        await _store.InitializeAsync(ct).ConfigureAwait(false);

        var replicaCursorKey = $"replica:{serviceId}";
        var replicaId = await _store.GetSyncCursorAsync(replicaCursorKey, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(replicaId))
        {
            var replicaName = _options.ReplicaName ?? $"honua-mobile-{serviceId}";
            var createResult = await _replicaClient.CreateReplicaAsync(serviceId, replicaName, _options.LayerIds, ct).ConfigureAwait(false);
            replicaId = createResult.ReplicaId;

            await _store.SetSyncCursorAsync(replicaCursorKey, replicaId, ct).ConfigureAwait(false);
            await _store.SetSyncCursorAsync($"servergen:{serviceId}", createResult.ServerGen.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        }

        var extractResult = await _replicaClient.ExtractChangesAsync(serviceId, replicaId, ct).ConfigureAwait(false);

        int totalAdds = 0;
        int totalUpdates = 0;
        int totalDeletes = 0;

        foreach (var layerChange in extractResult.LayerChanges)
        {
            var layerKey = layerChange.LayerId.ToString(CultureInfo.InvariantCulture);

            if (layerChange.AddFeaturesJson is { Length: > 0 })
            {
                foreach (var featureJson in layerChange.AddFeaturesJson)
                {
                    await _store.UpsertFeatureAsync(layerKey, featureJson, ct).ConfigureAwait(false);
                    totalAdds++;
                }
            }

            if (layerChange.UpdateFeaturesJson is { Length: > 0 })
            {
                foreach (var featureJson in layerChange.UpdateFeaturesJson)
                {
                    await _store.UpsertFeatureAsync(layerKey, featureJson, ct).ConfigureAwait(false);
                    totalUpdates++;
                }
            }

            if (layerChange.DeleteIds is { Length: > 0 })
            {
                foreach (var objectId in layerChange.DeleteIds)
                {
                    await _store.DeleteFeatureAsync(layerKey, objectId, ct).ConfigureAwait(false);
                    totalDeletes++;
                }
            }
        }

        var syncResult = await _replicaClient.SynchronizeReplicaAsync(serviceId, replicaId, "download", ct).ConfigureAwait(false);

        await _store.SetSyncCursorAsync($"servergen:{serviceId}", syncResult.ServerGen.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);

        return new DeltaDownloadResult
        {
            Adds = totalAdds,
            Updates = totalUpdates,
            Deletes = totalDeletes,
            ServerGen = syncResult.ServerGen,
        };
    }
}
