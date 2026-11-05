using System.Net;
using BruTile;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Rendering.Skia;
using Mapsui.Tiling.Layers;
using SkiaSharp;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Rendering;

namespace TheEye.Core.Tests;

/// <summary>
/// The map repeats east and west. These tests pin down what that has to preserve:
/// a pan that never jumps, one world of data drawn once per copy without anything
/// doubling up at the date line, and tiles that are the real ones on either side.
/// </summary>
[Collection(RendererRegistration.Name)]
public sealed class WorldWrapTests
{
    private const double World = WorldWrap.WorldWidth;
    private const double Edge = WorldWrap.HalfWorldWidth;
    private const double ShallowResolution = WorldMap.MaxZoomOutResolution;

    private static readonly MapPalette Dark = new("#0E2233", "#1B303F", "#2C4557", "#AEC4D4", "#4FB3FF", true);

    static WorldWrapTests() => MapRenderers.Register();

    [Fact]
    public void WorldWidth_IsTheWidthOfTheProjection()
    {
        Assert.Equal(WorldMap.Project(0, 180).X, Edge, 6);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(Edge + 1000, -Edge + 1000)]
    [InlineData(-Edge - 1000, Edge - 1000)]
    [InlineData(1000 + 3 * World, 1000)]
    public void Wrap_BringsAnyPositionIntoTheMainWorld(double x, double expected)
    {
        Assert.Equal(expected, WorldWrap.Wrap(x), 3);
    }

    [Fact]
    public void NearestCopy_TakesTheShortWayAcrossTheDateLine()
    {
        Assert.Equal(Edge + 1000, WorldWrap.NearestCopy(-Edge + 1000, Edge - 1000), 3);
        Assert.Equal(-Edge - 1000, WorldWrap.NearestCopy(Edge - 1000, -Edge + 1000), 3);
        Assert.Equal(5000, WorldWrap.NearestCopy(5000, 0), 3);
    }

    [Fact]
    public void Copies_OnlyTheMainWorldAwayFromTheDateLine()
    {
        Assert.Equal((0, 0), WorldWrap.CopiesOf(null, Viewport(0, 1000)));
    }

    [Theory]
    [InlineData(Edge, 0, 1)]
    [InlineData(-Edge, -1, 0)]
    public void Copies_BothSidesAtTheDateLine(double centerX, int first, int last)
    {
        Assert.Equal((first, last), WorldWrap.CopiesOf(null, Viewport(centerX, 1000)));
    }

    [Fact]
    public void Copies_FollowAnExtentThatRunsPastTheEdgeOfTheWorld()
    {
        var overhanging = new MRect(Edge - 1_000_000, 0, Edge + 1_000_000, 1);

        // Nowhere near the main copy of it, but right on top of the one to the west.
        Assert.Equal((-1, -1), WorldWrap.CopiesOf(overhanging, Viewport(-Edge + 500_000, 1000)));
    }

    [Fact]
    public void Copies_NoneForAnExtentOutOfView()
    {
        var (first, last) = WorldWrap.CopiesOf(new MRect(1_000_000, 0, 2_000_000, 1), Viewport(-5_000_000, 1000));

        Assert.True(first > last);
    }

    [Fact]
    public void Pointer_StaysPutUnlessTheWindowIsWiderThanTheWorld()
    {
        var viewport = Viewport(0, 1000);
        Assert.Equal(new ScreenPosition(12, 34), WorldWrap.OntoNearestCopy(new ScreenPosition(12, 34), viewport));

        var wide = new Viewport(0, 0, ShallowResolution, 0, 5000, 300);
        var worldInPixels = World / ShallowResolution;
        var moved = WorldWrap.OntoNearestCopy(new ScreenPosition(4900, 34), wide);
        Assert.Equal(4900 - worldInPixels, moved.X, 6);
        Assert.Equal(34, moved.Y);
    }

