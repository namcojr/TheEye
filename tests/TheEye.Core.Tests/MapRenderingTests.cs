using System.Collections.Specialized;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Services;
using TheEye.Presentation.Collections;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;
using TheEye.Rendering;

namespace TheEye.Core.Tests;

/// <summary>
/// The aircraft are painted inside the map's render pass and the polygons from
/// cached paths. These tests pin down what that has to preserve: pins that sit
/// exactly where the frame's viewport puts them, a map that looks the same as
/// Mapsui's own drawing of it, and pointer behaviour without any controls.
/// </summary>
[Collection(RendererRegistration.Name)]
public sealed class MapRenderingTests
{
    private const int Width = 400;
    private const int Height = 300;

    private static readonly MapPalette Dark = new("#0E2233", "#1B303F", "#2C4557", "#AEC4D4", "#4FB3FF", true);

    [Fact]
    public void Pin_IsPlacedByTheViewportOfTheFrameBeingDrawn()
    {
        var marker = Marker(worldX: 0, worldY: 0);
        var layer = new AircraftLayer { Markers = [marker] };
        var renderer = new AircraftLayerRenderer();

        // The marker is not touched between the frames: the pin follows the map
        // because the frame works its position out, so the two cannot disagree.
        var first = Viewport(centerX: 0, centerY: 0, resolution: 1000);
        var panned = Viewport(centerX: 50_000, centerY: -20_000, resolution: 1000);

        Assert.Equal((Width / 2d, Height / 2d), CentreOfPin(Render(renderer, layer, first)), new PixelComparer(2));
        Assert.Equal((Width / 2d - 50, Height / 2d - 20), CentreOfPin(Render(renderer, layer, panned)), new PixelComparer(2));
    }

