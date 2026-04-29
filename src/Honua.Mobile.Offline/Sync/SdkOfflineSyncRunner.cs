using Honua.Sdk.Offline.Abstractions;
using SdkOfflineSyncEngine = Honua.Sdk.Offline.OfflineSyncEngine;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Exposes the SDK offline sync engine through the mobile sync runner contract used by app lifecycle and background scheduling.
/// </summary>
public sealed class SdkOfflineSyncRunner : IOfflineSyncRunner
{
    private readonly SdkOfflineSyncEngine _engine;
    private readonly OfflinePackageManifest _manifest;

    /// <summary>
    /// Initializes a new <see cref="SdkOfflineSyncRunner"/>.
    /// </summary>
    /// <param name="engine">SDK offline sync engine.</param>
    /// <param name="manifest">Offline package manifest to sync.</param>
    public SdkOfflineSyncRunner(SdkOfflineSyncEngine engine, OfflinePackageManifest manifest)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    /// <inheritdoc />
    public async Task<SyncRunResult> SyncAsync(CancellationToken ct = default)
    {
        var result = await _engine.SyncAsync(_manifest, ct).ConfigureAwait(false);
        var failures = result.Push.Failures.Concat(result.Pull.Failures)
            .Select(failure => new SyncFailure(failure.OperationId ?? failure.SourceId ?? _manifest.PackageId, failure.Reason))
            .ToArray();

        return new SyncRunResult
        {
            Loaded = result.Push.Loaded,
            Succeeded = result.Push.Succeeded,
            Failed = failures.Length,
            Failures = failures,
        };
    }
}