    [Fact]
    public void Navigator_PansAroundTheWorldWithoutEverJumping()
    {
        using var map = WorldMap.Create(Dark, new GeoCoordinate(0, 0), 4891.96);
        var navigator = map.Navigator;
        navigator.SetSize(800, 600);
        navigator.SetViewport(new Viewport(Edge - 100_000, 0, 4891.96, 0, 800, 600));
        var step = 20 * navigator.Viewport.Resolution;

        var wraps = 0;
        for (var drag = 0; drag < 2000; drag++)
        {
            var before = navigator.Viewport.CenterX;
            navigator.Manipulate(new Manipulation(new ScreenPosition(380, 300), new ScreenPosition(400, 300), 1, 0, 0));
            var moved = navigator.Viewport.CenterX - before;
            if (Math.Abs(moved) > Edge)
            {
                wraps++;
            }

            Assert.Equal(step, WorldWrap.Wrap(moved), 3);
            Assert.InRange(navigator.Viewport.CenterX, -Edge, Edge);
        }

        Assert.True(wraps >= 4);
    }

    [Fact]
    public void Navigator_StillStopsAtTheTopOfTheMap()
    {
        using var map = WorldMap.Create(Dark, new GeoCoordinate(0, 0), 4891.96);
        map.Navigator.SetSize(800, 600);

        map.Navigator.SetViewport(new Viewport(0, 3 * Edge, 4891.96, 0, 800, 600));

        Assert.Equal(20037508.34, map.Navigator.Viewport.CenterY, 3);
    }

    [Fact]
    public void Navigator_AnimatesAcrossTheDateLineToTheExactTarget()
    {
        using var map = WorldMap.Create(Dark, new GeoCoordinate(0, 0), 4891.96);
        var navigator = map.Navigator;
        navigator.SetSize(800, 600);
        navigator.SetViewport(new Viewport(Edge - 300_000, 0, 4891.96, 0, 800, 600));

        navigator.CenterOn(new MPoint(Edge + 300_000, 0), 100);
        Settle(navigator);

        Assert.Equal(-Edge + 300_000, navigator.Viewport.CenterX, 0);
    }

    [Fact]
    public void Navigator_DoesNotJumpWhenTheZoomIsLimitedAsTheCentreWraps()
    {
        using var map = WorldMap.Create(Dark, new GeoCoordinate(0, 0), 4891.96);
        var navigator = map.Navigator;
        navigator.SetSize(800, 600);
        navigator.SetViewport(new Viewport(Edge - 1000, 0, ShallowResolution / 1.5, 0, 800, 600));
        var before = navigator.Viewport.CenterX;

        navigator.SetViewport(new Viewport(Edge + 1000, 0, ShallowResolution * 1.5, 0, 800, 600));

        Assert.InRange(Math.Abs(WorldWrap.Wrap(navigator.Viewport.CenterX - before)), 0, 10_000);
    }

    [Fact]
    public void Renderer_ChangesNothingAwayFromTheDateLine()
    {
        using var map = WorldMap.Create(Dark, new GeoCoordinate(0, 0), 4891.96);
        var center = WorldMap.Project(48, 10);
        var viewport = new Viewport(center.X, center.Y, 4891.96, 0, 400, 300);

        using var stock = Render(new MapRenderer(), map, viewport);
        using var wrapped = Render(new WrappedMapRenderer(), map, viewport);

        Assert.Equal(stock.Pixels, wrapped.Pixels);
    }

