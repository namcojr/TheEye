using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class RouteBuilderTests
{
    private static readonly GeoCoordinate Heathrow = new(51.4706, -0.4619);
    private static readonly GeoCoordinate Kennedy = new(40.6413, -73.7781);

    [Fact]
    public void Build_StartsAtDepartureAndEndsAtArrival()
    {
        var route = RouteBuilder.Build(Heathrow, Kennedy, maxSegments: 64);

        Assert.InRange(route.Count, 17, 65);
        Assert.Equal(Heathrow.Latitude, route[0].Latitude, 6);
        Assert.Equal(Heathrow.Longitude, route[0].Longitude, 6);
        Assert.Equal(Kennedy.Latitude, route[^1].Latitude, 6);
        Assert.Equal(Kennedy.Longitude, route[^1].Longitude, 6);
        Assert.All(route, point => Assert.True(point.IsValid));
    }

    [Fact]
    public void Build_RespectsMaximumSegmentCount()
    {
        var route = RouteBuilder.Build(new GeoCoordinate(-70, -170), new GeoCoordinate(70, 170), maxSegments: 16);

        Assert.Equal(17, route.Count);
    }

    [Fact]
    public void Build_ReturnsTwoPointsForIdenticalCoordinates()
    {
        var route = RouteBuilder.Build(Heathrow, Heathrow);

        Assert.Equal([Heathrow, Heathrow], route);
    }

    [Fact]
    public void Build_ReturnsEmptyForInvalidInput()
    {
        Assert.Empty(RouteBuilder.Build(new GeoCoordinate(91, 0), Heathrow));
        Assert.Empty(RouteBuilder.Build(Heathrow, new GeoCoordinate(0, 181)));
        Assert.Empty(RouteBuilder.Build(Heathrow, Kennedy, maxSegments: 1));
    }

    [Fact]
    public void BuildLines_SplitsRouteAtTheAntimeridian()
    {
        var lines = RouteBuilder.BuildLines(new GeoCoordinate(0, 179), new GeoCoordinate(0, -179));

        Assert.Equal(2, lines.Count);
        Assert.True(lines[0].Count >= 2);
        Assert.True(lines[1].Count >= 2);
        Assert.Equal(180, lines[0][^1].Longitude, 6);
        Assert.Equal(-180, lines[1][0].Longitude, 6);
        Assert.Equal(-179, lines[1][^1].Longitude, 6);
    }

    [Fact]
    public void BuildLines_ReturnsSingleLineWhenNoAntimeridianCrossing()
    {
        var lines = RouteBuilder.BuildLines(Heathrow, Kennedy);

        Assert.Single(lines);
        Assert.Equal(Heathrow.Latitude, lines[0][0].Latitude, 6);
        Assert.Equal(Heathrow.Longitude, lines[0][0].Longitude, 6);
    }

    [Fact]
    public void BuildLines_ReturnsEmptyForInvalidInput()
    {
        Assert.Empty(RouteBuilder.BuildLines(new GeoCoordinate(0, 0), new GeoCoordinate(double.NaN, 1)));
    }

    [Fact]
    public void DistanceNm_MatchesKnownGreatCircleDistances()
    {
        Assert.Equal(60.04, RouteBuilder.DistanceNm(new GeoCoordinate(0, 0), new GeoCoordinate(1, 0)), 1);
        Assert.InRange(RouteBuilder.DistanceNm(Heathrow, Kennedy), 2900, 3100);
        Assert.Equal(0, RouteBuilder.DistanceNm(new GeoCoordinate(double.NaN, 0), Kennedy));
        Assert.Equal(0, RouteBuilder.DistanceNm(Heathrow, Heathrow), 6);
    }
}
