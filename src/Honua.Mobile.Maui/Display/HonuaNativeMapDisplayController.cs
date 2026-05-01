using Honua.Sdk.Abstractions.Features;

namespace Honua.Mobile.Maui.Display;

/// <summary>
/// Coordinates SDK feature queries with a native map adapter.
/// </summary>
public sealed class HonuaNativeMapDisplayController
{
    private readonly IHonuaNativeMapAdapter _adapter;
    private readonly IHonuaFeatureQueryClient _featureQueryClient;

    public HonuaNativeMapDisplayController(
        IHonuaNativeMapAdapter adapter,
        IHonuaFeatureQueryClient featureQueryClient)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _featureQueryClient = featureQueryClient ?? throw new ArgumentNullException(nameof(featureQueryClient));
    }

    /// <summary>
    /// Applies the scene, sets the viewport, queries visible feature layers, and forwards results to the adapter.
    /// </summary>
    /// <param name="scene">Layer and annotation scene to render.</param>
    /// <param name="view">Current map viewport.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task RefreshAsync(
        HonuaNativeMapScene scene,
        HonuaNativeMapViewState view,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(view);

        scene.Validate();
        view.Validate();

        await _adapter.ApplySceneAsync(scene, ct).ConfigureAwait(false);
        await _adapter.SetViewAsync(view, ct).ConfigureAwait(false);

        foreach (var layer in scene.Layers
            .Where(layer => layer.IsVisible && layer.Kind == HonuaNativeMapLayerKind.Feature)
            .OrderBy(layer => layer.ZIndex))
        {
            var query = CreateFeatureQuery(layer, view);
            var features = await _featureQueryClient.QueryAsync(query, ct).ConfigureAwait(false);
            await _adapter.RenderFeaturesAsync(layer, features, ct).ConfigureAwait(false);
        }

        await _adapter.RenderAnnotationsAsync(scene.Annotations, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the SDK feature query for a visible native feature layer.
    /// </summary>
    /// <param name="layer">Layer to query.</param>
    /// <param name="view">Viewport bounds and output CRS.</param>
    /// <returns>SDK feature query request for the layer and view.</returns>
    public static FeatureQueryRequest CreateFeatureQuery(
        HonuaNativeMapLayer layer,
        HonuaNativeMapViewState view)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(view);

        layer.Validate();
        view.Validate();

        return new FeatureQueryRequest
        {
            Source = layer.Source.ToFeatureSource(),
            Filter = layer.Filter,
            FilterLanguage = layer.FilterLanguage,
            OutFields = layer.OutFields.ToArray(),
            ReturnGeometry = true,
            Bbox = view.Extent,
            OutputCrs = view.DisplayCrs,
        };
    }
}