    [Theory]
    [InlineData(179.9)]
    [InlineData(-179.9)]
    public void Renderer_ShowsTheOtherSideOfTheWorldPastTheDateLine(double centerLongitude)
    {
        var aircraft = new AircraftLayer { Markers = [Marker(WorldMap.Project(0, -178)), Marker(WorldMap.Project(0, 178))] };
        var viewport = new Viewport(WorldMap.Project(0, centerLongitude).X, 0, 4891.96, 0, 400, 300);
        using var map = new Map { BackColor = Mapsui.Styles.Color.Black };
        map.Layers.Add(aircraft);

        using var bitmap = Render(new WrappedMapRenderer(), map, viewport);

        foreach (var longitude in new[] { -178d, 178d })
        {
            var x = 200 + WorldWrap.Wrap(WorldMap.Project(0, longitude).X - viewport.CenterX) / viewport.Resolution;
            Assert.NotEqual(bitmap.GetPixel(0, 0), bitmap.GetPixel((int)x, 150));
        }
    }

    [Fact]
    public void Renderer_DrawsTheSameAircraftTwiceInAWindowWiderThanTheWorld()
    {
        var aircraft = new AircraftLayer { Markers = [Marker(WorldMap.Project(0, 170))] };
        var viewport = new Viewport(0, 0, ShallowResolution, 0, 5000, 300);
        using var map = new Map { BackColor = Mapsui.Styles.Color.Black };
        map.Layers.Add(aircraft);

        using var bitmap = Render(new WrappedMapRenderer(), map, viewport);

        var east = 2500 + WorldMap.Project(0, 170).X / ShallowResolution;
        Assert.NotEqual(bitmap.GetPixel(0, 0), bitmap.GetPixel((int)east, 150));
        Assert.NotEqual(bitmap.GetPixel(0, 0), bitmap.GetPixel((int)(east - World / ShallowResolution), 150));
    }

