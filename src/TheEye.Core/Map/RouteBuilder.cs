using TheEye.Core.Models;

namespace TheEye.Core.Maps;

/// <summary>
/// Draws a great-circle route between departure and arrival. The API's filed
/// route string is not required for this overview: a geodesic is a useful,
/// predictable approximation for a traffic monitor.
/// </summary>
public static class RouteBuilder
{
    private const double EarthRadiusNm = 3440.065;

    public static IReadOnlyList<GeoCoordinate> Build(
        GeoCoordinate start,
        GeoCoordinate end,
        int maxSegments = 128)
    {
        if (!start.IsValid || !end.IsValid || maxSegments < 2)
        {
            return [];
        }

        if (start == end)
        {
            return [start, end];
        }

        var lat1 = DegreesToRadians(start.Latitude);
        var lon1 = DegreesToRadians(start.Longitude);
        var lat2 = DegreesToRadians(end.Latitude);
        var lon2 = DegreesToRadians(end.Longitude);

        var deltaLat = lat2 - lat1;
        var deltaLon = lon2 - lon1;
        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        var angularDistance = 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));

        if (angularDistance < 1e-9)
        {
            return [start, end];
        }

        var distanceNm = angularDistance * EarthRadiusNm;
        var segments = Math.Clamp((int)Math.Ceiling(distanceNm / 50), 16, maxSegments);
        var sinDistance = Math.Sin(angularDistance);

        var points = new List<GeoCoordinate>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var fraction = i / (double)segments;
            var aScale = Math.Sin((1 - fraction) * angularDistance) / sinDistance;
            var bScale = Math.Sin(fraction * angularDistance) / sinDistance;

            var x = aScale * Math.Cos(lat1) * Math.Cos(lon1) + bScale * Math.Cos(lat2) * Math.Cos(lon2);
            var y = aScale * Math.Cos(lat1) * Math.Sin(lon1) + bScale * Math.Cos(lat2) * Math.Sin(lon2);
            var z = aScale * Math.Sin(lat1) + bScale * Math.Sin(lat2);

            var latitude = RadiansToDegrees(Math.Atan2(z, Math.Sqrt(x * x + y * y)));
            var longitude = RadiansToDegrees(Math.Atan2(y, x));
            points.Add(new GeoCoordinate(latitude, NormalizeLongitude(longitude)));
        }

        return points;
    }

    /// <summary>
    /// Splits a route at the antimeridian so Mapsui never draws a line across
    /// the entire world when a flight crosses 180 degrees.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<GeoCoordinate>> BuildLines(
        GeoCoordinate start,
        GeoCoordinate end,
        int maxSegments = 128)
    {
        var points = Build(start, end, maxSegments);
        return points.Count < 2 ? [] : SplitAtAntimeridian(points);
    }

    /// <summary>
    /// Splits an already built point list wherever two consecutive points sit on
    /// opposite sides of the 180 degree meridian. Each half is closed against
    /// the meridian so the resulting lines stay on one side of the map.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<GeoCoordinate>> SplitAtAntimeridian(
        IReadOnlyList<GeoCoordinate> points)
    {
        if (points.Count < 2)
        {
            return [];
        }

        var lines = new List<IReadOnlyList<GeoCoordinate>>();
        var current = new List<GeoCoordinate> { points[0] };

        for (var i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1];
            var point = points[i];

            if (Math.Abs(point.Longitude - previous.Longitude) > 180)
            {
                var eastern = previous.Longitude > 0;
                current.Add(new GeoCoordinate(previous.Latitude, eastern ? 180 : -180));
                lines.Add(current);

                current =
                [
                    new GeoCoordinate(point.Latitude, eastern ? -180 : 180),
                    point
                ];
            }
            else
            {
                current.Add(point);
            }
        }

        if (current.Count >= 2)
        {
            lines.Add(current);
        }

        return lines;
    }

    /// <summary>
    /// Keeps the part of a route up to <paramref name="fraction"/> of its
    /// length, interpolating the point that ends the trimmed route. Used to show
    /// only the distance an aircraft has already covered.
    /// </summary>
    public static IReadOnlyList<GeoCoordinate> TrimToFraction(
        IReadOnlyList<GeoCoordinate> points,
        double fraction)
    {
        if (points.Count < 2 || double.IsNaN(fraction))
        {
            return points;
        }

        if (fraction >= 1)
        {
            return points;
        }

        if (fraction <= 0)
        {
            return [points[0]];
        }

        var exact = fraction * (points.Count - 1);
        var index = (int)Math.Floor(exact);
        if (index >= points.Count - 1)
        {
            return points;
        }

        var trimmed = new List<GeoCoordinate>(index + 2);
        for (var i = 0; i <= index; i++)
        {
            trimmed.Add(points[i]);
        }

        var remainder = exact - index;
        if (remainder > 0)
        {
            var start = points[index];
            var end = points[index + 1];

            // Longitudes may jump across the antimeridian; take the short way
            // round before interpolating and normalise the result again.
            var deltaLongitude = end.Longitude - start.Longitude;
            if (deltaLongitude > 180)
            {
                deltaLongitude -= 360;
            }
            else if (deltaLongitude < -180)
            {
                deltaLongitude += 360;
            }

            trimmed.Add(new GeoCoordinate(
                start.Latitude + ((end.Latitude - start.Latitude) * remainder),
                NormalizeLongitude(start.Longitude + (deltaLongitude * remainder))));
        }

        return trimmed;
    }

    public static double DistanceNm(GeoCoordinate start, GeoCoordinate end)
    {
        if (!start.IsValid || !end.IsValid)
        {
            return 0;
        }

        var lat1 = DegreesToRadians(start.Latitude);
        var lat2 = DegreesToRadians(end.Latitude);
        var deltaLat = lat2 - lat1;
        var deltaLon = DegreesToRadians(end.Longitude - start.Longitude);
        var a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);
        return 2 * Math.Asin(Math.Min(1, Math.Sqrt(a))) * EarthRadiusNm;
    }

    private static double NormalizeLongitude(double longitude)
    {
        while (longitude > 180)
        {
            longitude -= 360;
        }

        while (longitude < -180)
        {
            longitude += 360;
        }

        return longitude;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}
