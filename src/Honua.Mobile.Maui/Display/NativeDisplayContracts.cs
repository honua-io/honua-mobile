using Honua.Mobile.Maui.Annotations;
using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Maui.Display;

/// <summary>
/// Source-backed layer categories a native map renderer can bind to.
/// </summary>
public enum HonuaNativeMapLayerKind
{
    /// <summary>Feature records queried through SDK feature abstractions.</summary>
    Feature,
    /// <summary>Vector tiles provided by the source descriptor.</summary>
    VectorTile,
    /// <summary>Raster tiles provided by the source descriptor.</summary>
    RasterTile,
}

/// <summary>
/// Projection declaration for a native map layer. Projection execution belongs to the renderer.
/// </summary>
public sealed record HonuaNativeMapProjection
{
    public const string Wgs84 = "EPSG:4326";
    public const string WebMercator = "EPSG:3857";

    public string SourceCrs { get; init; } = Wgs84;

    public string DisplayCrs { get; init; } = WebMercator;

    public bool RequiresProjection =>
        !string.Equals(SourceCrs, DisplayCrs, StringComparison.OrdinalIgnoreCase);

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SourceCrs);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayCrs);
    }
}

/// <summary>
/// SDK source descriptor plus mobile rendering metadata for a native map layer.
/// </summary>
public sealed record HonuaNativeMapLayer
{
    public required string Id { get; init; }

    public HonuaNativeMapLayerKind Kind { get; init; } = HonuaNativeMapLayerKind.Feature;

    public required SourceDescriptor Source { get; init; }

    public HonuaNativeMapProjection Projection { get; init; } = new();

    public bool IsVisible { get; init; } = true;

    public int ZIndex { get; init; }

    public string? Filter { get; init; }

    public FeatureFilterLanguage FilterLanguage { get; init; } = FeatureFilterLanguage.ProviderDefault;

    public IReadOnlyList<string> OutFields { get; init; } = ["*"];

    public IReadOnlyDictionary<string, object?> RendererHints { get; init; } = new Dictionary<string, object?>();

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Source.Protocol);
        ArgumentNullException.ThrowIfNull(Source.Locator);
        ArgumentNullException.ThrowIfNull(OutFields);
        Projection.Validate();
    }
}

/// <summary>
/// Native map scene definition consumed by a platform-specific map adapter.
/// </summary>
public sealed record HonuaNativeMapScene
{
    public IReadOnlyList<HonuaNativeMapLayer> Layers { get; init; } = [];

    public IReadOnlyList<HonuaAnnotation> Annotations { get; init; } = [];

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Layers);
        ArgumentNullException.ThrowIfNull(Annotations);

        var layerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in Layers)
        {
            ArgumentNullException.ThrowIfNull(layer);
            layer.Validate();

            if (!layerIds.Add(layer.Id))
            {
                throw new InvalidOperationException($"Native map layer '{layer.Id}' is defined more than once.");
            }
        }
    }
}

/// <summary>
/// Native map viewport expressed with SDK feature query bounds.
/// </summary>
public sealed record HonuaNativeMapViewState
{
    public required FeatureBoundingBox Extent { get; init; }

    public string DisplayCrs { get; init; } = HonuaNativeMapProjection.WebMercator;

    public double? RotationDegrees { get; init; }

    public double? ScaleDenominator { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Extent);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayCrs);

        if (!double.IsFinite(Extent.MinX)
            || !double.IsFinite(Extent.MinY)
            || !double.IsFinite(Extent.MaxX)
            || !double.IsFinite(Extent.MaxY))
        {
            throw new ArgumentException("View extent coordinates must be finite.", nameof(Extent));
        }

        if (Extent.MaxX < Extent.MinX)
        {
            throw new ArgumentException("View extent MaxX must be greater than or equal to MinX.", nameof(Extent));
        }

        if (Extent.MaxY < Extent.MinY)
        {
            throw new ArgumentException("View extent MaxY must be greater than or equal to MinY.", nameof(Extent));
        }

        if (ScaleDenominator is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ScaleDenominator), "Scale denominator must be positive.");
        }
    }
}

/// <summary>
/// Platform renderer boundary for native .NET map display implementations.
/// </summary>
public interface IHonuaNativeMapAdapter
{
    Task ApplySceneAsync(HonuaNativeMapScene scene, CancellationToken ct = default);

    Task SetViewAsync(HonuaNativeMapViewState view, CancellationToken ct = default);

    Task RenderFeaturesAsync(
        HonuaNativeMapLayer layer,
        FeatureQueryResult features,
        CancellationToken ct = default);

    Task RenderAnnotationsAsync(IReadOnlyList<HonuaAnnotation> annotations, CancellationToken ct = default);
}
