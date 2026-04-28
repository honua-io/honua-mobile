namespace Honua.Mobile.Maui.Annotations;

/// <summary>
/// Geographic coordinate in WGS84 latitude/longitude order.
/// </summary>
public readonly record struct HonuaMapCoordinate
{
    public HonuaMapCoordinate(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || double.IsInfinity(latitude) || latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90 degrees.");
        }

        if (double.IsNaN(longitude) || double.IsInfinity(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180 degrees.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }
}
