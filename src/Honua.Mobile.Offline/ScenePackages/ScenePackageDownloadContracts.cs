using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Sdk.Scenes;

namespace Honua.Mobile.Offline.ScenePackages;

/// <summary>
/// Parameters for downloading an immutable offline 3D scene package.
/// </summary>
public sealed class ScenePackageDownloadRequest
{
    /// <summary>
    /// Manifest describing the package assets to download.
    /// </summary>
    public required HonuaScenePackageManifest Manifest { get; init; }

    /// <summary>
    /// Base URI used to resolve package-local manifest asset paths.
    /// </summary>
    public required Uri AssetBaseUri { get; init; }

    /// <summary>
    /// Directory where the ready package directory and staging directory are created.
    /// </summary>
    public required string OutputDirectory { get; init; }

    /// <summary>
    /// Progress callback invoked as assets are downloaded.
    /// </summary>
    public IProgress<ScenePackageDownloadProgress>? Progress { get; init; }

    /// <summary>
    /// When <see langword="true"/>, partial package staging files are deleted after a failed download.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool CleanupPartialPackageOnFailure { get; init; } = true;

    /// <summary>
    /// Optional clock override for deterministic tests.
    /// </summary>
    public DateTimeOffset? UtcNow { get; init; }
}

/// <summary>
/// Progress information for a scene package download.
/// </summary>
public sealed class ScenePackageDownloadProgress
{
    /// <summary>
    /// Asset key currently being downloaded.
    /// </summary>
    public required string AssetKey { get; init; }

    /// <summary>
    /// Number of assets fully downloaded so far.
    /// </summary>
    public int CompletedAssetCount { get; init; }

    /// <summary>
    /// Total assets listed in the manifest.
    /// </summary>
    public int TotalAssetCount { get; init; }

    /// <summary>
    /// Bytes downloaded during this operation.
    /// </summary>
    public long DownloadedBytes { get; init; }

    /// <summary>
    /// Declared package byte budget, when advertised by the manifest.
    /// </summary>
    public long? DeclaredBytes { get; init; }
}

/// <summary>
/// Result of downloading an offline 3D scene package.
/// </summary>
public sealed class ScenePackageDownloadResult
{
    /// <summary>
    /// Ready package directory containing package-local scene assets.
    /// </summary>
    public required string PackageDirectory { get; init; }

    /// <summary>
    /// File path to the downloaded manifest JSON.
    /// </summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    /// Number of assets downloaded by this operation.
    /// </summary>
    public int DownloadedAssetCount { get; init; }

    /// <summary>
    /// Bytes downloaded by this operation.
    /// </summary>
    public long DownloadedBytes { get; init; }

    /// <summary>
    /// Catalog record persisted in the offline store.
    /// </summary>
    public required ScenePackageRecord CatalogRecord { get; init; }
}

/// <summary>
/// Downloads immutable offline 3D scene package assets and registers the local package catalog entry.
/// </summary>
public interface IHonuaScenePackageDownloader
{
    /// <summary>
    /// Downloads all manifest assets into a ready package directory and registers package metadata.
    /// </summary>
    /// <param name="request">Package download request.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ScenePackageDownloadResult> DownloadAsync(
        ScenePackageDownloadRequest request,
        CancellationToken ct = default);
}
