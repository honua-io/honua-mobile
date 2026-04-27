using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.MapAreas;

/// <summary>
/// Parameters for downloading an offline map area.
/// </summary>
public sealed class MapAreaDownloadRequest
{
    /// <summary>
    /// Unique identifier for the map area.
    /// </summary>
    public required string AreaId { get; init; }

    /// <summary>
    /// Human-readable name of the map area.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Geographic extent of the area to download.
    /// </summary>
    public required BoundingBox BoundingBox { get; init; }

    /// <summary>
    /// Directory where the GeoPackage file will be created.
    /// </summary>
    public required string OutputDirectory { get; init; }

    /// <summary>
    /// Minimum tile zoom level to include.
    /// </summary>
    public int MinZoom { get; init; }

    /// <summary>
    /// Maximum tile zoom level to include.
    /// </summary>
    public int MaxZoom { get; init; }

    /// <summary>
    /// Maximum allowed payload size per layer in bytes. Defaults to 32 MB.
    /// </summary>
    public long MaxLayerPayloadBytes { get; init; } = 32L * 1024L * 1024L;

    /// <summary>
    /// Layer sources to download, processed in priority order.
    /// </summary>
    public IReadOnlyList<MapLayerDownloadSource> Layers { get; init; } = [];
}

/// <summary>
/// Describes a single map layer source to download for an offline map area.
/// </summary>
public sealed class MapLayerDownloadSource
{
    /// <summary>
    /// Unique key identifying this layer within the map area package.
    /// </summary>
    public required string LayerKey { get; init; }

    /// <summary>
    /// URL template for the layer data. Supports placeholders: <c>{minLon}</c>, <c>{minLat}</c>,
    /// <c>{maxLon}</c>, <c>{maxLat}</c>, <c>{minZoom}</c>, <c>{maxZoom}</c>.
    /// </summary>
    public required string SourceUrl { get; init; }

    /// <summary>
    /// Download priority; lower values are processed first. Defaults to 100.
    /// </summary>
    public int Priority { get; init; } = 100;

    /// <summary>
    /// Optional content type override. When <see langword="null"/>, the server's content-type header is used.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// When <see langword="true"/>, a failed download for this layer causes the entire operation to fail. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Required { get; init; } = true;
}

/// <summary>
/// Result of downloading an offline map area.
/// </summary>
public sealed class MapAreaDownloadResult
{
    /// <summary>
    /// File path to the created GeoPackage containing the downloaded layer data.
    /// </summary>
    public required string GeoPackagePath { get; init; }

    /// <summary>
    /// Number of layers successfully downloaded.
    /// </summary>
    public int DownloadedLayerCount { get; init; }

    /// <summary>
    /// Total bytes downloaded across all layers.
    /// </summary>
    public long DownloadedBytes { get; init; }
}

/// <summary>
/// Downloads map layer data for offline use into a local GeoPackage file.
/// </summary>
public interface IMapAreaDownloader
{
    /// <summary>
    /// Downloads all layers specified in <paramref name="request"/> and stores them in a GeoPackage file.
    /// </summary>
    /// <param name="request">The download parameters including area bounds, layers, and output directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the GeoPackage path, layer count, and total bytes downloaded.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is <see langword="null"/>.</exception>
    Task<MapAreaDownloadResult> DownloadAsync(MapAreaDownloadRequest request, CancellationToken ct = default);
}
