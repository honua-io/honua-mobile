using Honua.Mobile.Maui;
using Honua.Mobile.Maui.Annotations;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.ScenePackages;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Mobile.Maui.Tests;

public sealed class HonuaAnnotationLayerTests
{
    [Fact]
    public void DrawPoint_AppliesDefaultStyleAndStoresAnnotation()
    {
        var layer = new HonuaAnnotationLayer()
            .SetFillColor("#FF0000")
            .SetStrokeColor("#00FF00")
            .SetStrokeWidth(4)
            .SetOpacity(0.5);

        var annotation = layer.DrawPoint(new HonuaMapCoordinate(21.3069, -157.8583), id: "poi-1");

        Assert.Equal("poi-1", annotation.Id);
        Assert.Equal(HonuaAnnotationType.Point, annotation.Type);
        Assert.Equal("#FF0000", annotation.Style.FillColor);
        Assert.Equal("#00FF00", annotation.Style.StrokeColor);
        Assert.Equal(4, annotation.Style.StrokeWidth);
        Assert.Equal(0.5, annotation.Style.Opacity);
        Assert.Single(layer.Annotations);
    }

    [Fact]
    public void DrawPolyline_RequiresAtLeastTwoCoordinates()
    {
        var layer = new HonuaAnnotationLayer();

        var ex = Assert.Throws<ArgumentException>(() =>
            layer.DrawPolyline([new HonuaMapCoordinate(21.30, -157.85)]));

        Assert.Contains("at least 2", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrawPolygon_RequiresAtLeastThreeCoordinates()
    {
        var layer = new HonuaAnnotationLayer();

        var ex = Assert.Throws<ArgumentException>(() =>
            layer.DrawPolygon(
            [
                new HonuaMapCoordinate(21.30, -157.85),
                new HonuaMapCoordinate(21.31, -157.86),
            ]));

        Assert.Contains("at least 3", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DrawText_RequiresText()
    {
        var layer = new HonuaAnnotationLayer();

        Assert.Throws<ArgumentException>(() =>
            layer.DrawText(new HonuaMapCoordinate(21.30, -157.85), " "));
    }

    [Fact]
    public void AddAnnotation_RejectsDuplicateIds()
    {
        var layer = new HonuaAnnotationLayer();
        layer.DrawPoint(new HonuaMapCoordinate(21.30, -157.85), id: "duplicate");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            layer.DrawText(new HonuaMapCoordinate(21.31, -157.86), "note", id: "duplicate"));

        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateAnnotation_ReplacesAnnotationAndPreservesCreatedAt()
    {
        var layer = new HonuaAnnotationLayer();
        var original = layer.DrawText(new HonuaMapCoordinate(21.30, -157.85), "before", id: "label");

        var updated = layer.UpdateAnnotation("label", annotation => annotation with
        {
            Text = "after",
            Style = annotation.Style.SetOpacity(0.25),
        });

        Assert.Equal("after", updated.Text);
        Assert.Equal(0.25, updated.Style.Opacity);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= original.UpdatedAt);
        Assert.Equal("after", layer.Annotations.Single().Text);
    }

    [Fact]
    public void RemoveAnnotation_RemovesById()
    {
        var layer = new HonuaAnnotationLayer();
        layer.DrawPoint(new HonuaMapCoordinate(21.30, -157.85), id: "poi");

        Assert.True(layer.RemoveAnnotation("poi"));
        Assert.False(layer.RemoveAnnotation("poi"));
        Assert.Empty(layer.Annotations);
    }

    [Fact]
    public void GetAnnotationsInBounds_ReturnsIntersectingAnnotations()
    {
        var layer = new HonuaAnnotationLayer();
        var inside = layer.DrawPoint(new HonuaMapCoordinate(21.3069, -157.8583), id: "inside");
        layer.DrawPoint(new HonuaMapCoordinate(20.75, -156.45), id: "outside");
        var crossing = layer.DrawPolyline(
        [
            new HonuaMapCoordinate(21.20, -158.00),
            new HonuaMapCoordinate(21.50, -157.70),
        ], id: "crossing");

        var matches = layer.GetAnnotationsInBounds(new HonuaAnnotationBounds(-157.90, 21.25, -157.80, 21.35));

        Assert.Equal(["crossing", "inside"], matches.Select(annotation => annotation.Id).Order().ToArray());
        Assert.Contains(inside, matches);
        Assert.Contains(crossing, matches);
    }

    [Fact]
    public void DrawPolyline_CopiesMutableCoordinateCollections()
    {
        var coordinates = new List<HonuaMapCoordinate>
        {
            new(21.20, -158.00),
            new(21.50, -157.70),
        };
        var layer = new HonuaAnnotationLayer();

        var annotation = layer.DrawPolyline(coordinates, id: "route");
        coordinates.Clear();

        Assert.Equal(2, annotation.Coordinates.Count);
        Assert.Equal(2, layer.Annotations.Single().Coordinates.Count);
        var matches = layer.GetAnnotationsInBounds(new HonuaAnnotationBounds(-158.10, 21.10, -157.60, 21.60));
        Assert.Single(matches);
    }

    [Fact]
    public void GetAnnotationsByType_FiltersByAnnotationType()
    {
        var layer = new HonuaAnnotationLayer();
        layer.DrawPoint(new HonuaMapCoordinate(21.30, -157.85), id: "point");
        layer.DrawText(new HonuaMapCoordinate(21.30, -157.85), "label", id: "label");
        layer.DrawPolygon(
        [
            new HonuaMapCoordinate(21.30, -157.85),
            new HonuaMapCoordinate(21.31, -157.85),
            new HonuaMapCoordinate(21.31, -157.84),
        ], id: "polygon");

        var textAnnotations = layer.GetAnnotationsByType(HonuaAnnotationType.Text);

        Assert.Single(textAnnotations);
        Assert.Equal("label", textAnnotations[0].Id);
    }

    [Fact]
    public void SetOpacity_RejectsOutOfRangeValues()
    {
        var layer = new HonuaAnnotationLayer();

        Assert.Throws<ArgumentOutOfRangeException>(() => layer.SetOpacity(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layer.SetOpacity(1.1));
    }

    [Fact]
    public void AddHonuaMapAnnotations_RegistersTransientAnnotationLayer()
    {
        var services = new ServiceCollection()
            .AddHonuaMapAnnotations()
            .BuildServiceProvider();

        var first = services.GetRequiredService<HonuaAnnotationLayer>();
        var second = services.GetRequiredService<HonuaAnnotationLayer>();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void AddHonuaScenePackageDownload_RegistersDownloader()
    {
        var storePath = Path.Combine(Path.GetTempPath(), $"honua-scene-package-di-{Guid.NewGuid():N}.gpkg");
        using var services = new ServiceCollection()
            .AddHonuaGeoPackageOfflineSync(new GeoPackageSyncStoreOptions { DatabasePath = storePath })
            .AddHonuaScenePackageDownload()
            .BuildServiceProvider();

        var downloader = services.GetRequiredService<IHonuaScenePackageDownloader>();

        Assert.IsType<ScenePackageDownloader>(downloader);
    }
}
