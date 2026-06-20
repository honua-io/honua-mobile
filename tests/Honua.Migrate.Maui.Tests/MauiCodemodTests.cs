using Honua.Migrate.Maui;

namespace Honua.Migrate.Maui.Tests;

/// <summary>
/// Before/after unit coverage for the ArcGIS Maps SDK for .NET (MAUI) -> Honua
/// codemod. Each test writes a small MAUI source snippet to a temp project,
/// runs the codemod, and asserts the rewritten output plus the parity metrics.
/// </summary>
public sealed class MauiCodemodTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    private string WriteProject(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "honua-maui-codemod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(dir, name), content);
        }

        return dir;
    }

    private static MauiCodemodResult RunWrite(string dir) =>
        MauiCodemod.Run(new MauiCodemodOptions { RootDir = dir, Write = true });

    // ---------------------------------------------------------------------
    // Auto-migrated constructors
    // ---------------------------------------------------------------------

    [Fact]
    public void Rewrites_FeatureLayer_And_Replaces_Using()
    {
        var dir = WriteProject(("Page.cs", string.Join("\n",
            "using Esri.ArcGISRuntime.Mapping;",
            "public class Page",
            "{",
            "    public object Build() => new FeatureLayer();",
            "}")));

        var result = RunWrite(dir);

        Assert.Equal(1, result.FilesChanged);
        Assert.Equal(1, result.Metrics.AutoMigratedCallSites);
        Assert.Equal(0, result.Metrics.ManualCallSites);
        Assert.Equal(
            new CodemodKindMetrics(1, 1, 0),
            result.Metrics.ByKind[MauiConstructKind.FeatureLayer]);

        var output = File.ReadAllText(Path.Combine(dir, "Page.cs"));
        Assert.Contains("new HonuaFeatureLayer()", output);
        Assert.DoesNotContain("new FeatureLayer()", output);
        Assert.Contains("using Honua.Mobile.Maui.Mapping;", output);
        Assert.DoesNotContain("using Esri.ArcGISRuntime.Mapping;", output);
    }

    [Theory]
    [InlineData("Esri.ArcGISRuntime.Geometry", "MapPoint", "HonuaPoint", "Honua.Mobile.Sdk.Geometry")]
    [InlineData("Esri.ArcGISRuntime.Geometry", "Polyline", "HonuaPolyline", "Honua.Mobile.Sdk.Geometry")]
    [InlineData("Esri.ArcGISRuntime.Geometry", "Polygon", "HonuaPolygon", "Honua.Mobile.Sdk.Geometry")]
    [InlineData("Esri.ArcGISRuntime.Geometry", "Envelope", "HonuaEnvelope", "Honua.Mobile.Sdk.Geometry")]
    [InlineData("Esri.ArcGISRuntime.Geometry", "SpatialReference", "HonuaSpatialReference", "Honua.Mobile.Sdk.Geometry")]
    [InlineData("Esri.ArcGISRuntime.Mapping", "Map", "HonuaMap", "Honua.Mobile.Maui.Mapping")]
    [InlineData("Esri.ArcGISRuntime.Mapping", "Basemap", "HonuaBasemap", "Honua.Mobile.Maui.Mapping")]
    [InlineData("Esri.ArcGISRuntime.Maui", "MapView", "HonuaMapView", "Honua.Mobile.Maui.Controls")]
    [InlineData("Esri.ArcGISRuntime.Maui", "GraphicsOverlay", "HonuaGraphicsOverlay", "Honua.Mobile.Maui.Controls")]
    [InlineData("Esri.ArcGISRuntime.Symbology", "SimpleMarkerSymbol", "HonuaMarkerSymbol", "Honua.Mobile.Maui.Controls")]
    [InlineData("Esri.ArcGISRuntime.Symbology", "SimpleLineSymbol", "HonuaLineSymbol", "Honua.Mobile.Maui.Controls")]
    [InlineData("Esri.ArcGISRuntime.Symbology", "SimpleFillSymbol", "HonuaFillSymbol", "Honua.Mobile.Maui.Controls")]
    public void Rewrites_Auto_Constructor(string arcGisNs, string arcGisType, string honuaType, string honuaNs)
    {
        var dir = WriteProject(("File.cs", string.Join("\n",
            $"using {arcGisNs};",
            "public class C",
            "{",
            $"    public object M() => new {arcGisType}();",
            "}")));

        var result = RunWrite(dir);

        Assert.Equal(1, result.Metrics.AutoMigratedCallSites);
        var output = File.ReadAllText(Path.Combine(dir, "File.cs"));
        Assert.Contains($"new {honuaType}()", output);
        Assert.DoesNotContain($"new {arcGisType}()", output);
        Assert.Contains($"using {honuaNs};", output);
    }

    [Fact]
    public void Rewrites_FullyQualified_Constructor_Without_Using()
    {
        var dir = WriteProject(("File.cs", string.Join("\n",
            "public class C",
            "{",
            "    public object M() => new Esri.ArcGISRuntime.Geometry.MapPoint();",
            "}")));

        var result = RunWrite(dir);

        Assert.Equal(1, result.Metrics.AutoMigratedCallSites);
        var output = File.ReadAllText(Path.Combine(dir, "File.cs"));
        Assert.Contains("new HonuaPoint()", output);
    }

    [Fact]
    public void Rewrites_Multiple_Constructs_In_One_File()
    {
        var dir = WriteProject(("Page.cs", string.Join("\n",
            "using Esri.ArcGISRuntime.Mapping;",
            "using Esri.ArcGISRuntime.Geometry;",
            "public class Page",
            "{",
            "    public void Build()",
            "    {",
            "        var map = new Map();",
            "        var layer = new FeatureLayer();",
            "        var pt = new MapPoint();",
            "    }",
            "}")));

        var result = RunWrite(dir);

        Assert.Equal(3, result.Metrics.AutoMigratedCallSites);
        var output = File.ReadAllText(Path.Combine(dir, "Page.cs"));
        Assert.Contains("new HonuaMap()", output);
        Assert.Contains("new HonuaFeatureLayer()", output);
        Assert.Contains("new HonuaPoint()", output);
        Assert.Contains("using Honua.Mobile.Maui.Mapping;", output);
        Assert.Contains("using Honua.Mobile.Sdk.Geometry;", output);
    }

    // ---------------------------------------------------------------------
    // Guided-manual constructs (recognized, TODO marker emitted)
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("Esri.ArcGISRuntime.Data", "ServiceFeatureTable", MauiConstructKind.ServiceFeatureTable)]
    [InlineData("Esri.ArcGISRuntime.Data", "QueryParameters", MauiConstructKind.QueryParameters)]
    [InlineData("Esri.ArcGISRuntime.Maui", "SceneView", MauiConstructKind.SceneView)]
    [InlineData("Esri.ArcGISRuntime.Mapping", "Scene", MauiConstructKind.Scene)]
    public void Flags_GuidedManual_Construct(string arcGisNs, string arcGisType, MauiConstructKind kind)
    {
        var dir = WriteProject(("File.cs", string.Join("\n",
            $"using {arcGisNs};",
            "public class C",
            "{",
            $"    public object M() => new {arcGisType}();",
            "}")));

        var result = RunWrite(dir);

        Assert.Equal(0, result.Metrics.AutoMigratedCallSites);
        Assert.Equal(1, result.Metrics.ManualCallSites);
        var todo = Assert.Single(result.ManualTodos);
        Assert.Equal(kind, todo.Kind);

        // Recognized-only: source must be left untouched when not annotating.
        var output = File.ReadAllText(Path.Combine(dir, "File.cs"));
        Assert.Contains($"new {arcGisType}()", output);
    }

    [Fact]
    public void AnnotateTodos_Inserts_Inline_Marker()
    {
        var dir = WriteProject(("File.cs", string.Join("\n",
            "using Esri.ArcGISRuntime.Data;",
            "public class C",
            "{",
            "    public object M() => new ServiceFeatureTable();",
            "}")));

        var result = MauiCodemod.Run(new MauiCodemodOptions
        {
            RootDir = dir,
            Write = true,
            AnnotateTodos = true,
        });

        var output = File.ReadAllText(Path.Combine(dir, "File.cs"));
        Assert.Contains("TODO(honua-migrate)[ServiceFeatureTable]", output);
        Assert.Equal(1, result.FileResults.Single().AnnotatedTodoComments);
    }

    // ---------------------------------------------------------------------
    // Scoping / safety
    // ---------------------------------------------------------------------

    [Fact]
    public void Does_Not_Touch_Unrelated_Type_With_Same_Simple_Name()
    {
        // No ArcGIS using -> a user-defined "Map" must be left alone.
        var dir = WriteProject(("File.cs", string.Join("\n",
            "using System.Collections.Generic;",
            "public class Map { }",
            "public class C",
            "{",
            "    public object M() => new Map();",
            "}")));

        var result = RunWrite(dir);

        Assert.Equal(0, result.FilesChanged);
        Assert.Equal(0, result.Metrics.TotalCodemodScopedCallSites);
        var output = File.ReadAllText(Path.Combine(dir, "File.cs"));
        Assert.Contains("new Map()", output);
        Assert.DoesNotContain("HonuaMap", output);
    }

    [Fact]
    public void Dry_Run_Does_Not_Write()
    {
        var original = string.Join("\n",
            "using Esri.ArcGISRuntime.Mapping;",
            "public class C { public object M() => new FeatureLayer(); }");
        var dir = WriteProject(("File.cs", original));

        var result = MauiCodemod.Run(new MauiCodemodOptions { RootDir = dir, Write = false });

        Assert.Equal(1, result.Metrics.AutoMigratedCallSites);
        Assert.Equal(original, File.ReadAllText(Path.Combine(dir, "File.cs")));
    }

    [Fact]
    public void Skips_Bin_And_Obj_Directories()
    {
        var dir = WriteProject(("Real.cs", string.Join("\n",
            "using Esri.ArcGISRuntime.Mapping;",
            "public class C { public object M() => new FeatureLayer(); }")));
        var objDir = Path.Combine(dir, "obj");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(Path.Combine(objDir, "Generated.cs"), string.Join("\n",
            "using Esri.ArcGISRuntime.Mapping;",
            "public class G { public object M() => new FeatureLayer(); }"));

        var result = RunWrite(dir);

        Assert.Equal(1, result.FilesScanned);
        Assert.Equal(1, result.Metrics.AutoMigratedCallSites);
    }

    [Fact]
    public void Preserves_Constructor_Arguments()
    {
        var dir = WriteProject(("File.cs", string.Join("\n",
            "using Esri.ArcGISRuntime.Geometry;",
            "public class C",
            "{",
            "    public object M() => new MapPoint(-117.0, 34.0);",
            "}")));

        RunWrite(dir);

        var output = File.ReadAllText(Path.Combine(dir, "File.cs"));
        Assert.Contains("new HonuaPoint(-117.0, 34.0)", output);
    }
}