    [Fact]
    public void Pin_FollowsTheDisplayScaleAndTheCanvasOffset()
    {
        var layer = new AircraftLayer { Markers = [Marker(0, 0)] };
        using var bitmap = new SKBitmap(Width * 2, Height * 2);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Black);
            canvas.Translate(10, 6);
            canvas.Scale(2);
            new AircraftLayerRenderer().Render(canvas, Viewport(0, 0, 1000), layer, new RenderService());
        }

        var (x, y) = CentreOfPin(bitmap);
        Assert.InRange(x, Width + 10 - 4, Width + 10 + 4);
        Assert.InRange(y, Height + 6 - 4, Height + 6 + 4);
    }

    [Fact]
    public void Pin_MarkersThatLeaveTheListOrTheMapAreNotDrawn()
    {
        var hidden = Marker(0, 0);
        hidden.HasPosition = false;
        var layer = new AircraftLayer { Markers = [hidden, Marker(50_000_000, 0)] };

        using var bitmap = Render(new AircraftLayerRenderer(), layer, Viewport(0, 0, 1000));

        Assert.False(PinPixels(bitmap).Any());
    }

    [Fact]
    public void Pin_OnTheGroundIsDrawnFainter()
    {
        var renderer = new AircraftLayerRenderer();
        var airborne = Marker(0, 0);
        airborne.Callsign = string.Empty;
        var parked = Marker(0, 0);
        parked.Callsign = string.Empty;
        parked.IsOnGround = true;
        var viewport = Viewport(0, 0, 1000);

        using var flying = Render(renderer, new AircraftLayer { Markers = [airborne] }, viewport);
        using var ground = Render(renderer, new AircraftLayer { Markers = [parked] }, viewport);

        Assert.True(Brightest(ground) < Brightest(flying));
    }

    [Fact]
    public void Pin_RepaintsWhenTheThemeShadowChanges()
    {
        var renderer = new AircraftLayerRenderer();
        var marker = Marker(0, 0);
        marker.Callsign = string.Empty;
        var viewport = Viewport(0, 0, 1000);

        using var soft = Render(renderer, new AircraftLayer { Markers = [marker], ShadowColor = 0x33FF0000 }, viewport);
        using var hard = Render(renderer, new AircraftLayer { Markers = [marker], ShadowColor = 0xFFFF0000 }, viewport);

        Assert.True(PinPixels(hard).Count(pixel => pixel.Red > 200 && pixel.Green < 60) >
                    PinPixels(soft).Count(pixel => pixel.Red > 200 && pixel.Green < 60));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(300)]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(20000)]
    public void Polygon_LooksLikeMapsuisOwnDrawing(double resolution)
    {
        var feature = new GeometryFeature { Geometry = Star(radius: resolution * 110) };
        var style = new VectorStyle
        {
            Fill = new Brush(new Mapsui.Styles.Color(80, 160, 240, 120)),
            Outline = new Pen(new Mapsui.Styles.Color(240, 240, 240), 1.1)
        };
        var viewport = Viewport(resolution * 13.4, resolution * 7.7, resolution);

        using var cached = RenderStyle(new CachedPolygonRenderer(), feature, style, viewport);
        using var stock = RenderStyle(new VectorStyleRenderer(), feature, style, viewport);

        Assert.True(MeanDifference(cached, stock) < 2.0, $"mean difference {MeanDifference(cached, stock):0.00}");
    }

    [Fact]
    public void Polygon_DoesNotWobbleWhenPannedAtTheFinestResolutionItDraws()
    {
        // A continent-sized shape, 40 m per pixel, far from the middle of the world:
        // the case single precision is worst at. A whole-pixel pan has to move
        // the picture by exactly that, or the coast would shimmer during a pan.
        const double resolution = 40;
        const double originX = 9_000_000;
        var feature = new GeometryFeature
        {
            Geometry = new Polygon(new LinearRing(
            [
                new(originX - 6_000_000, -3_000_000),
                new(originX + 1000, -3_000_000),
                new(originX + 1000, 4000),
                new(originX - 2000, 2500),
                new(originX - 6_000_000, 3_000_000),
                new(originX - 6_000_000, -3_000_000)
            ]))
        };
        var style = new VectorStyle
        {
            Fill = new Brush(new Mapsui.Styles.Color(255, 255, 255)),
            Outline = new Pen(new Mapsui.Styles.Color(255, 0, 0), 1)
        };

        using var first = RenderStyle(new CachedPolygonRenderer(), feature, style, Viewport(originX, 0, resolution));
        using var panned = RenderStyle(
            new CachedPolygonRenderer(), feature, style, Viewport(originX + 17 * resolution, 11 * resolution, resolution));

        var worst = 0;
        for (var y = 0; y < Height - 11; y++)
        {
            for (var x = 17; x < Width; x++)
            {
                var a = first.GetPixel(x, y);
                var b = panned.GetPixel(x - 17, y + 11);
                worst = Math.Max(worst, Math.Abs(a.Green - b.Green));
            }
        }

        Assert.True(worst <= 40, $"largest difference {worst} of 255");
    }

    [Fact]
    public void Backdrop_LooksLikeTheMapDrawnDirectly()
    {
        using var model = CreateModel(out _);
        model.UpdateTraffic(Snapshot([Pilot(1, 0, 0)], [Tower("EDDB_TWR", 1)]));
        var map = model.Map!;
        using var backdrop = new BackdropMapRenderer();
        var viewport = map.Navigator.Viewport;

        // The first frame at a resolution draws directly, the second uses the bitmap.
        using var settling = RenderMap(backdrop, map, viewport);
        using var cached = RenderMap(backdrop, map, viewport);
        using var direct = RenderMap(new MapRenderer(), map, viewport);

        Assert.True(MeanDifference(cached, direct) < 1.0, $"mean difference {MeanDifference(cached, direct):0.00}");
    }

    [Fact]
    public void Backdrop_KeepsTheAircraftOnTheMapWhilePanningByPartsOfAPixel()
    {
        using var model = CreateModel(out _);
        model.UpdateTraffic(Snapshot([Pilot(1, 0, 0)], []));
        var map = model.Map!;
        using var backdrop = new BackdropMapRenderer();
        var start = map.Navigator.Viewport;
        using var settling = RenderMap(backdrop, map, start);
        using var captured = RenderMap(backdrop, map, start);

        // 0.4 px rounds the bitmap away, so the pin must follow it, not the viewport.
        var panned = new Viewport(
            start.CenterX + 40.4 * start.Resolution, start.CenterY, start.Resolution, 0, start.Width, start.Height);
        using var cached = RenderMap(backdrop, map, panned);
        var snapped = new Viewport(
            start.CenterX + 40 * start.Resolution, start.CenterY, start.Resolution, 0, start.Width, start.Height);
        using var exact = RenderMap(new MapRenderer(), map, snapped);

        Assert.True(MeanDifference(cached, exact) < 1.0, $"mean difference {MeanDifference(cached, exact):0.00}");
    }

    [Fact]
    public void Backdrop_PaintsAgainWhenALayerChanges()
    {
        using var model = CreateModel(out _);
        model.UpdateTraffic(Snapshot([], [Tower("EDDB_TWR", 1)]));
        var map = model.Map!;
        map.Navigator.CenterOn(WorldMap.Project(new GeoCoordinate(52, 14)));
        var viewport = map.Navigator.Viewport;
        using var backdrop = new BackdropMapRenderer();
        using var settling = RenderMap(backdrop, map, viewport);
        using var withStation = RenderMap(backdrop, map, viewport);

        model.UpdateTraffic(Snapshot([], []));
        using var cleared = RenderMap(backdrop, map, viewport);
        using var direct = RenderMap(new MapRenderer(), map, viewport);

        Assert.True(MeanDifference(withStation, direct) > 0.001);
        Assert.True(MeanDifference(cleared, direct) < 1.0);
    }

    [Fact]
    public void Backdrop_RepaintsAWholeNewAreaAfterALongPan()
    {
        using var model = CreateModel(out _);
        var map = model.Map!;
        using var backdrop = new BackdropMapRenderer();
        var start = map.Navigator.Viewport;
        using var settling = RenderMap(backdrop, map, start);
        using var captured = RenderMap(backdrop, map, start);

        var far = new Viewport(
            start.CenterX + 900 * start.Resolution, start.CenterY, start.Resolution, 0, start.Width, start.Height);
        using var cached = RenderMap(backdrop, map, far);
        using var direct = RenderMap(new MapRenderer(), map, far);

        Assert.True(MeanDifference(cached, direct) < 1.0);
    }

    [Fact]
    public void Backdrop_PaintsAgainWhenATileLands()
    {
        var tiles = new LoadingLayer();
        var stations = new MemoryLayer(IvaoMapLayers.AtcStationsLayerName) { Style = null };
        using var map = new Map { BackColor = new Mapsui.Styles.Color(0, 0, 0) };
        map.Layers.Add(tiles);
        map.Layers.Add(stations);
        var viewport = Viewport(0, 0, 1000);
        using var backdrop = new BackdropMapRenderer();
        using var settling = RenderMap(backdrop, map, viewport);
        using var empty = RenderMap(backdrop, map, viewport);
        Assert.Equal(SKColors.Black, empty.GetPixel(Width / 2, Height / 2));

        tiles.Land(new GeometryFeature
        {
            Geometry = new Polygon(new LinearRing(
                [new(-50000, -50000), new(50000, -50000), new(50000, 50000), new(-50000, 50000), new(-50000, -50000)])),
            Styles = [new VectorStyle { Fill = new Brush(new Mapsui.Styles.Color(255, 255, 255)), Outline = null }]
        });
        using var landed = RenderMap(backdrop, map, viewport);

        Assert.Equal(SKColors.White, landed.GetPixel(Width / 2, Height / 2));
    }

    [Fact]
    public void Polygon_HolesStayOpen()
    {
        var outer = new LinearRing([new(-100000, -100000), new(100000, -100000), new(100000, 100000), new(-100000, 100000), new(-100000, -100000)]);
        var hole = new LinearRing([new(-40000, -40000), new(40000, -40000), new(40000, 40000), new(-40000, 40000), new(-40000, -40000)]);
        var feature = new GeometryFeature { Geometry = new Polygon(outer, [hole]) };
        var style = new VectorStyle { Fill = new Brush(new Mapsui.Styles.Color(255, 255, 255)), Outline = null };

        using var bitmap = RenderStyle(new CachedPolygonRenderer(), feature, style, Viewport(0, 0, 1000));

        Assert.Equal(SKColors.Black, bitmap.GetPixel(Width / 2, Height / 2));
        Assert.Equal(SKColors.White, bitmap.GetPixel(Width / 2 + 70, Height / 2));
    }

    [Fact]
    public void Polygon_FarZoomDropsTheVerticesItCannotShow()
    {
        // Thousands of vertices along a shape that spans a few pixels at this zoom.
        const double resolution = 20000;
        var points = Enumerable.Range(0, 20000)
            .Select(index => index * Math.PI * 2 / 20000)
            .Select(angle => new Coordinate(Math.Cos(angle) * resolution * 60, Math.Sin(angle) * resolution * 60))
            .ToList();
        points.Add(points[0]);
        var feature = new GeometryFeature { Geometry = new Polygon(new LinearRing(points.ToArray())) };
        var style = new VectorStyle { Fill = new Brush(new Mapsui.Styles.Color(255, 255, 255)), Outline = null };
        var viewport = Viewport(0, 0, resolution);

        using var cached = RenderStyle(new CachedPolygonRenderer(), feature, style, viewport);
        using var stock = RenderStyle(new VectorStyleRenderer(), feature, style, viewport);

        Assert.True(MeanDifference(cached, stock) < 1.0);
    }

    [Fact]
    public void ToArgb_ReadsThePlainAndTheTranslucentThemeColours()
    {
        Assert.Equal(0xFF102030u, WorldMap.ToArgb("#102030"));
        Assert.Equal(0x66000000u, WorldMap.ToArgb("#66000000"));
        Assert.Equal(0x33ABCDEFu, WorldMap.ToArgb("#33ABCDEF"));
    }

    [Fact]
    public void Palette_CarriesTheThemeCallsignAndShadowColoursToTheAircraft()
    {
        using var model = CreateModel(out _);
        model.ApplyTheme(Dark with { Text = "#112233", Shadow = "#44000000" });

        var layer = Aircraft(model);
        Assert.Equal(0xFF112233u, layer.LabelColor);
        Assert.Equal(0x44000000u, layer.ShadowColor);
    }

    [Fact]
    public void Layer_IsOnTopOfEverythingAndTheOneSwitchHidesIt()
    {
        using var model = CreateModel(out _);
        Assert.Same(Aircraft(model), model.Map!.Layers.Last());

        model.ShowPilots = false;
        Assert.False(Aircraft(model).Enabled);

        model.ShowPilots = true;
        Assert.True(Aircraft(model).Enabled);
    }

    [Fact]
    public void Layer_StaysOnTopWhenTheBasemapChanges()
    {
        using var directory = new TemporaryDirectory();
        using var model = new MapViewModel(new Images(), basemapCacheDirectory: directory.DirectoryPath);
        model.Initialize(Dark, new GeoCoordinate(0, 0), 1000);

        model.MapStyle = "positron";

        Assert.Same(Aircraft(model), model.Map!.Layers.Last());
    }

    [Fact]
    public void Traffic_PublishesEveryAircraftAndDropsTheOnesThatLeft()
    {
        using var model = CreateModel(out _);
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0), Pilot(2, 1, 1)));
        Assert.Equal(2, Aircraft(model).Markers.Length);

        model.UpdateTraffic(Snapshot(Pilot(2, 1, 1)));

        Assert.Same(model.Pilots.Single().Marker, Assert.Single(Aircraft(model).Markers));
    }

    [Fact]
    public void Marker_TracksWhatThePinShows()
    {
        using var model = CreateModel(out _);
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0, heading: 90, callsign: "TAP123")));
        var item = model.Pilots.Single();
        var marker = item.Marker;
        Assert.Equal("TAP123", marker.Callsign);
        Assert.Equal(90, marker.Heading);
        Assert.Equal(item.DisplayGlyphSize, marker.Size);
        Assert.Equal(item.MarkerSize, marker.BoxSize);
        Assert.Equal(WorldMap.ToArgb(item.IconColor), marker.Color);

        item.MarkerColor = "#FF8800";
        Assert.Equal(0xFFFF8800u, marker.Color);

        item.MarkerColor = string.Empty;
        Assert.Equal(WorldMap.ToArgb(item.IconColor), marker.Color);

        var plainSize = marker.Size;
        item.IsSelected = true;
        Assert.True(marker.Size > plainSize);
        item.IsSelected = false;
        item.IsHovered = true;
        Assert.True(marker.Size > plainSize);
    }

    [Fact]
    public void Motion_DoesNotRedrawUntilAVisibleAircraftMovedOnScreen()
    {
        var clock = new Clock();
        using var model = CreateModel(out _, clock);
        model.Map!.Navigator.CenterOnAndZoomTo(WorldMap.Project(new GeoCoordinate(0, 0)), 20000);
        model.UpdateTraffic(Snapshot(clock, Pilot(1, 0, 0, speed: 400)));
        clock.Advance(1.2);
        model.AdvanceAircraft();
        var refreshes = 0;
        model.Map.RefreshGraphicsRequest += (_, _) => refreshes++;

        // 400 knots is about 0.01 pixels per second at this zoom.
        clock.Advance(1);
        model.AdvanceAircraft();
        Assert.Equal(0, refreshes);

        clock.Advance(30);
        model.AdvanceAircraft();
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void Motion_FollowsFastAircraftCloseIn()
    {
        var clock = new Clock();
        using var model = CreateModel(out _, clock);
        model.Map!.Navigator.CenterOnAndZoomTo(WorldMap.Project(new GeoCoordinate(0, 0)), 20);
        model.UpdateTraffic(Snapshot(clock, Pilot(1, 0, 0, speed: 400)));
        clock.Advance(1.2);
        model.AdvanceAircraft();
        var before = model.Pilots.Single().Marker.WorldX;

        // About ten pixels per second: well past the threshold at every tick.
        clock.Advance(0.05);
        model.AdvanceAircraft();

        Assert.True(model.Pilots.Single().Marker.WorldX > before);
    }

    [Fact]
    public void Motion_ChecksSoonerOnceTheTrafficRefreshes()
    {
        var clock = new Clock();
        using var model = CreateModel(out _, clock);
        model.Map!.Navigator.CenterOnAndZoomTo(WorldMap.Project(new GeoCoordinate(0, 0)), 20000);
        model.UpdateTraffic(Snapshot(clock, Pilot(1, 0, 0, speed: 0)));
        clock.Advance(1.2);
        model.AdvanceAircraft();

        // A parked aircraft leaves the check at its longest wait, and a report
        // that moves it has to be followed at once regardless.
        model.UpdateTraffic(Snapshot(clock, Pilot(1, 0, 3, speed: 0)));
        var marker = model.Pilots.Single().Marker;
        var start = marker.WorldX;
        var positions = new List<double>();
        for (var step = 0; step < 24; step++)
        {
            clock.Advance(0.05);
            model.AdvanceAircraft();
            positions.Add(marker.WorldX);
        }

        // The aircraft eases in and comes to rest within the redraw threshold of
        // the report, a quarter of a pixel, which at this zoom is 5 km.
        Assert.InRange(positions[^1], WorldMap.Project(new GeoCoordinate(0, 3)).X - 5000, double.MaxValue);
        Assert.Contains(positions, x => x != start && x != positions[^1]);
    }

    [Fact]
    public void Viewport_MovesAreOnlyNotedUntilThePointerAsks()
    {
        using var model = CreateModel(out var target);
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0)));
        var item = model.Pilots.Single();
        var before = item.X;
        var navigator = model.Map!.Navigator;

        navigator.CenterOn(new MPoint(navigator.Viewport.CenterX + 100 * navigator.Viewport.Resolution, navigator.Viewport.CenterY));

        // Thousands of pins would otherwise be re-projected on every frame.
        Assert.Equal(before, item.X);

        var screen = new ScreenPosition(target.X - 100, target.Y);
        Assert.Same(item, model.PilotMarkerAt(screen));
        Assert.Equal(before - 100, item.X, 6);
    }

    [Fact]
    public void Viewport_ChangeOnAWorkerThreadLeavesTheHoverToTheUiThread()
    {
        using var model = CreateModel(out var target);
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0)));
        model.SetHoveredAtc(new ScreenPosition(target.X, target.Y));
        Assert.NotNull(model.MapTooltipText);

        var navigator = model.Map!.Navigator;
        navigator.CenterOn(new MPoint(navigator.Viewport.CenterX + 1000, navigator.Viewport.CenterY));

        // The pin has moved from under the pointer, so it is no longer hovered.
        Assert.False(model.Pilots.Single().IsHovered);
        Assert.False(model.IsPointerOverAircraft);
        Assert.Null(model.MapTooltipText);
    }

    [Fact]
    public void Pointer_OverAPinHoversItAndLeavingClearsIt()
    {
        using var model = CreateModel(out var target);
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0)));
        var item = model.Pilots.Single();

        model.SetHoveredAtc(new ScreenPosition(target.X, target.Y));
        Assert.True(item.IsHovered);
        Assert.True(model.IsPointerOverAircraft);
        Assert.Equal(item.Tooltip, model.MapTooltipText);
        Assert.NotEmpty(Layer(model, IvaoMapLayers.HoverRouteLayerName).Features);

        model.SetHoveredAtc(new ScreenPosition(target.X + 200, target.Y + 100));
        Assert.False(item.IsHovered);
        Assert.False(model.IsPointerOverAircraft);
        Assert.Empty(Layer(model, IvaoMapLayers.HoverRouteLayerName).Features);

        model.SetHoveredAtc(new ScreenPosition(target.X, target.Y));
        model.SetHoveredAtc(null);
        Assert.False(item.IsHovered);
        Assert.False(model.HasMapTooltip);
    }

    [Fact]
    public void Pointer_HiddenPinsAreNotThere()
    {
        using var model = CreateModel(out var target);
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0)));
        model.ShowPilots = false;

        Assert.Null(model.PilotMarkerAt(new ScreenPosition(target.X, target.Y)));
    }

    [Fact]
    public void Pointer_ThePinDrawnLastIsTheOneOnTop()
    {
        using var model = CreateModel(out var target);
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0), Pilot(2, 0, 0)));

        Assert.Same(model.Pilots[1], model.PilotMarkerAt(new ScreenPosition(target.X, target.Y)));
    }

    [Fact]
    public void Pointer_TheWholeBoxOfThePinCounts()
    {
        using var model = CreateModel(out var target);
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0)));
        var half = model.Pilots.Single().MarkerSize / 2d;

        Assert.NotNull(model.PilotMarkerAt(new ScreenPosition(target.X + half - 1, target.Y - half + 1)));
        Assert.Null(model.PilotMarkerAt(new ScreenPosition(target.X + half + 1, target.Y)));
    }

    [Fact]
    public void Tap_SelectsTheAircraftWithoutCentringWhenCentringIsOff()
    {
        using var model = CreateModel(out var target);
        model.CenterOnMapClick = false;
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0)));
        var world = model.Map!.Navigator.Viewport.ScreenToWorld(target);

        model.TapAt(world, target);

        Assert.Same(model.Pilots.Single(), model.SelectedPilot);
    }

    [Fact]
    public void Tap_OnTheSelectedAircraftKeepsItSelected()
    {
        using var model = CreateModel(out var target);
        model.CenterOnMapClick = false;
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0)));
        var world = model.Map!.Navigator.Viewport.ScreenToWorld(target);

        model.TapAt(world, target);
        model.TapAt(world, target);

        Assert.Same(model.Pilots.Single(), model.SelectedPilot);
    }

    [Fact]
    public void Tap_NextToTheAircraftClearsTheSelection()
    {
        using var model = CreateModel(out var target);
        model.CenterOnMapClick = false;
        model.UpdateTraffic(Snapshot(Pilot(1, 0, 0)));
        model.TapAt(model.Map!.Navigator.Viewport.ScreenToWorld(target), target);

        var away = new ScreenPosition(target.X + 150, target.Y + 100);
        model.TapAt(model.Map.Navigator.Viewport.ScreenToWorld(away), away);

        Assert.Null(model.SelectedPilot);
    }

    [Fact]
    public void Atc_UnchangedStationsKeepTheirLayersAcrossARefresh()
    {
        using var model = CreateModel(out _);
        model.UpdateTraffic(Snapshot([], [Tower("EDDB_TWR", 1)]));
        var regions = Layer(model, IvaoMapLayers.AtcRegionsLayerName).Features;
        var stations = Layer(model, IvaoMapLayers.AtcStationsLayerName).Features;

        model.UpdateTraffic(Snapshot([], [Tower("EDDB_TWR", 1)]));

        Assert.Same(regions, Layer(model, IvaoMapLayers.AtcRegionsLayerName).Features);
        Assert.Same(stations, Layer(model, IvaoMapLayers.AtcStationsLayerName).Features);
    }

    [Fact]
    public void Atc_ANewStationOrSupervisorRebuildsTheLayers()
    {
        using var model = CreateModel(out _);
        model.UpdateTraffic(Snapshot([], [Tower("EDDB_TWR", 1)]));
        var stations = Layer(model, IvaoMapLayers.AtcStationsLayerName).Features;

        model.UpdateTraffic(Snapshot([], [Tower("EDDB_TWR", 1), Tower("EDDH_TWR", 2)]));
        Assert.Equal(2, Layer(model, IvaoMapLayers.AtcStationsLayerName).Features.Count());
        Assert.NotSame(stations, Layer(model, IvaoMapLayers.AtcStationsLayerName).Features);

        stations = Layer(model, IvaoMapLayers.AtcStationsLayerName).Features;
        model.UpdateTraffic(new TrackerSnapshot(
            [],
            [Tower("EDDB_TWR", 1), Tower("EDDH_TWR", 2)],
            [new IvaoSupervisor { UserId = 1 }],
            new IvaoConnectionStats(),
            DateTimeOffset.UtcNow,
            []));
        Assert.NotSame(stations, Layer(model, IvaoMapLayers.AtcStationsLayerName).Features);
    }

    [Fact]
    public void Layers_KeepTheirPlaceInTheMapWhenTheirContentChanges()
    {
        using var model = CreateModel(out _);
        var before = model.Map!.Layers.ToArray();

        model.UpdateTraffic(Snapshot([Pilot(1, 0, 0)], [Tower("EDDB_TWR", 1)]));
        model.SelectPilot(model.Pilots.Single(), center: false);
        model.ApplyTheme(Dark);

        Assert.Equal(before, model.Map.Layers.ToArray());
    }

    [Fact]
    public void FilteredCollection_WaitsForTheBatchAndResetsOnce()
    {
        var source = new System.Collections.ObjectModel.ObservableCollection<string>();
        var view = new FilteredCollection<string>(source);
        var resets = 0;
        ((INotifyCollectionChanged)view).CollectionChanged += (_, _) => resets++;

        view.BeginUpdate();
        for (var index = 0; index < 100; index++)
        {
            source.Add(index.ToString());
        }

        Assert.Empty(view);
        view.EndUpdate();

        Assert.Equal(100, view.Count);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void FilteredCollection_DoesNotResetWhenNothingChanged()
    {
        var source = new System.Collections.ObjectModel.ObservableCollection<string> { "a", "b" };
        var view = new FilteredCollection<string>(source);
        var resets = 0;
        ((INotifyCollectionChanged)view).CollectionChanged += (_, _) => resets++;

        view.Refresh();
        Assert.Equal(0, resets);

        source.Add("c");
        Assert.Equal(1, resets);
    }

    private static MapViewModel CreateModel(out ScreenPosition centre, Clock? clock = null)
    {
        var model = new MapViewModel(new Images(), clock);
        model.Initialize(Dark, new GeoCoordinate(0, 0), 1000);
        model.Map!.Navigator.SetSize(800, 600);
        centre = new ScreenPosition(400, 300);
        return model;
    }

    private static AircraftLayer Aircraft(MapViewModel model) =>
        Assert.IsType<AircraftLayer>(model.Map!.Layers.Single(layer => layer.Name == AircraftLayer.LayerName));

    private static MemoryLayer Layer(MapViewModel model, string name) =>
        Assert.IsType<MemoryLayer>(model.Map!.Layers.Single(layer => layer.Name == name));

    private static IvaoPilot Pilot(
        long id, double latitude, double longitude, int speed = 0, int heading = 90, string callsign = "TEST") => new()
    {
        Id = id,
        Callsign = callsign,
        LastTrack = new IvaoLastTrack
        {
            Latitude = latitude,
            Longitude = longitude,
            GroundSpeed = speed,
            Heading = heading,
            Timestamp = DateTimeOffset.UtcNow
        },
        FlightPlan = new IvaoFlightPlan
        {
            Departure = new IvaoAirport { Latitude = 10, Longitude = 10 },
            Arrival = new IvaoAirport { Latitude = -10, Longitude = -10 }
        }
    };

    private static IvaoAtc Tower(string callsign, long id) => new()
    {
        Id = id,
        UserId = id,
        Callsign = callsign,
        AtcSession = new IvaoAtcSession { Position = "TWR", Frequency = 118.5 },
        AtcPosition = new IvaoAtcPosition
        {
            Position = "TWR",
            AtcCallsign = callsign,
            Airport = new IvaoAirport { Icao = "EDDB", Latitude = 52, Longitude = 13 + id }
        }
    };

    private static TrackerSnapshot Snapshot(params IvaoPilot[] pilots) => Snapshot(pilots, []);

    private static TrackerSnapshot Snapshot(Clock clock, params IvaoPilot[] pilots)
    {
        foreach (var pilot in pilots)
        {
            pilot.LastTrack!.Timestamp = clock.GetUtcNow();
        }

        return Snapshot(pilots, []);
    }

    private static TrackerSnapshot Snapshot(IvaoPilot[] pilots, IvaoAtc[] atc) =>
        new(pilots, atc, [], new IvaoConnectionStats(), DateTimeOffset.UtcNow, []);

    private static AircraftMarker Marker(double worldX, double worldY) => new()
    {
        WorldX = worldX,
        WorldY = worldY,
        HasPosition = true,
        Glyph = AircraftGlyph.MediumTwin,
        Heading = 0,
        Size = 30,
        BoxSize = 35,
        Color = 0xFFFFFFFF,
        Callsign = string.Empty
    };

    private static Viewport Viewport(double centerX, double centerY, double resolution) =>
        new(centerX, centerY, resolution, 0, Width, Height);

    private static SKBitmap Render(AircraftLayerRenderer renderer, AircraftLayer layer, Viewport viewport)
    {
        var bitmap = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        renderer.Render(canvas, viewport, layer, new RenderService());
        return bitmap;
    }

    private static SKBitmap RenderStyle(
        Mapsui.Rendering.Skia.SkiaStyles.ISkiaStyleRenderer renderer, IFeature feature, IStyle style, Viewport viewport)
    {
        var bitmap = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);
        canvas.Save();
        renderer.Draw(canvas, viewport, new MemoryLayer("test"), feature, style, new RenderService(), 0);
        canvas.Restore();
        return bitmap;
    }

    private static SKBitmap RenderMap(IMapRenderer renderer, Map map, Viewport viewport)
    {
        var bitmap = new SKBitmap((int)viewport.Width, (int)viewport.Height);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(canvas, viewport, map.Layers.ToArray(), map.Widgets, map.RenderService, map.BackColor);
        return bitmap;
    }

    private static IEnumerable<SKColor> PinPixels(SKBitmap bitmap) => bitmap.Pixels.Where(pixel => pixel != SKColors.Black);

    private static int Brightest(SKBitmap bitmap) => bitmap.Pixels.Max(pixel => (int)pixel.Red);

    private static (double X, double Y) CentreOfPin(SKBitmap bitmap)
    {
        double left = int.MaxValue, right = int.MinValue, top = int.MaxValue, bottom = int.MinValue;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Red > 128)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return ((left + right + 1) / 2, (top + bottom + 1) / 2);
    }

    private static double MeanDifference(SKBitmap first, SKBitmap second)
    {
        var a = first.Pixels;
        var b = second.Pixels;
        double total = 0;
        for (var index = 0; index < a.Length; index++)
        {
            total += Math.Abs(a[index].Red - b[index].Red) +
                     Math.Abs(a[index].Green - b[index].Green) +
                     Math.Abs(a[index].Blue - b[index].Blue);
        }

        return total / (a.Length * 3);
    }

    /// <summary>A jagged outline with enough corners to expose any drift in the transform.</summary>
    private static Polygon Star(double radius)
    {
        var points = new List<Coordinate>();
        for (var index = 0; index < 40; index++)
        {
            var angle = index * Math.PI / 20;
            var length = index % 2 == 0 ? radius : radius * 0.55;
            points.Add(new Coordinate(Math.Cos(angle) * length + radius * 0.2, Math.Sin(angle) * length));
        }

        points.Add(points[0]);
        return new Polygon(new LinearRing(points.ToArray()));
    }

    /// <summary>
    /// The map tests render whole maps, which need the app's renderers registered.
    /// Mapsui's registry is one process-wide table, so this happens once, up front.
    /// </summary>
    static MapRenderingTests() => MapRenderers.Register();

    /// <summary>A layer whose data arrives by itself, the way a tile layer's does.</summary>
    private sealed class LoadingLayer() : BaseLayer("Tiles")
    {
        private IFeature[] _features = [];

        public void Land(IFeature feature)
        {
            _features = [feature];
            DataHasChanged();
        }

        public override IEnumerable<IFeature> GetFeatures(MRect box, double resolution) => _features;
    }

    private sealed class PixelComparer(double tolerance) : IEqualityComparer<(double X, double Y)>
    {
        public bool Equals((double X, double Y) a, (double X, double Y) b) =>
            Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

        public int GetHashCode((double X, double Y) obj) => 0;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        private long _ticks;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(double seconds)
        {
            _now = _now.AddSeconds(seconds);
            _ticks += TimeSpan.FromSeconds(seconds).Ticks;
        }
    }

    private sealed class Images : IImagePipeline, IAircraftIconCache, IImageCache
    {
        public IAircraftIconCache AircraftIcons => this;

        public IImageCache SessionImages => this;

        public object Get(AircraftGlyph glyph) => glyph;

        public object? TryGet(string? url) => null;

        public Task<object?> GetAsync(string? url, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public void Dispose()
        {
        }
    }
}

[CollectionDefinition(RendererRegistration.Name, DisableParallelization = true)]
public static class RendererRegistration
{
    public const string Name = "Map renderers";
}
