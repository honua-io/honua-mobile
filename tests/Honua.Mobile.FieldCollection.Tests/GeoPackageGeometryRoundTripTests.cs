using Honua.Mobile.FieldCollection.Models;
using Honua.Mobile.FieldCollection.Services.Storage;

namespace Honua.Mobile.FieldCollection.Tests;

public sealed class GeoPackageGeometryRoundTripTests
{
    public GeoPackageGeometryRoundTripTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task StoreAndGetFeature_PolygonWithHole_PreservesInteriorRing()
    {
        var databasePath = CreateDatabasePath();
        using var cleanup = new TempFile(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.CreateLayerAsync(CreateLayer(GeometryType.Polygon));

        var polygon = new Polygon
        {
            Coordinates =
            [
                // Shell.
                [new Point(0, 0), new Point(0, 10), new Point(10, 10), new Point(10, 0), new Point(0, 0)],
                // Interior ring (hole) that the old converter silently discarded.
                [new Point(2, 2), new Point(2, 8), new Point(8, 8), new Point(8, 2), new Point(2, 2)],
            ],
        };

        await storage.StoreFeatureAsync(CreateFeature("poly-1", polygon));

        var stored = await storage.GetFeatureAsync("poly-1", 1);

        Assert.NotNull(stored);
        var storedPolygon = Assert.IsType<Polygon>(stored!.Geometry);
        Assert.Equal(2, storedPolygon.Coordinates.Count);
        Assert.Equal(5, storedPolygon.Coordinates[0].Count);
        Assert.Equal(5, storedPolygon.Coordinates[1].Count);
        Assert.Equal(2, storedPolygon.Coordinates[1][0].Latitude);
        Assert.Equal(2, storedPolygon.Coordinates[1][0].Longitude);
    }

    [Fact]
    public async Task StoreAndGetFeature_PointWithAltitude_PreservesZ()
    {
        var databasePath = CreateDatabasePath();
        using var cleanup = new TempFile(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.CreateLayerAsync(CreateLayer(GeometryType.Point));

        await storage.StoreFeatureAsync(CreateFeature("pt-1", new Point(21.3, -157.8, 1234.5)));

        var stored = await storage.GetFeatureAsync("pt-1", 1);

        Assert.NotNull(stored);
        var storedPoint = Assert.IsType<Point>(stored!.Geometry);
        Assert.Equal(21.3, storedPoint.Latitude, 6);
        Assert.Equal(-157.8, storedPoint.Longitude, 6);
        Assert.NotNull(storedPoint.Altitude);
        Assert.Equal(1234.5, storedPoint.Altitude!.Value, 6);
    }

    [Fact]
    public async Task StoreAndGetFeature_PointWithoutAltitude_RemainsTwoDimensional()
    {
        var databasePath = CreateDatabasePath();
        using var cleanup = new TempFile(databasePath);
        using var storage = new GeoPackageStorageService(databasePath);
        await storage.CreateLayerAsync(CreateLayer(GeometryType.Point));

        await storage.StoreFeatureAsync(CreateFeature("pt-2", new Point(21.3, -157.8)));

        var stored = await storage.GetFeatureAsync("pt-2", 1);

        Assert.NotNull(stored);
        var storedPoint = Assert.IsType<Point>(stored!.Geometry);
        Assert.Null(storedPoint.Altitude);
    }

    private static LayerInfo CreateLayer(GeometryType geometryType) => new()
    {
        Id = 1,
        ServiceId = "field-support",
        SourceId = "field-support/FeatureServer/1",
        Name = "Field Assets",
        GeometryType = geometryType,
        IsEditable = true,
    };

    private static Feature CreateFeature(string id, Geometry geometry) => new()
    {
        Id = id,
        LayerId = 1,
        Version = 1,
        Geometry = geometry,
        CreatedAt = DateTime.UtcNow,
        ModifiedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Attributes = new Dictionary<string, object?> { ["name"] = id },
    };

    private static string CreateDatabasePath()
        => Path.Combine(Path.GetTempPath(), $"honua-field-geom-{Guid.NewGuid():N}.gpkg");

    private sealed class TempFile : IDisposable
    {
        private readonly string _path;

        public TempFile(string path) => _path = path;

        public void Dispose()
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var candidate = _path + suffix;
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }
}
