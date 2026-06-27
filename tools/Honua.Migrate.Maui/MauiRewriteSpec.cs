namespace Honua.Migrate.Maui;

/// <summary>
/// The category of an ArcGIS Maps SDK for .NET (MAUI) construct that the codemod
/// recognizes. Mirrors the <c>CodemodConstructorKind</c> union used by the
/// JavaScript and Python honua-migrate codemods so the scan/translate/report
/// playbook stays consistent across SDKs.
/// </summary>
public enum MauiConstructKind
{
    MapView,
    SceneView,
    Map,
    Scene,
    Basemap,
    FeatureLayer,
    ServiceFeatureTable,
    GraphicsOverlay,
    Graphic,
    SimpleMarkerSymbol,
    SimpleLineSymbol,
    SimpleFillSymbol,
    MapPoint,
    Polyline,
    Polygon,
    Envelope,
    SpatialReference,
    QueryParameters,
}

/// <summary>
/// One row of the ArcGIS-MAUI → Honua mapping table. Equivalent to a
/// <c>ConstructorRewriteSpec</c> in the JS codemod: it pairs a recognized ArcGIS
/// type (by simple name + the namespaces it can be imported from) with the Honua
/// replacement symbol and an automation classification.
/// </summary>
internal sealed record MauiRewriteSpec(
    MauiConstructKind Kind,
    string ArcGisTypeName,
    IReadOnlyList<string> ArcGisNamespaces,
    string HonuaTypeName,
    string HonuaNamespace,
    MauiRewriteMode Mode,
    string? Note = null);

/// <summary>
/// How a recognized construct is handled. <see cref="AutoConstructor"/> means a
/// deterministic 1:1 constructor rewrite is emitted; <see cref="GuidedManual"/>
/// means the construct is recognized but has no safe automatic rewrite, so a
/// <c>TODO(honua-migrate)</c> review marker is emitted instead.
/// </summary>
internal enum MauiRewriteMode
{
    AutoConstructor,
    GuidedManual,
}

internal static class MauiMappingTable
{
    // ArcGIS Maps SDK for .NET root namespaces that the MAUI runtime exposes
    // these types under. Kept explicit (rather than a single prefix match) so
    // unrelated user types that happen to share a simple name are never touched.
    private const string GeometryNs = "Esri.ArcGISRuntime.Geometry";
    private const string MappingNs = "Esri.ArcGISRuntime.Mapping";
    private const string SymbologyNs = "Esri.ArcGISRuntime.Symbology";
    private const string DataNs = "Esri.ArcGISRuntime.Data";
    private const string UiControlsNs = "Esri.ArcGISRuntime.Maui";

    private const string HonuaMapping = "Honua.Mobile.Maui.Mapping";
    private const string HonuaControls = "Honua.Mobile.Maui.Controls";
    private const string HonuaGeometry = "Honua.Mobile.Sdk.Geometry";
    private const string HonuaData = "Honua.Mobile.Sdk.Data";

