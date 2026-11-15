using CommunitySos.Domain.Common;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace CommunitySos.Domain.ValueObjects;

/// <summary>WGS-84 latitude/longitude, convertible to a SQL Server <c>geography</c> point (SRID 4326).</summary>
public readonly record struct GeoCoordinate
{
    public const int Srid = 4326;

    private static readonly GeometryFactory Factory =
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: Srid);

    public GeoCoordinate(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
        {
            throw new DomainException("location.invalid_latitude", "Latitude must be between -90 and 90.");
        }

        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
        {
            throw new DomainException("location.invalid_longitude", "Longitude must be between -180 and 180.");
        }

        // (0,0) is what many devices report when GPS has no fix.
        if (latitude == 0 && longitude == 0)
        {
            throw new DomainException("location.no_fix", "A valid GPS location is required.");
        }

        Latitude = Math.Round(latitude, 6);
        Longitude = Math.Round(longitude, 6);
    }

    public double Latitude { get; }

    public double Longitude { get; }

    /// <summary>NTS uses X = longitude, Y = latitude.</summary>
    public Point ToPoint() => Factory.CreatePoint(new Coordinate(Longitude, Latitude));

    public static GeoCoordinate FromPoint(Point point) => new(point.Y, point.X);

    /// <summary>Mean Earth radius (IUGG), metres.</summary>
    private const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>
    /// Great-circle (haversine) distance in metres. SQL Server's geography STDistance uses the WGS-84 ellipsoid;
    /// the two agree to within about 0.5 %, which is irrelevant for a 5 km alert radius. Used where SQL is not
    /// available (tests) and for display.
    /// </summary>
    public double DistanceMetersTo(GeoCoordinate other)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;

        var dLat = Radians(other.Latitude - Latitude);
        var dLon = Radians(other.Longitude - Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(Radians(Latitude)) * Math.Cos(Radians(other.Latitude)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }
}
