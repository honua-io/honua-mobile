// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Globalization;
using System.Text.Json;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Sdk.Offline.Abstractions;

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
        var serverGenCursorKey = $"servergen:{serviceId}";
        var replicaId = await _store.GetSyncCursorAsync(replicaCursorKey, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(replicaId))
        {
            var replicaName = _options.ReplicaName ?? $"honua-mobile-{serviceId}";
            var createResult = await _replicaClient.CreateReplicaAsync(serviceId, replicaName, _options.LayerIds, ct).ConfigureAwait(false);
            replicaId = createResult.ReplicaId;

            await _store.SetSyncCursorAsync(replicaCursorKey, replicaId, ct).ConfigureAwait(false);
            await _store.SetSyncCursorAsync(serverGenCursorKey, createResult.ServerGen.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        }

        // Read back the persisted server generation so extractChanges is scoped to changes since the
        // last sync. Without this the server re-extracts the entire change set every run, defeating
        // delta sync. The cursor is null on the very first run for a pre-existing replica, in which
        // case the SDK omits the "since" bound and the server returns the full set once.
        var sinceServerGen = await _store.GetSyncCursorAsync(serverGenCursorKey, ct).ConfigureAwait(false);

        var extractResult = await _replicaClient.ExtractChangesAsync(serviceId, replicaId, sinceServerGen, ct).ConfigureAwait(false);

        int totalAdds = 0;
        int totalUpdates = 0;
        int totalDeletes = 0;

        foreach (var layerChange in extractResult.LayerChanges)
        {
            var layerKey = layerChange.LayerId.ToString(CultureInfo.InvariantCulture);

            // Adds and updates are both upserts; apply them as a single batched transaction
            // per layer so the store pays one durable write instead of one fsync per feature.
            var upserts = new List<string>();
            if (layerChange.AddFeaturesJson is { Count: > 0 } adds)
            {
                upserts.AddRange(adds);
                totalAdds += adds.Count;
            }

            if (layerChange.UpdateFeaturesJson is { Count: > 0 } updates)
            {
                upserts.AddRange(updates);
                totalUpdates += updates.Count;
            }

            if (upserts.Count > 0)
            {
                await _store.UpsertFeaturesAsync(layerKey, upserts, ct).ConfigureAwait(false);
            }

            if (layerChange.DeleteIds is { Count: > 0 } deletes)
            {
                await _store.DeleteFeaturesAsync(layerKey, deletes, ct).ConfigureAwait(false);
                totalDeletes += deletes.Count;
            }
        }

        // Acknowledge the replica server-side so it can reclaim change-tracking state. The
        // synchronize response generation is intentionally not used as the cursor (see below).
        await _replicaClient.SynchronizeReplicaAsync(serviceId, replicaId, "download", ct).ConfigureAwait(false);

        // Advance the "since" cursor to the generation the applied changes were extracted at,
        // NOT the synchronize response generation. Synchronize runs after extract over a
        // separate round-trip, so the synchronize generation can be strictly greater than
        // extractResult.ServerGen when the server commits edits between the two calls. Persisting
        // the higher value would make the next extractChanges skip the changes in
        // (extractResult.ServerGen, synchronizeGen] permanently.
        await _store.SetSyncCursorAsync(serverGenCursorKey, extractResult.ServerGen.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);

        return new DeltaDownloadResult
        {
            Adds = totalAdds,
            Updates = totalUpdates,
            Deletes = totalDeletes,
            ServerGen = extractResult.ServerGen,
        };
    }
}