    /// <summary>
    /// The canonical mapping table. The order is stable for deterministic report
    /// output. Auto-rewritten constructors are the high-frequency
    /// MapView/Map/layers/graphics/geometry surface called out in
    /// docs/guides/migration-guide.md; the long tail (portal items, renderers,
    /// tasks, widgets) is recognized and routed to guided-manual TODO markers.
    /// </summary>
    public static readonly IReadOnlyList<MauiRewriteSpec> Specs = new MauiRewriteSpec[]
    {
        // --- View / control surface -----------------------------------------
        new(MauiConstructKind.MapView, "MapView", new[] { UiControlsNs },
            "HonuaMapView", HonuaControls, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.SceneView, "SceneView", new[] { UiControlsNs },
            "HonuaSceneView", HonuaControls, MauiRewriteMode.GuidedManual,
            "SceneView maps to HonuaSceneView, but 3D scene wiring (surface, camera, scene layers) needs manual review."),

        // --- Map / scene documents ------------------------------------------
        new(MauiConstructKind.Map, "Map", new[] { MappingNs },
            "HonuaMap", HonuaMapping, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.Scene, "Scene", new[] { MappingNs },
            "HonuaScene", HonuaMapping, MauiRewriteMode.GuidedManual,
            "Scene maps to HonuaScene; elevation/ground sources require manual configuration."),
        new(MauiConstructKind.Basemap, "Basemap", new[] { MappingNs },
            "HonuaBasemap", HonuaMapping, MauiRewriteMode.AutoConstructor),

        // --- Layers ----------------------------------------------------------
        new(MauiConstructKind.FeatureLayer, "FeatureLayer", new[] { MappingNs },
            "HonuaFeatureLayer", HonuaMapping, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.GraphicsOverlay, "GraphicsOverlay", new[] { UiControlsNs },
            "HonuaGraphicsOverlay", HonuaControls, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.ServiceFeatureTable, "ServiceFeatureTable", new[] { DataNs },
            "HonuaFeatureSource", HonuaData, MauiRewriteMode.GuidedManual,
            "ServiceFeatureTable becomes a HonuaFeatureSource bound to a Honua service id + layer id; the REST/portal URL must be translated manually."),

        // --- Graphics & symbols ---------------------------------------------
        new(MauiConstructKind.Graphic, "Graphic", new[] { UiControlsNs, MappingNs },
            "HonuaGraphic", HonuaControls, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.SimpleMarkerSymbol, "SimpleMarkerSymbol", new[] { SymbologyNs },
            "HonuaMarkerSymbol", HonuaControls, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.SimpleLineSymbol, "SimpleLineSymbol", new[] { SymbologyNs },
            "HonuaLineSymbol", HonuaControls, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.SimpleFillSymbol, "SimpleFillSymbol", new[] { SymbologyNs },
            "HonuaFillSymbol", HonuaControls, MauiRewriteMode.AutoConstructor),

        // --- Geometry --------------------------------------------------------
        new(MauiConstructKind.MapPoint, "MapPoint", new[] { GeometryNs },
            "HonuaPoint", HonuaGeometry, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.Polyline, "Polyline", new[] { GeometryNs },
            "HonuaPolyline", HonuaGeometry, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.Polygon, "Polygon", new[] { GeometryNs },
            "HonuaPolygon", HonuaGeometry, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.Envelope, "Envelope", new[] { GeometryNs },
            "HonuaEnvelope", HonuaGeometry, MauiRewriteMode.AutoConstructor),
        new(MauiConstructKind.SpatialReference, "SpatialReference", new[] { GeometryNs },
            "HonuaSpatialReference", HonuaGeometry, MauiRewriteMode.AutoConstructor),

        // --- Query -----------------------------------------------------------
        new(MauiConstructKind.QueryParameters, "QueryParameters", new[] { DataNs },
            "HonuaQuery", HonuaData, MauiRewriteMode.GuidedManual,
            "QueryParameters maps to HonuaQuery, but WhereClause/Geometry/SpatialRelationship members need manual property renames."),
    };

    private static readonly Lazy<IReadOnlyDictionary<string, MauiRewriteSpec>> ByArcGisTypeNameLookup =
        new(() =>
        {
            var map = new Dictionary<string, MauiRewriteSpec>(StringComparer.Ordinal);
            foreach (var spec in Specs)
            {
                // Simple names are unique across the table by construction.
                map[spec.ArcGisTypeName] = spec;
            }

            return map;
        });

    /// <summary>The set of ArcGIS namespaces any recognized type can live in.</summary>
    public static readonly IReadOnlySet<string> ArcGisNamespaces =
        Specs.SelectMany(s => s.ArcGisNamespaces).ToHashSet(StringComparer.Ordinal);

    public static MauiRewriteSpec? TryGetByArcGisTypeName(string simpleName) =>
        ByArcGisTypeNameLookup.Value.TryGetValue(simpleName, out var spec) ? spec : null;
}
