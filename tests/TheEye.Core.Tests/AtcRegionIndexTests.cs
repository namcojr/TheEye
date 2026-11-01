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

    [Theory]
    [InlineData(185, 0)]
    [InlineData(-175, 0)]
    [InlineData(185, -360)]
    public void FijiDateLineSector_KeepsBothSidesAndDoesNotCoverGreenwich(double easternLongitude, double longitudeOffset)
    {
        var station = CreateAtc("NFFN_CTR", "CTR",
            (0, 175), (0, easternLongitude), (-8, easternLongitude), (-8, -166),
            (-28, easternLongitude), (-28, 176), (-31, 172), (-33, 167),
            (-21, 167), (-16, 166), (-16, 168), (-12, 175));
        station.AtcPosition!.RegionMapPolygon = station.AtcPosition.RegionMapPolygon
            .Select(point => point with { Longitude = point.Longitude + longitudeOffset }).ToList();
        var points = IvaoMapLayers.GetGeoPoints(station);
        var geometry = Assert.IsType<NetTopologySuite.Geometries.MultiPolygon>(IvaoMapLayers.CreatePolygon(station));
        var index = AtcRegionIndex.Build([station]);

        Assert.Equal(12, points.Count);
        Assert.Equal(2, geometry.NumGeometries);
        Assert.All(Enumerable.Range(0, geometry.NumGeometries), i =>
        {
            var polygon = geometry.GetGeometryN(i);
            Assert.True(polygon.IsValid);
            Assert.True(polygon.EnvelopeInternal.Width < WorldMap.Project(0, 40).X);
            Assert.Equal(13, polygon.Coordinates.Length);
        });
        Assert.Same(station, index.FindAt(new GeoCoordinate(-17, 178)));
        Assert.Same(station, index.FindAt(new GeoCoordinate(-17, -178)));
        Assert.Null(index.FindAt(new GeoCoordinate(-17, 0)));
        Assert.InRange(station.Position!.Value.Longitude, -180, 180);
        var labels = IvaoMapLayers.CreateAtcLabels([station],
            new MapPalette("#000000", "#111111", "#222222", "#FFFFFF", "#FFFFFF", true))
            .Features.Cast<Mapsui.Layers.PointFeature>().ToArray();
        Assert.Equal(2, labels.Length);
        Assert.All(labels, label => Assert.True(geometry.Contains(
            new NetTopologySuite.Geometries.Point(label.Point.X, label.Point.Y))));
        Assert.Equal(2 * WorldMap.Project(0, 180).X, Math.Abs(labels[0].Point.X - labels[1].Point.X), 6);
    }

    [Fact]
    public void FindAt_DoesNotCountDateLineCopiesTwiceWhenOrderingSectors()
    {
        var larger = CreateAtc("LARGER_CTR", "CTR", (0, 178), (0, 180), (2.5, 180), (2.5, 178));
        var smaller = CreateAtc("SMALLER_TWR", "TWR", (0, 179), (0, -178), (1, -178), (1, 179));
        var index = AtcRegionIndex.Build([larger, smaller]);

        Assert.Same(smaller, index.FindAt(new GeoCoordinate(0.5, 179.5)));
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
