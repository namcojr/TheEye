using Mapsui.Layers;
using Mapsui.Styles;
using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class AtcMarkerStylesTests
{
    private static readonly MapPalette Palette = new(
        Water: "#16324F",
        Land: "#1B2A35",
        Border: "#2F4858",
        Label: "#6FE3F5",
        Accent: "#4CC9F0",
        IsDark: true);

    [Fact]
    public void For_GroundPosition_DrawsStar()
    {
        var marker = AtcMarkerStyles.For("GND", "EDDB_GND");

        Assert.Equal(AtcMarkerShape.Star, marker.Shape);
        Assert.Equal(AtcMarkerStyles.GroundFill, marker.Fill);
        Assert.Contains("<path", marker.SvgSource, StringComparison.Ordinal);
    }

    [Fact]
    public void For_TowerPosition_DrawsRedDisc()
    {
        var marker = AtcMarkerStyles.For("TWR", "EDDB_TWR");

        Assert.Equal(AtcMarkerShape.Circle, marker.Shape);
        Assert.Equal(AtcMarkerStyles.TowerFill, marker.Fill);
        Assert.Contains("<circle", marker.SvgSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("APP")]
    [InlineData("DEP")]
    public void For_ApproachAndDeparturePositions_DrawLargerBlueDisc(string facility)
    {
        var marker = AtcMarkerStyles.For(facility, $"EDDB_{facility}");
        var tower = AtcMarkerStyles.For("TWR", "EDDB_TWR");

        Assert.Equal(AtcMarkerShape.Circle, marker.Shape);
        Assert.Equal(AtcMarkerStyles.ApproachFill, marker.Fill);
        Assert.True(marker.Scale > tower.Scale);
    }

    [Fact]
    public void For_MissingFacility_FallsBackToCallsignSuffix()
    {
        var marker = AtcMarkerStyles.For("ATC", "EDDB_TW1_GND");

        Assert.Equal(AtcMarkerShape.Star, marker.Shape);
        Assert.Equal(AtcMarkerStyles.GroundFill, marker.Fill);
    }

    [Fact]
    public void For_UnknownFacility_DrawsDefaultDisc()
    {
        var marker = AtcMarkerStyles.For(null, "BIRD");

        Assert.Equal(AtcMarkerShape.Circle, marker.Shape);
        Assert.Equal(AtcMarkerStyles.DefaultFill, marker.Fill);
    }

    [Fact]
    public void CreateImageStyle_UsesInlineSvgAndReusesImage()
    {
        var marker = AtcMarkerStyles.For("APP", "EDDB_APP");

        var style = AtcMarkerStyles.CreateImageStyle(marker);
        var second = AtcMarkerStyles.CreateImageStyle(marker);

        Assert.NotNull(style.Image);
        Assert.Equal($"svg-content://{marker.SvgSource}", style.Image!.Source?.ToString());
        Assert.Equal(marker.Scale, style.SymbolScale);
        Assert.False(style.RotateWithMap);
        Assert.Same(style.Image, second.Image);
    }

    [Fact]
    public void CreateAtcStations_MarkerOnlyStationsGetMarkerAndLabel()
    {
        var layer = IvaoMapLayers.CreateAtcStations([CreateAirportAtc("EDDB_GND", "GND")], Palette);

        var feature = Assert.Single(layer.Features);
        var image = Assert.Single(feature.Styles.OfType<ImageStyle>());
        var label = Assert.Single(feature.Styles.OfType<LabelStyle>());

        Assert.Contains("<path", image.Image!.Source?.ToString(), StringComparison.Ordinal);
        Assert.Equal("Callsign", label.LabelColumn);
        Assert.True(label.Offset.Y > 0);
    }

    [Fact]
    public void CreateAtcStations_SkipsStationsWithSectorPolygon()
    {
        var layer = IvaoMapLayers.CreateAtcStations(
            [CreatePolygonAtc("EDDB_TWR", "TWR", (52.3, 13.3), (52.3, 13.7), (52.6, 13.7), (52.6, 13.3))],
            Palette);

        Assert.Empty(layer.Features);
    }

    [Fact]
    public void CreateAtcStations_DrawsFacilityGlyphForShapeLessStations()
    {
        var layer = IvaoMapLayers.CreateAtcStations(
            [CreateAirportAtc("EDDB_TWR", "TWR"), CreateAirportAtc("EDDB_APP", "APP")],
            Palette);

        var sources = layer.Features
            .Select(feature => feature.Styles.OfType<ImageStyle>().Single().Image!.Source?.ToString())
            .ToList();

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, source => source!.Contains(AtcMarkerStyles.TowerFill, StringComparison.Ordinal));
        Assert.Contains(sources, source => source!.Contains(AtcMarkerStyles.ApproachFill, StringComparison.Ordinal));
    }

    [Fact]
    public void CreateAtcStations_SkipsStationsWithoutPosition()
    {
        var layer = IvaoMapLayers.CreateAtcStations(
            [new IvaoAtc { Callsign = "NOWHERE_CTR", AtcSession = new IvaoAtcSession { Position = "CTR" } }],
            Palette);

        Assert.Empty(layer.Features);
    }

    [Fact]
    public void CreateImageStyle_ConvertsScreenOffsetToMapsuiOffset()
    {
        var marker = AtcMarkerStyles.For("TWR", "EDDB_TWR");

        var style = AtcMarkerStyles.CreateImageStyle(marker, (12, -8));

        // Mapsui applies the offset before the symbol scale and its Y axis points
        // up, so a screen offset (Y down) is divided by the scale and negated.
        Assert.Equal(12 / marker.Scale, style.Offset.X, 6);
        Assert.Equal(8 / marker.Scale, style.Offset.Y, 6);
    }

    [Fact]
    public void ImageFor_PublishesTheSourceToTheMapsuiFetcher()
    {
        var marker = AtcMarkerStyles.For("INFO", "EDDB_INFO");

        var image = AtcMarkerStyles.ImageFor(marker);

        // RenderService.ImageSourceCache fetches every entry of this dictionary,
        // which is how the inline SVG reaches the renderer.
        Assert.True(Image.SourceToSourceId.ContainsKey(image.Source));
    }

    [Fact]
    public void ClusterOffsets_SingleStationKeepsTheAirportCentre()
    {
        var offsets = AtcMarkerStyles.ClusterOffsets(1);

        Assert.Equal(new[] { (0d, 0d) }, offsets);
    }

    [Fact]
    public void ClusterOffsets_SpreadBadgesAroundTheAirport()
    {
        var offsets = AtcMarkerStyles.ClusterOffsets(4);

        Assert.Equal(4, offsets.Count);
        Assert.Equal(0, offsets[0].X, 6);
        Assert.True(offsets[0].Y < 0);
        Assert.True(offsets[1].X > 0);
        Assert.Equal(-offsets[0].Y, offsets[2].Y, 6);
        Assert.True(offsets[3].X < 0);
        Assert.All(
            offsets,
            offset => Assert.True(Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y)) > 10));
    }

    [Fact]
    public void LabelOffset_PushesTheCallsignPastTheBadgeAlongTheClusterDirection()
    {
        var marker = AtcMarkerStyles.For("APP", "EDDB_APP");

        var (x, y) = AtcMarkerStyles.LabelOffset(marker, (0, -16));

        Assert.Equal(0, x, 6);
        // Label offsets are rounded to one decimal to keep the styles stable.
        Assert.Equal(Math.Round(-16d - ((16 * marker.Scale) + 4), 1), y, 6);
    }

    [Fact]
    public void LabelOffset_SingleStationSitsBelowTheBadge()
    {
        var marker = AtcMarkerStyles.For("TWR", "EDDB_TWR");

        var (x, y) = AtcMarkerStyles.LabelOffset(marker, default);

        Assert.Equal(0, x, 6);
        Assert.Equal((16 * marker.Scale) + 4, y, 6);
    }

    [Theory]
    [InlineData(12d, 0d, LabelStyle.HorizontalAlignmentEnum.Left, LabelStyle.VerticalAlignmentEnum.Center)]
    [InlineData(-12d, 0d, LabelStyle.HorizontalAlignmentEnum.Right, LabelStyle.VerticalAlignmentEnum.Center)]
    [InlineData(0d, 12d, LabelStyle.HorizontalAlignmentEnum.Center, LabelStyle.VerticalAlignmentEnum.Top)]
    [InlineData(0d, -12d, LabelStyle.HorizontalAlignmentEnum.Center, LabelStyle.VerticalAlignmentEnum.Bottom)]
    public void LabelAlignment_GrowsAwayFromTheBadge(
        double x,
        double y,
        LabelStyle.HorizontalAlignmentEnum horizontal,
        LabelStyle.VerticalAlignmentEnum vertical)
    {
        var alignment = AtcMarkerStyles.LabelAlignment((x, y));

        Assert.Equal(horizontal, alignment.Horizontal);
        Assert.Equal(vertical, alignment.Vertical);
    }

    [Fact]
    public void SortOrder_KeepsTheFacilityOrderInsideACluster()
    {
        var facilities = new[] { ("CTR", "EDDB_CTR"), ("APP", "EDDB_APP"), ("GND", "EDDB_GND"), ("TWR", "EDDB_TWR") };

        var ordered = facilities
            .OrderBy(item => AtcMarkerStyles.SortOrder(item.Item1, item.Item2))
            .Select(item => item.Item1)
            .ToList();

        Assert.Equal(new[] { "GND", "TWR", "APP", "CTR" }, ordered);
    }

    [Fact]
    public void CreateAtcStations_FansOutPositionsThatShareAnAirport()
    {
        var layer = IvaoMapLayers.CreateAtcStations(
            [
                CreateAirportAtc("EDDB_GND", "GND"),
                CreateAirportAtc("EDDB_TWR", "TWR"),
                CreateAirportAtc("EDDB_APP", "APP")
            ],
            Palette);

        Assert.Equal(3, layer.Features.Count());
        Assert.Equal(
            3,
            layer.Features
                .Select(feature => feature.Styles.OfType<ImageStyle>().Single().Offset)
                .Select(offset => (offset.X, offset.Y))
                .Distinct()
                .Count());
        Assert.Equal(
            3,
            layer.Features
                .Select(feature => feature.Styles.OfType<LabelStyle>().Single().Offset)
                .Select(offset => (offset.X, offset.Y))
                .Distinct()
                .Count());
        Assert.Equal(
            3,
            layer.Features
                .Select(feature => feature.Styles.OfType<LabelStyle>().Single())
                .Select(style => (style.HorizontalAlignment, style.VerticalAlignment))
                .Distinct()
                .Count());
        Assert.Single(layer.Features.Cast<PointFeature>().Select(feature => feature.Point).Distinct());
    }

    [Fact]
    public void For_ZoomedMarker_KeepsTheGlyphAndGrowsIt()
    {
        var marker = AtcMarkerStyles.For("TWR", "EDDB_TWR");
        var zoomed = AtcMarkerStyles.For("TWR", "EDDB_TWR", 2);

        Assert.Equal(marker.Shape, zoomed.Shape);
        Assert.Equal(marker.Fill, zoomed.Fill);
        Assert.Equal(Math.Round(marker.Scale * 2, 3), zoomed.Scale, 6);
    }

    [Theory]
    [InlineData(WorldMap.MaxResolution, AtcMarkerStyles.MinZoomScale)]
    [InlineData(AtcMarkerStyles.ReferenceResolution, 1d)]
    [InlineData(WorldMap.MinResolution, AtcMarkerStyles.MaxZoomScale)]
    public void ZoomScale_IsClampedToTheSupportedRange(double resolution, double expected)
    {
        Assert.Equal(expected, AtcMarkerStyles.ZoomScale(resolution), 6);
    }

    [Fact]
    public void ZoomScale_GrowsWhenTheViewportZoomsIn()
    {
        var resolutions = new[] { 156543.034, 40000, 25000, 6000, 1500, 375, 94, WorldMap.MinResolution };

        var scales = resolutions.Select(AtcMarkerStyles.ZoomScale).ToList();

        Assert.Equal(scales.OrderBy(scale => scale), scales);
        Assert.True(scales[0] < scales[^1]);
    }

    [Fact]
    public void ZoomScale_FallsBackToTheBaseSizeForAnEmptyViewport()
    {
        Assert.Equal(1, AtcMarkerStyles.ZoomScale(0));
        Assert.Equal(1, AtcMarkerStyles.ZoomScale(double.NaN));
    }

    [Fact]
    public void LabelFontSize_GrowsWithTheZoomButStaysReadable()
    {
        Assert.Equal(9, AtcMarkerStyles.LabelFontSize(1));
        Assert.True(AtcMarkerStyles.LabelFontSize(2) > 9);
        Assert.Equal(14.4, AtcMarkerStyles.LabelFontSize(AtcMarkerStyles.MaxZoomScale), 6);
    }

    [Fact]
    public void ClusterOffsets_WidenWithTheZoom()
    {
        var baseOffsets = AtcMarkerStyles.ClusterOffsets(4);
        var zoomedOffsets = AtcMarkerStyles.ClusterOffsets(4, 2);

        Assert.Equal(baseOffsets[1].X * 2, zoomedOffsets[1].X, 6);
        Assert.Equal(baseOffsets[1].Y * 2, zoomedOffsets[1].Y, 6);
    }

    [Fact]
    public void CreateAtcStations_ScalesTheGlyphsAndLabelsWithTheZoom()
    {
        var layer = IvaoMapLayers.CreateAtcStations(
            [CreateAirportAtc("EDDB_TWR", "TWR")],
            Palette,
            zoom: 2);

        var image = Assert.Single(layer.Features.SelectMany(feature => feature.Styles.OfType<ImageStyle>()));
        var label = Assert.Single(layer.Features.SelectMany(feature => feature.Styles.OfType<LabelStyle>()));

        Assert.Equal(AtcMarkerStyles.For("TWR", "EDDB_TWR", 2).Scale, image.SymbolScale, 6);
        Assert.True(label.Font.Size > 9);
    }

    [Fact]
    public void HitRadius_GrowsWithTheZoomButStaysClickable()
    {
        var tower = AtcMarkerStyles.For("TWR", "EDDB_TWR");
        var zoomed = AtcMarkerStyles.For("TWR", "EDDB_TWR", AtcMarkerStyles.MaxZoomScale);
        var ground = AtcMarkerStyles.For("GND", "EDDB_GND", AtcMarkerStyles.MinZoomScale);

        Assert.Equal((16 * tower.Scale) + 4, AtcMarkerStyles.HitRadius(tower), 6);
        Assert.True(AtcMarkerStyles.HitRadius(zoomed) > AtcMarkerStyles.HitRadius(tower));
        Assert.True(AtcMarkerStyles.HitRadius(ground) >= 10);
    }

    [Fact]
    public void CreateAtcStationPlacements_MatchesTheDrawnBadges()
    {
        var stations = new[]
        {
            CreateAirportAtc("EDDB_GND", "GND"),
            CreateAirportAtc("EDDB_TWR", "TWR"),
            CreateAirportAtc("EDDB_APP", "APP")
        };

        var placements = IvaoMapLayers.CreateAtcStationPlacements(stations, zoom: 2);

        Assert.Equal(3, placements.Count);
        Assert.Equal(3, placements.Select(placement => (placement.OffsetX, placement.OffsetY)).Distinct().Count());
        Assert.All(
            placements,
            placement => Assert.Equal(
                AtcMarkerStyles.For(placement.Station.Facility, placement.Station.Callsign, 2).Scale,
                placement.Marker.Scale,
                6));
        Assert.Single(placements.Select(placement => placement.Point).Distinct());

        var layer = IvaoMapLayers.CreateAtcStations(placements, Palette, zoom: 2);

        Assert.Equal(placements.Count, layer.Features.Count());
    }

    [Fact]
    public void CreateAtcStationPlacements_LeavesSectorStationsToTheRegionLayer()
    {
        // A center that also publishes an airport position keeps its polygon as
        // the click target, so it must not get a badge on top of the airport.
        var center = CreateAirportAtc("EDWW_CTR", "CTR");
        center.AtcPosition!.RegionMapPolygon =
        [
            new IvaoCoordinate(52, 13),
            new IvaoCoordinate(52, 14),
            new IvaoCoordinate(53, 14),
            new IvaoCoordinate(53, 13)
        ];

        var placements = IvaoMapLayers.CreateAtcStationPlacements([center, CreateAirportAtc("EDDB_TWR", "TWR")]);

        var placement = Assert.Single(placements);
        Assert.Equal("EDDB_TWR", placement.Station.Callsign);
        Assert.Equal(0, placement.OffsetX, 6);
        Assert.Equal(0, placement.OffsetY, 6);
    }

    private static IvaoAtc CreateAirportAtc(string callsign, string facility) =>
        new()
        {
            Callsign = callsign,
            AtcSession = new IvaoAtcSession { Position = facility, Frequency = 121.9 },
            AtcPosition = new IvaoAtcPosition
            {
                Position = facility,
                AtcCallsign = callsign,
                Airport = new IvaoAirport { Icao = "EDDB", Latitude = 52.36, Longitude = 13.5 }
            }
        };

    private static IvaoAtc CreatePolygonAtc(
        string callsign,
        string facility,
        params (double Latitude, double Longitude)[] polygon) =>
        new()
        {
            Callsign = callsign,
            AtcSession = new IvaoAtcSession { Position = facility, Frequency = 118.7 },
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