    [Theory]
    [InlineData(179.9)]
    [InlineData(-179.9)]
    public void Renderer_PaintsASectorAcrossTheDateLineOnce(double centerLongitude)
    {
        var palette = Dark;
        var center = WorldMap.Project(-35, centerLongitude);
        var viewport = new Viewport(center.X, center.Y, 2445.98, 0, 800, 600);
        using var crossing = new Map { BackColor = WorldMap.Parse(palette.Water) };
        crossing.Layers.Add(IvaoMapLayers.CreateAtcRegions(
            [Sector((-25, 170), (-25, -170), (-45, -170), (-45, 170))], palette));
        using var plain = new Map { BackColor = WorldMap.Parse(palette.Water) };
        plain.Layers.Add(IvaoMapLayers.CreateAtcRegions(
            [Sector((-25, 10), (-25, 50), (-45, 50), (-45, 10))], palette));
        var aside = WorldMap.Project(-35, 30);

        using var bitmap = Render(new WrappedMapRenderer(), crossing, viewport);
        using var reference = Render(
            new WrappedMapRenderer(), plain, new Viewport(aside.X, aside.Y, 2445.98, 0, 800, 600));

        var expected = reference.GetPixel(400, 300);
        Assert.NotEqual(WorldMap.Parse(palette.Water).R, expected.Red);
        foreach (var longitude in new[] { 178d, -178d })
        {
            var x = 400 + WorldWrap.Wrap(WorldMap.Project(-35, longitude).X - viewport.CenterX) / viewport.Resolution;
            Assert.Equal(expected, bitmap.GetPixel((int)x, 300));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void Renderer_LeavesNoLineWhereACountryIsCutAtTheDateLine(double pixelOffset)
    {
        using var map = WorldMap.Create(Dark, new GeoCoordinate(0, 0), 2445.98);
        map.Layers.Single(layer => layer.Name == WorldMap.CountryLabelsLayerName).Enabled = false;
        var center = WorldMap.Project(67, 180);
        var viewport = new Viewport(center.X + pixelOffset * 2445.98, center.Y, 2445.98, 0, 400, 300);

        using var bitmap = Render(new WrappedMapRenderer(), map, viewport);

        // Land over land may round one level off where the fill overlaps itself;
        // the water showing through, the seam this is about, is 13 levels away.
        var land = WorldMap.Parse(Dark.Land);
        for (var x = 190; x <= 210; x++)
        {
            var pixel = bitmap.GetPixel(x, 150);
            Assert.InRange(pixel.Red, land.R - 1, land.R);
            Assert.InRange(pixel.Green, land.G - 1, land.G);
            Assert.InRange(pixel.Blue, land.B - 1, land.B);
        }
    }

    [Fact]
    public void Backdrop_KeepsItsBitmapWhenTheNavigatorJumpsAWholeWorld()
    {
        var layer = new CountingLayer();
        using var map = new Map();
        map.Layers.Add(layer);
        using var backdrop = new BackdropMapRenderer();
        var east = new Viewport(Edge - 1000, 0, 4891.96, 0, 400, 300);

        // The first frame only learns the resolution; the second paints the bitmap.
        Render(backdrop, map, east).Dispose();
        using var before = Render(backdrop, map, east);
        var queries = layer.Queries;
        using var after = Render(backdrop, map, east with { CenterX = east.CenterX - World });

        Assert.Equal(queries, layer.Queries);
        Assert.Equal(before.Pixels, after.Pixels);
    }

    [Theory]
    [InlineData(3, -1, 7)]
    [InlineData(3, 8, 0)]
    [InlineData(3, 7, 7)]
    [InlineData(3, 16, 0)]
    [InlineData(0, -3, 0)]
    public void RealIndex_NamesTheTileAtThatPlaceOfTheWorld(int level, int column, int expected)
    {
        Assert.Equal(new TileIndex(expected, 2, level), WrappedTileSource.RealIndex(new TileIndex(column, 2, level)));
    }

    [Fact]
    public void Schema_CarriesOnPastBothEdgesOfTheWorld()
    {
        var acrossEast = new Extent(Edge - 1000, -1000, Edge + 1000, 1000);
        var acrossWest = new Extent(-Edge - 1000, -1000, -Edge + 1000, 1000);

        Assert.Equal([7, 8], MapBasemap.WrappedSchema.GetTileInfos(acrossEast, 3).Select(tile => tile.Index.Col).Distinct());
        Assert.Equal([-1, 0], MapBasemap.WrappedSchema.GetTileInfos(acrossWest, 3).Select(tile => tile.Index.Col).Distinct());
    }

    [Fact]
    public void Schema_MatchesTheRealOneInsideTheWorld()
    {
        var extent = new Extent(1_000_000, 1_000_000, 8_000_000, 6_000_000);

        Assert.Equal(
            MapBasemap.Schema.GetTileInfos(extent, 5).Select(tile => tile.Index),
            MapBasemap.WrappedSchema.GetTileInfos(extent, 5).Select(tile => tile.Index));
    }

    [Fact]
    public void Apply_GivesTheTileLayerTheWrappedSchema()
    {
        using var temporary = new TemporaryDirectory();
        using var map = WorldMap.Create(Dark, new GeoCoordinate(0, 0), 4891.96);

        MapBasemap.Apply(map, "positron", temporary.DirectoryPath);

        var layer = Assert.IsType<TileLayer>(map.Layers.Single(item => item.Name == MapBasemap.LayerName));
        Assert.IsType<WrappedTileSource>(layer.TileSource);
        Assert.Same(MapBasemap.WrappedSchema, layer.TileSource.Schema);
        Assert.Equal("Positron", layer.TileSource.Name);
    }

    [Fact]
    public async Task Tiles_ShowTheRealTilesOnBothSidesAndAreFetchedOnce()
    {
        using var temporary = new TemporaryDirectory();
        var handler = new StubHttpMessageHandler(TileResponse);
        using var client = new HttpClient(handler);
        var source = new WrappedTileSource(
            MapBasemap.CreateTileSource(MapBasemap.Find("basic"), temporary.DirectoryPath),
            MapBasemap.WrappedSchema);
        using var map = new Map { CRS = "EPSG:3857", BackColor = Mapsui.Styles.Color.Black };
        var layer = new TileLayer(source, httpClient: client) { Name = MapBasemap.LayerName };
        map.Layers.Add(layer);
        map.Navigator.Limiter = new WrappingViewportLimiter();
        map.Navigator.SetSize(800, 200);

        foreach (var centerX in new[] { Edge, -Edge })
        {
            map.Navigator.SetViewport(new Viewport(centerX, 0, ShallowResolution, 0, 800, 200));
            await FetchAsync(map, layer);

            using var bitmap = Render(new WrappedMapRenderer(), map, map.Navigator.Viewport);

            // Level 3 of the tiles has 8 columns: the last one sits west of the
            // date line and the first one east of it, in either copy of the seam.
            Assert.Equal(SKColors.Blue, bitmap.GetPixel(300, 100));
            Assert.Equal(SKColors.Red, bitmap.GetPixel(500, 100));
        }

        // Both ends of the world asked for the same tiles, which came from disk.
        // Level 0 is left out: its one tile fills two columns of a single view.
        var requests = handler.Requests.Where(request => request.Contains("/3/")).ToArray();
        Assert.NotEmpty(requests);
        Assert.Equal(requests.Length, requests.Distinct().Count());
        Assert.DoesNotContain(handler.Requests, request => request.Contains("/-") || request.Contains("/3/8/"));
    }

    private static HttpResponseMessage TileResponse(HttpRequestMessage request)
    {
        var segments = request.RequestUri!.AbsolutePath.Split('/');
        var level = int.Parse(segments[^3]);
        var column = int.Parse(segments[^2]);
        var color = column == 0 ? SKColors.Red : column == (1 << level) - 1 ? SKColors.Blue : SKColors.Gray;

        using var bitmap = new SKBitmap(MapBasemap.TileSize, MapBasemap.TileSize);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data.ToArray()) };
    }

