namespace TheEye.Core.Models;

public readonly record struct GeoBounds(double MinLatitude, double MinLongitude, double MaxLatitude, double MaxLongitude)
{
    public bool IsValid =>
        !double.IsNaN(MinLatitude) &&
        !double.IsNaN(MinLongitude) &&
        !double.IsNaN(MaxLatitude) &&
        !double.IsNaN(MaxLongitude) &&
        MinLatitude <= MaxLatitude &&
        MinLongitude <= MaxLongitude;

    public GeoCoordinate Center => new((MinLatitude + MaxLatitude) / 2, (MinLongitude + MaxLongitude) / 2);

    public double LatitudeSpan => MaxLatitude - MinLatitude;

    public double LongitudeSpan => MaxLongitude - MinLongitude;

    public override string ToString() =>
        FormattableString.Invariant($"{MinLatitude:0.####},{MinLongitude:0.####} → {MaxLatitude:0.####},{MaxLongitude:0.####}");
}

/// <summary>
/// Small geometry helpers used for hit-testing and labelling IVAO sectors without
/// pulling an entire GIS stack into the UI layer.
/// </summary>
public static class GeoPolygon
{
    /// <summary>Accepts polygon longitudes in either [-180, 180] or [0, 360] notation.</summary>
    public static double NormalizeLongitude(double longitude) =>
        longitude is >= -180 and <= 180 ? longitude : ((longitude + 180) % 360 + 360) % 360 - 180;

    /// <summary>Keeps adjacent vertices on the same side of the date line for geometry calculations.</summary>
    public static IReadOnlyList<GeoCoordinate> Unwrap(IReadOnlyList<GeoCoordinate> points)
    {
        var result = new List<GeoCoordinate>(points.Count);
        foreach (var point in points)
        {
            var longitude = point.Longitude;
            if (result.Count > 0)
            {
                longitude += 360 * Math.Round((result[^1].Longitude - longitude) / 360);
            }

            result.Add(new GeoCoordinate(point.Latitude, longitude));
        }

        return result;
    }

    public static IReadOnlyList<GeoCoordinate> DistinctConsecutive(IEnumerable<GeoCoordinate> points)
    {
        var result = new List<GeoCoordinate>();
        foreach (var point in points)
        {
            if (!point.IsValid)
            {
                continue;
            }

            if (result.Count == 0 || result[^1] != point)
            {
                result.Add(point);
            }
        }

        return result;
    }

    public static bool Contains(IReadOnlyList<GeoCoordinate> polygon, GeoCoordinate point)
    {
        if (polygon.Count < 3 || !point.IsValid)
        {
            return false;
        }

        polygon = Unwrap(polygon);
        point = point with
        {
            Longitude = point.Longitude + 360 * Math.Round((polygon[0].Longitude - point.Longitude) / 360)
        };
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];

            if (IsOnSegment(a, b, point))
            {
                return true;
            }

            if ((a.Latitude > point.Latitude) != (b.Latitude > point.Latitude) &&
                point.Longitude < (b.Longitude - a.Longitude) * (point.Latitude - a.Latitude) /
                (b.Latitude - a.Latitude) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static GeoCoordinate? Centroid(IReadOnlyList<GeoCoordinate> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        points = Unwrap(points);
        double twiceArea = 0;
        double x = 0;
        double y = 0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var current = points[i];
            var previous = points[j];
            var cross = previous.Longitude * current.Latitude - current.Longitude * previous.Latitude;
            twiceArea += cross;
            x += (previous.Longitude + current.Longitude) * cross;
            y += (previous.Latitude + current.Latitude) * cross;
        }

        if (Math.Abs(twiceArea) < 1e-12)
        {
            return new GeoCoordinate(points.Average(p => p.Latitude), NormalizeLongitude(points.Average(p => p.Longitude)));
        }

        return new GeoCoordinate(y / (3 * twiceArea), NormalizeLongitude(x / (3 * twiceArea)));
    }

    public static GeoBounds Bounds(IEnumerable<GeoCoordinate> points)
    {
        double minLat = double.MaxValue;
        double minLon = double.MaxValue;
        double maxLat = double.MinValue;
        double maxLon = double.MinValue;
        var any = false;

        foreach (var point in points)
        {
            if (!point.IsValid)
            {
                continue;
            }

            any = true;
            minLat = Math.Min(minLat, point.Latitude);
            minLon = Math.Min(minLon, point.Longitude);
            maxLat = Math.Max(maxLat, point.Latitude);
            maxLon = Math.Max(maxLon, point.Longitude);
        }

        return any
            ? new GeoBounds(minLat, minLon, maxLat, maxLon)
            : new GeoBounds(0, 0, 0, 0);
    }

    private static bool IsOnSegment(GeoCoordinate a, GeoCoordinate b, GeoCoordinate point)
    {
        const double epsilon = 1e-9;
        var cross = (point.Latitude - a.Latitude) * (b.Longitude - a.Longitude) -
                    (point.Longitude - a.Longitude) * (b.Latitude - a.Latitude);
        if (Math.Abs(cross) > epsilon)
        {
            return false;
        }

        return point.Longitude >= Math.Min(a.Longitude, b.Longitude) - epsilon &&
               point.Longitude <= Math.Max(a.Longitude, b.Longitude) + epsilon &&
               point.Latitude >= Math.Min(a.Latitude, b.Latitude) - epsilon &&
               point.Latitude <= Math.Max(a.Latitude, b.Latitude) + epsilon;
    }
}
