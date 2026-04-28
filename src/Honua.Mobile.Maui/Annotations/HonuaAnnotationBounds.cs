namespace Honua.Mobile.Maui.Annotations;

/// <summary>
/// Axis-aligned geographic bounding box for annotation queries.
/// </summary>
public readonly record struct HonuaAnnotationBounds
{
    public HonuaAnnotationBounds(
        double minLongitude,
        double minLatitude,
        double maxLongitude,
        double maxLatitude)
    {
        if (minLongitude > maxLongitude)
        {
            throw new ArgumentException("Minimum longitude must be less than or equal to maximum longitude.");
        }

        if (minLatitude > maxLatitude)
        {
            throw new ArgumentException("Minimum latitude must be less than or equal to maximum latitude.");
        }

        _ = new HonuaMapCoordinate(minLatitude, minLongitude);
        _ = new HonuaMapCoordinate(maxLatitude, maxLongitude);

        MinLongitude = minLongitude;
        MinLatitude = minLatitude;
        MaxLongitude = maxLongitude;
        MaxLatitude = maxLatitude;
    }

    public double MinLongitude { get; }

    public double MinLatitude { get; }

    public double MaxLongitude { get; }

    public double MaxLatitude { get; }

    public bool Contains(HonuaMapCoordinate coordinate) =>
        coordinate.Longitude >= MinLongitude
        && coordinate.Longitude <= MaxLongitude
        && coordinate.Latitude >= MinLatitude
        && coordinate.Latitude <= MaxLatitude;

    public bool Intersects(HonuaAnnotationBounds other) =>
        MinLongitude <= other.MaxLongitude
        && MaxLongitude >= other.MinLongitude
        && MinLatitude <= other.MaxLatitude
        && MaxLatitude >= other.MinLatitude;

    public static HonuaAnnotationBounds FromCoordinates(IEnumerable<HonuaMapCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var materialized = coordinates as IReadOnlyCollection<HonuaMapCoordinate> ?? coordinates.ToArray();
        if (materialized.Count == 0)
        {
            throw new ArgumentException("At least one coordinate is required.", nameof(coordinates));
        }

        return new HonuaAnnotationBounds(
            materialized.Min(coordinate => coordinate.Longitude),
            materialized.Min(coordinate => coordinate.Latitude),
            materialized.Max(coordinate => coordinate.Longitude),
            materialized.Max(coordinate => coordinate.Latitude));
    }
}
