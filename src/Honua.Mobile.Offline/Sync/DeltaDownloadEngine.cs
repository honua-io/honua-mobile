using System.Globalization;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Downloads server-side feature changes into the local GeoPackage cache using
/// the replica/extract-changes/synchronize workflow.
/// </summary>
public sealed class DeltaDownloadEngine
{
    private readonly IGeoPackageSyncStore _store;
    private readonly IReplicaSyncClient _replicaClient;
    private readonly DeltaDownloadOptions _options;

    /// <summary>
    /// Initializes a new <see cref="DeltaDownloadEngine"/>.
    /// </summary>
    /// <param name="store">The local sync store for persisting features and cursors.</param>
    /// <param name="replicaClient">Client for the server replica sync API.</param>
    /// <param name="options">Download options; defaults are used when <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> or <paramref name="replicaClient"/> is <see langword="null"/>.</exception>
    public DeltaDownloadEngine(IGeoPackageSyncStore store, IReplicaSyncClient replicaClient, DeltaDownloadOptions? options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _replicaClient = replicaClient ?? throw new ArgumentNullException(nameof(replicaClient));
        _options = options ?? new DeltaDownloadOptions();
    }

    /// <summary>
    /// Downloads delta changes from the server for the specified service,
    /// creating a replica on first run and extracting incremental changes on subsequent runs.
    /// </summary>
    /// <param name="serviceId">The feature service to download changes from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A summary of adds, updates, and deletes applied to the local cache.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="serviceId"/> is null or whitespace.</exception>
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
