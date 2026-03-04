using Honua.Mobile.Offline.GeoPackage;

namespace Honua.Mobile.Offline.MapAreas;

public sealed class MapAreaDownloadRequest
{
    public required string AreaId { get; init; }

    public required string Name { get; init; }

    public required BoundingBox BoundingBox { get; init; }

    public required string OutputDirectory { get; init; }

    public int MinZoom { get; init; }

    public int MaxZoom { get; init; }

    public IReadOnlyList<MapLayerDownloadSource> Layers { get; init; } = [];
}

public sealed class MapLayerDownloadSource
{
    public required string LayerKey { get; init; }

    public required string SourceUrl { get; init; }

    public int Priority { get; init; } = 100;

    public string? ContentType { get; init; }

    public bool Required { get; init; } = true;
}

public sealed class MapAreaDownloadResult
{
    public required string GeoPackagePath { get; init; }

    public int DownloadedLayerCount { get; init; }

    public long DownloadedBytes { get; init; }
}

public interface IMapAreaDownloader
{
    Task<MapAreaDownloadResult> DownloadAsync(MapAreaDownloadRequest request, CancellationToken ct = default);
}
