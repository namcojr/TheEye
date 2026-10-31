using Mapsui;
using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class AtcRegionIndexTests
{
    [Fact]
    public void FindAt_ReturnsStationContainingCoordinate()
    {
        var index = AtcRegionIndex.Build([CreateAtc("EHAM_TWR", "TWR", (52, 4), (52, 6), (53, 6), (53, 4))]);

        var match = index.FindAt(new GeoCoordinate(52.5, 5));

        Assert.NotNull(match);
        Assert.Equal("EHAM_TWR", match.Callsign);
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void FindAt_ReturnsNullOutsideEveryPolygon()
    {
        var index = AtcRegionIndex.Build([CreateAtc("EHAM_TWR", "TWR", (52, 4), (52, 6), (53, 6), (53, 4))]);

        Assert.Null(index.FindAt(new GeoCoordinate(40, 40)));
        Assert.Null(index.FindAt(new GeoCoordinate(double.NaN, 5)));
    }

    [Fact]
    public void FindAt_PrefersSmallerSectorWhenPolygonsOverlap()
    {
        var center = CreateAtc("EHAA_CTR", "CTR", (49, 1), (49, 9), (55, 9), (55, 1));
        var tower = CreateAtc("EHAM_TWR", "TWR", (52, 4), (52, 6), (53, 6), (53, 4));
        var index = AtcRegionIndex.Build([center, tower]);

        var match = index.FindAt(new GeoCoordinate(52.5, 5));

        Assert.NotNull(match);
        Assert.Equal("EHAM_TWR", match.Callsign);
        Assert.Equal("EHAA_CTR", index.FindAt(new GeoCoordinate(50, 2))?.Callsign);
    }

    [Fact]
    public void Build_SkipsStationsWithoutUsablePolygon()
    {
        var index = AtcRegionIndex.Build(
        [
            CreateAtc("NO_POLYGON", "TWR"),
            CreateAtc("TWO_POINTS", "TWR", (52, 4), (52, 6)),
            CreateAtc("VALID", "TWR", (52, 4), (52, 6), (53, 6), (53, 4))
        ]);

        Assert.Equal(1, index.Count);
        Assert.Equal("VALID", index.FindAt(new GeoCoordinate(52.5, 5))?.Callsign);
    }

    [Fact]
    public void FindAt_AcceptsProjectedPoint()
    {
        var index = AtcRegionIndex.Build([CreateAtc("EHAM_TWR", "TWR", (52, 4), (52, 6), (53, 6), (53, 4))]);
        var projected = WorldMap.Project(new GeoCoordinate(52.5, 5));

        Assert.Equal("EHAM_TWR", index.FindAt(new MPoint(projected.X, projected.Y))?.Callsign);
    }

    private static IvaoAtc CreateAtc(
        string callsign,
        string facility,
        params (double Latitude, double Longitude)[] polygon) =>
        new()
        {
            Callsign = callsign,
            AtcSession = new IvaoAtcSession { Position = facility, Frequency = 118.275 },
            AtcPosition = new IvaoAtcPosition
            {
                Position = facility,
                AtcCallsign = callsign,
                RegionMapPolygon = polygon
                    .Select(point => new IvaoCoordinate(point.Latitude, point.Longitude))
                    .ToList()
            }
        };
}
