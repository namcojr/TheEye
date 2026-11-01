using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class GeoPolygonTests
{
    private static readonly GeoCoordinate[] Square =
    [
        new(50, 4),
        new(50, 8),
        new(54, 8),
        new(54, 4)
    ];

    [Fact]
    public void DistinctConsecutive_RemovesRepeatedAndInvalidPoints()
    {
        var points = new[]
        {
            new GeoCoordinate(50, 4),
            new GeoCoordinate(50, 4),
            new GeoCoordinate(double.NaN, 8),
            new GeoCoordinate(54, 8),
            new GeoCoordinate(54, 8),
            new GeoCoordinate(54, 4)
        };

        var result = GeoPolygon.DistinctConsecutive(points);

        Assert.Equal(3, result.Count);
        Assert.Equal(new GeoCoordinate(50, 4), result[0]);
        Assert.Equal(new GeoCoordinate(54, 8), result[1]);
        Assert.Equal(new GeoCoordinate(54, 4), result[2]);
    }

    [Fact]
    public void Contains_HandlesInsideOutsideAndBoundaryPoints()
    {
        Assert.True(GeoPolygon.Contains(Square, new GeoCoordinate(52, 6)));
        Assert.False(GeoPolygon.Contains(Square, new GeoCoordinate(52, 9)));
        Assert.False(GeoPolygon.Contains(Square, new GeoCoordinate(60, 6)));

        // A point on an edge belongs to the polygon, which keeps click
        // detection forgiving around sector borders.
        Assert.True(GeoPolygon.Contains(Square, new GeoCoordinate(50, 6)));
    }

    [Fact]
    public void Contains_RejectsDegeneratePolygonsAndInvalidCoordinates()
    {
        Assert.False(GeoPolygon.Contains([], new GeoCoordinate(0, 0)));
        Assert.False(GeoPolygon.Contains([new GeoCoordinate(0, 0), new GeoCoordinate(1, 1)], new GeoCoordinate(0, 0)));
        Assert.False(GeoPolygon.Contains(Square, new GeoCoordinate(double.NaN, 6)));
    }

    [Fact]
    public void Centroid_ReturnsCenterOfSquare()
    {
        var centroid = GeoPolygon.Centroid(Square);

        Assert.NotNull(centroid);
        Assert.Equal(52, centroid.Value.Latitude, 6);
        Assert.Equal(6, centroid.Value.Longitude, 6);
    }

    [Fact]
    public void DateLinePolygon_CentroidAndHitTestingUseTheShortLongitudeSpan()
    {
        GeoCoordinate[] polygon = [new(-10, 170), new(-10, -170), new(-20, -170), new(-20, 170)];

        var centroid = GeoPolygon.Centroid(polygon)!.Value;

        Assert.Equal(-15, centroid.Latitude, 6);
        Assert.Equal(180, Math.Abs(centroid.Longitude), 6);
        Assert.True(GeoPolygon.Contains(polygon, new GeoCoordinate(-15, 179)));
        Assert.True(GeoPolygon.Contains(polygon, new GeoCoordinate(-15, -179)));
        Assert.False(GeoPolygon.Contains(polygon, new GeoCoordinate(-15, 0)));
        Assert.Equal(new GeoCoordinate(-15, -175), new IvaoCoordinate(-15, 185).ToGeoCoordinate());
    }

    [Fact]
    public void Centroid_FallsBackToAverageForDegeneratePolygon()
    {
        var centroid = GeoPolygon.Centroid(
        [
            new GeoCoordinate(1, 2),
            new GeoCoordinate(1, 4),
            new GeoCoordinate(1, 6)
        ]);

        Assert.NotNull(centroid);
        Assert.Equal(1, centroid.Value.Latitude, 6);
        Assert.Equal(4, centroid.Value.Longitude, 6);
        Assert.Null(GeoPolygon.Centroid([]));
    }

    [Fact]
    public void Bounds_IgnoresInvalidCoordinatesAndReportsSpan()
    {
        var bounds = GeoPolygon.Bounds(
        [
            new GeoCoordinate(48, 2),
            new GeoCoordinate(double.NaN, 99),
            new GeoCoordinate(52, 10)
        ]);

        Assert.True(bounds.IsValid);
        Assert.Equal(48, bounds.MinLatitude);
        Assert.Equal(2, bounds.MinLongitude);
        Assert.Equal(52, bounds.MaxLatitude);
        Assert.Equal(10, bounds.MaxLongitude);
        Assert.Equal(4, bounds.LatitudeSpan);
        Assert.Equal(8, bounds.LongitudeSpan);
        Assert.Equal(new GeoCoordinate(50, 6), bounds.Center);
    }

    [Fact]
    public void Bounds_ReturnsEmptyValidRangeWhenNothingIsUsable()
    {
        var bounds = GeoPolygon.Bounds([new GeoCoordinate(double.NaN, double.NaN)]);

        Assert.True(bounds.IsValid);
        Assert.Equal(0, bounds.LatitudeSpan);
        Assert.Equal(0, bounds.LongitudeSpan);
    }
}