    private static async Task FetchAsync(Map map, TileLayer layer)
    {
        map.RefreshData();
        for (var wait = 0; wait < 500; wait++)
        {
            await Task.Delay(10);
            if (!layer.Busy)
            {
                return;
            }
        }

        Assert.Fail("The tiles did not arrive.");
    }

    private static void Settle(Navigator navigator)
    {
        while (navigator.UpdateAnimations())
        {
            Thread.Sleep(10);
        }

        navigator.UpdateAnimations();
    }

    private static Viewport Viewport(double centerX, double resolution) =>
        new(centerX, 0, resolution, 0, 400, 300);

    private static SKBitmap Render(Mapsui.Rendering.IMapRenderer renderer, Map map, Viewport viewport)
    {
        var bitmap = new SKBitmap((int)viewport.Width, (int)viewport.Height);
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(canvas, viewport, map.Layers.ToArray(), map.Widgets, map.RenderService, map.BackColor);
        return bitmap;
    }

    private static AircraftMarker Marker(MPoint position) => new()
    {
        WorldX = position.X,
        WorldY = position.Y,
        HasPosition = true,
        Glyph = AircraftGlyph.MediumTwin,
        Size = 30,
        BoxSize = 35,
        Color = 0xFFFFFFFF
    };

    private static IvaoAtc Sector(params (double Latitude, double Longitude)[] polygon) => new()
    {
        Callsign = "TEST_CTR",
        AtcSession = new IvaoAtcSession { Position = "CTR", Frequency = 128 },
        AtcPosition = new IvaoAtcPosition
        {
            Position = "CTR",
            AtcCallsign = "TEST_CTR",
            RegionMapPolygon = polygon.Select(point => new IvaoCoordinate(point.Latitude, point.Longitude)).ToList()
        }
    };

    /// <summary>Counts the frames that had to paint the backdrop.</summary>
    private sealed class CountingLayer() : BaseLayer(IvaoMapLayers.AtcStationsLayerName)
    {
        public int Queries { get; private set; }

        public override IEnumerable<IFeature> GetFeatures(MRect box, double resolution)
        {
            Queries++;
            return [];
        }
    }
}
