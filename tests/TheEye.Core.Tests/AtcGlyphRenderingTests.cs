using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using SkiaSharp;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Rendering;

namespace TheEye.Core.Tests;

public sealed class AtcGlyphRenderingTests
{
    private const double ReferenceResolution = WorldMap.MaxResolution / (1 << AtcMarkerStyles.ReferenceZoomLevel);

    [Theory]
    [InlineData(0.001)]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(2)]
    // 6 keeps the widened tower badge (45 px per zoom step) inside the rendered
    // frame; the clamped maximum is covered by AtcMarkerStylesTests, which can
    // assert the size without drawing a 1300 px circle into a 1200 px bitmap.
    [InlineData(6)]
    public void Circle_RenderedDiameterMatchesClampAndContourStaysOnePixel(double zoom)
    {
        var style = new AtcGlyphStyle("TWR", "TEST_TWR");
        using var bitmap = Render(style, ReferenceResolution / zoom);
        var row = bitmap.Height / 2;
        var solidPixels = Enumerable.Range(0, bitmap.Width)
            .Where(x => bitmap.GetPixel(x, row).Alpha > 128).ToArray();
        var diameter = (solidPixels[^1] - solidPixels[0] + 1) / 4d;
        var expected = style.MarkerAtResolution(ReferenceResolution / zoom).Scale * 30;

        Assert.InRange(diameter, expected - 0.5, expected + 0.5);
        Assert.InRange(solidPixels.Length / 8d, 0.75, 1.25);
    }

    [Theory]
    [InlineData("GND")]
    [InlineData("DEL")]
    [InlineData("TWR")]
    public void SameStyle_ChangesSizeAcrossFractionalZoomFrames(string facility)
    {
        var style = new AtcGlyphStyle(facility, $"TEST_{facility}");
        using var first = Render(style, ReferenceResolution);
        using var next = Render(style, ReferenceResolution / 1.25);
        var firstArea = PaintedPixels(first);
        var nextArea = PaintedPixels(next);

        Assert.InRange(nextArea / (double)firstArea, 1.45, 1.7);
    }

    [Fact]
    public void Ring_ContourStaysTwoPixelsAtLargeZoom()
    {
        // The ring is twice the badge radius, so a zoom of 6 still leaves the whole
        // contour inside the frame (zoom 100 drew it outside the bitmap).
        using var bitmap = Render(new AtcGlyphStyle("TWR", "TEST_TWR", "#FFFFFF"), ReferenceResolution / 6);
        var count = Enumerable.Range(0, bitmap.Width)
            .Count(x => bitmap.GetPixel(x, bitmap.Height / 2).Alpha > 128);

        Assert.InRange(count / 8d, 1.75, 2.25);
    }

    [Fact]
    public void DateLineSector_RendererDrawsBothSidesWithoutAFalseSeam()
    {
        var station = new IvaoAtc
        {
            Callsign = "NFFN_CTR",
            AtcSession = new IvaoAtcSession { Position = "CTR" },
            AtcPosition = new IvaoAtcPosition
            {
                RegionMapPolygon =
                [
                    new(-10, -185), new(-10, -175), new(-20, -175), new(-20, -185)
                ]
            }
        };
        var center = WorldMap.Project(-15, 180);
        var viewport = new Viewport
        {
            Width = 300, Height = 300, CenterX = center.X, CenterY = center.Y,
            Resolution = WorldMap.Project(0, 1).X / 20
        };
        var palette = new MapPalette("#000000", "#111111", "#222222", "#FFFFFF", "#FFFFFF", true);
        using var bitmap = RenderLayer(IvaoMapLayers.CreateAtcRegions([station], palette), viewport);

        // The sector is filled with the control alpha of a dark basemap, whatever
        // part of the date line the pixel belongs to.
        var fillAlpha = AtcMarkerStyles.ControlFillAlpha + AtcMarkerStyles.DarkSectorFillBoost;
        foreach (var longitude in new[] { 178d, 180d, 182d })
        {
            var point = viewport.WorldToScreen(WorldMap.Project(-15, longitude));
            Assert.Equal(fillAlpha, bitmap.GetPixel((int)(point.X * 4), (int)(point.Y * 4)).Alpha);
        }

        var outside = viewport.WorldToScreen(WorldMap.Project(-15, 174));
        Assert.Equal(0, bitmap.GetPixel((int)(outside.X * 4), (int)(outside.Y * 4)).Alpha);
    }

    [Theory]
    [InlineData(6000)]
    [InlineData(14000)]
    [InlineData(ReferenceResolution / 1.25)]
    public void AirportPositionLabels_RenderEveryStackedLineAtSectorLabelZooms(double resolution)
    {
        AtcGlyphRenderer.Register();
        var facilities = new[] { "DEL", "GND", "GND", "TWR", "TWR", "APP", "DEP", "OBS" };
        var stations = facilities.Select((facility, index) => new IvaoAtc
        {
            Callsign = $"TEST_{index}_{facility}",
            AtcSession = new IvaoAtcSession { Position = facility },
            AtcPosition = new IvaoAtcPosition
            {
                Airport = new IvaoAirport { Icao = "TEST", Latitude = 0, Longitude = 0 }
            }
        }).ToArray();
        var palette = new MapPalette("#000000", "#111111", "#222222", "#FFFFFF", "#FFFFFF", true);
        var zoom = AtcMarkerStyles.ZoomScale(resolution);
        var placements = IvaoMapLayers.CreateAtcStationPlacements(stations, zoom);
        var viewport = new Viewport { Width = 300, Height = 400, Resolution = resolution };
        using var bitmap = RenderLayer(IvaoMapLayers.CreateAtcStations(placements, palette), viewport);

        Assert.Equal(facilities.Length, placements.Count);
        foreach (var placement in placements)
        {
            var startRow = (int)((viewport.Height / 2 + placement.LabelOffsetY) * 4);
            var endRow = startRow + (int)((AtcMarkerStyles.LabelFontSize() + 6) * 4);
            Assert.True(Enumerable.Range(startRow, endRow - startRow).Any(row =>
                Enumerable.Range(100 * 4, 100 * 4).Any(column => bitmap.GetPixel(column, row).Alpha > 128)),
                $"Missing rendered label for {placement.Station.Callsign} at resolution {resolution}.");
        }
    }

    private static SKBitmap Render(AtcGlyphStyle style, double resolution)
    {
        AtcGlyphRenderer.Register();
        var feature = new PointFeature(new MPoint(0, 0));
        feature.Styles.Add(style);
        var viewport = new Viewport { Width = 300, Height = 300, Resolution = resolution };
        return RenderLayer(new MemoryLayer { Features = [feature], Style = null }, viewport);
    }

    private static SKBitmap RenderLayer(MemoryLayer layer, Viewport viewport)
    {
        using var map = new Map();
        map.Layers.Add(layer);
        using var stream = new MapRenderer().RenderToBitmapStream(viewport, map.Layers, map.RenderService,
            background: new Color(0, 0, 0, 0), pixelDensity: 4);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static int PaintedPixels(SKBitmap bitmap) =>
        bitmap.Pixels.Count(pixel => pixel.Alpha > 20);
}
