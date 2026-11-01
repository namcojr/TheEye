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
    [InlineData(100)]
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
        using var bitmap = Render(new AtcGlyphStyle("TWR", "TEST_TWR", "#FFFFFF"), ReferenceResolution / 100);
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

        foreach (var longitude in new[] { 178d, 180d, 182d })
        {
            var point = viewport.WorldToScreen(WorldMap.Project(-15, longitude));
            Assert.Equal(52, bitmap.GetPixel((int)(point.X * 4), (int)(point.Y * 4)).Alpha);
        }

        var outside = viewport.WorldToScreen(WorldMap.Project(-15, 174));
        Assert.Equal(0, bitmap.GetPixel((int)(outside.X * 4), (int)(outside.Y * 4)).Alpha);
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
