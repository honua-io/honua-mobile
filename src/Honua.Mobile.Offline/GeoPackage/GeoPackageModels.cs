using Honua.Mobile.Sdk.Scenes;

namespace Honua.Mobile.Offline.GeoPackage;

/// <summary>
/// Configuration options for <see cref="GeoPackageSyncStore"/>.
/// </summary>
public sealed class GeoPackageSyncStoreOptions
{
    /// <summary>
    /// File path to the SQLite GeoPackage database.
    /// </summary>
    public required string DatabasePath { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the parent directory of <see cref="DatabasePath"/> is created automatically. Defaults to <see langword="true"/>.
    /// </summary>
    public bool AutoCreateDirectory { get; init; } = true;

    /// <summary>
    /// Duration after which an in-progress operation claim is considered stale and can be reclaimed. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan InProgressLeaseTimeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// The type of offline edit operation.
/// </summary>
public enum OfflineOperationType
{
    /// <summary>Insert a new feature.</summary>
    Add,
    /// <summary>Update an existing feature.</summary>
    Update,
    /// <summary>Delete an existing feature.</summary>
    Delete,
}

/// <summary>
/// Represents a single offline edit operation queued for upload.
/// </summary>
public sealed class OfflineEditOperation
{
    /// <summary>
    /// Unique identifier for this operation. Auto-generated if not specified.
    /// </summary>
    public string OperationId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Identifies the feature layer this operation targets.
    /// </summary>
    public required string LayerKey { get; init; }

    /// <summary>
    /// The server-side collection or service endpoint to upload to.
    /// </summary>
    public required string TargetCollection { get; init; }

    /// <summary>
    /// Whether this is an add, update, or delete operation.
    /// </summary>
    public required OfflineOperationType OperationType { get; init; }

    /// <summary>
    /// JSON payload containing the feature data for the operation.
    /// </summary>
    public required string PayloadJson { get; init; }

    /// <summary>
    /// Upload priority; lower values are processed first. Defaults to 100.
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>
    /// UTC timestamp when the operation was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of previous upload attempts for this operation.
    /// </summary>
    public int AttemptCount { get; init; }
}

/// <summary>
/// A geographic bounding box defined by longitude/latitude extents.
/// </summary>
/// <param name="MinLongitude">Western boundary in decimal degrees.</param>
/// <param name="MinLatitude">Southern boundary in decimal degrees.</param>
/// <param name="MaxLongitude">Eastern boundary in decimal degrees.</param>
/// <param name="MaxLatitude">Northern boundary in decimal degrees.</param>
public sealed record BoundingBox(double MinLongitude, double MinLatitude, double MaxLongitude, double MaxLatitude);

/// <summary>
/// Metadata for a downloaded offline map area package stored in the local GeoPackage.
/// </summary>
public sealed class MapAreaPackage
{
    /// <summary>
    /// Unique identifier for this map area.
    /// </summary>
    public required string AreaId { get; init; }

    /// <summary>
    /// Human-readable name of the map area.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Geographic extent of the map area.
    /// </summary>
    public required BoundingBox BoundingBox { get; init; }

    /// <summary>
    /// Minimum tile zoom level included in the package.
    /// </summary>
    public int MinZoom { get; init; }

    /// <summary>
    /// Maximum tile zoom level included in the package.
    /// </summary>
    public int MaxZoom { get; init; }

    /// <summary>
    /// File path to the GeoPackage file containing the offline tile data.
    /// </summary>
    public required string GeoPackagePath { get; init; }

    /// <summary>
    /// UTC timestamp of the last update to this map area package.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Metadata for a downloaded immutable offline 3D scene package.
/// </summary>
public sealed class ScenePackageRecord
{
    /// <summary>
    /// Stable package identifier from the scene package manifest.
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Scene identifier rendered by this offline package.
    /// </summary>
    public required string SceneId { get; init; }

    /// <summary>
    /// Human-readable package name.
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Required product edition for package use.
    /// </summary>
    public required string EditionGate { get; init; }

    /// <summary>
    /// Server scene revision represented by this package.
    /// </summary>
    public required string ServerRevision { get; init; }

    /// <summary>
    /// WGS84 package extent.
    /// </summary>
    public HonuaSceneBounds? Extent { get; init; }

    /// <summary>
    /// Directory containing package-local scene assets.
    /// </summary>
    public required string PackageDirectory { get; init; }

    /// <summary>
    /// File path to the downloaded manifest JSON.
    /// </summary>
    public required string ManifestPath { get; init; }

    /// <summary>
    /// Derived validation state for the local package.
    /// </summary>
    public HonuaScenePackageState State { get; init; }

    /// <summary>
    /// Server-declared expected package size in bytes.
    /// </summary>
    public long DeclaredBytes { get; init; }

    /// <summary>
    /// Bytes stored on disk for downloaded assets.
    /// </summary>
    public long DownloadedBytes { get; init; }

    /// <summary>
    /// Number of required manifest assets.
    /// </summary>
    public int RequiredAssetCount { get; init; }

    /// <summary>
    /// Number of assets downloaded by this client.
    /// </summary>
    public int DownloadedAssetCount { get; init; }

    /// <summary>
    /// Optional manifest asset keys that were not downloaded.
    /// </summary>
    public IReadOnlyList<string> MissingOptionalAssetKeys { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Time after which the package should surface stale state.
    /// </summary>
    public DateTimeOffset? StaleAfterUtc { get; init; }

    /// <summary>
    /// Time after which protected offline package content must not render without revalidation.
    /// </summary>
    public DateTimeOffset? OfflineUseExpiresAtUtc { get; init; }

    /// <summary>
    /// Expiry for download or refresh credentials.
    /// </summary>
    public DateTimeOffset? AuthExpiresAtUtc { get; init; }

    /// <summary>
    /// Last catalog update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
