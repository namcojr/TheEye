using BruTile;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Tiling.Layers;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Settings;

namespace TheEye.Core.Tests;

/// <summary>
/// The online basemap is a diagnostic feature: it must line up with the offline
/// map's zoom ladder, ask IVAO's proxy the way WebEye does, and disappear again
/// without disturbing anything drawn on top of it.
/// </summary>
public sealed class MapBasemapTests
{
    private static readonly MapPalette Palette = new("#0B1721", "#1B2A35", "#4A6070", "#6FE3F5", "#38BDF8", true);

    [Fact]
    public void Styles_StartWithTheOfflineMapAndUseTheProxyStyleIds()
    {
        Assert.Equal(MapBasemap.Offline, MapBasemap.Styles[0].Id);
        Assert.Equal(
            [
                "Offline",
                "basic",
                "osm-bright",
                "osm-liberty",
                "openstreetmap",
                "positron",
                "voyager",
                "dark-matter",
                "toner",
                "satellite-hybrid"
            ],
            MapBasemap.Styles.Select(style => style.Id));
    }

    [Theory]
    [InlineData("dark-matter", "dark-matter")]
    [InlineData("  Dark-Matter  ", "dark-matter")]
    [InlineData("POSITRON", "positron")]
    [InlineData("offline", "Offline")]
    [InlineData("", "Offline")]
    [InlineData("   ", "Offline")]
    [InlineData(null, "Offline")]
    [InlineData("osm-bright/../../etc", "Offline")]
    public void Normalize_ReturnsTheStoredIdAndFallsBackToTheOfflineMap(string? style, string expected) =>
        Assert.Equal(expected, MapBasemap.Normalize(style));

    [Fact]
    public void IsKnown_AcceptsTheStyleIdsAndNothingElse()
    {
        Assert.True(MapBasemap.IsKnown("Offline"));
        Assert.True(MapBasemap.IsKnown(" satellite-hybrid "));
        Assert.False(MapBasemap.IsKnown(null));
        Assert.False(MapBasemap.IsKnown(string.Empty));
        Assert.False(MapBasemap.IsKnown("hybrid"));
    }

    [Fact]
    public void Schema_RequestsOneProviderLevelBelowTheViewLevel()
    {
        // A provider level is a 512 px tile, so it covers the ground of a 256 px
        // tile one level deeper: view level n has to ask for {z} = n - 1.
        for (var viewLevel = WorldMap.MinZoomLevel; viewLevel <= WorldMap.DeepestZoomLevel; viewLevel++)
        {
            var resolution = WorldMap.MaxResolution / Math.Pow(2, viewLevel);
            Assert.Equal(viewLevel - 1, Utilities.GetNearestLevel(MapBasemap.Schema.Resolutions, resolution));
        }
    }

    [Fact]
    public void Schema_UsesSquare512PixelTilesAndCoversTheWholeWorld()
    {
        Assert.Equal(WorldMap.DeepestZoomLevel - 1, MapBasemap.Schema.Resolutions.Keys.Max());

        foreach (var level in MapBasemap.Schema.Resolutions.Keys)
        {
            Assert.Equal(MapBasemap.TileSize, MapBasemap.Schema.GetTileWidth(level));
            Assert.Equal(MapBasemap.TileSize, MapBasemap.Schema.GetTileHeight(level));
            Assert.Equal(1L << level, MapBasemap.Schema.GetMatrixWidth(level));
            Assert.Equal(1L << level, MapBasemap.Schema.GetMatrixHeight(level));
        }
    }

    [Fact]
    public void Schema_SpansThePannableWorldWithTheTopRowInTheNorth()
    {
        Assert.Equal(-20037508.34, MapBasemap.Schema.Extent.MinX, 6);
        Assert.Equal(-20037508.34, MapBasemap.Schema.Extent.MinY, 6);
        Assert.Equal(20037508.34, MapBasemap.Schema.Extent.MaxX, 6);
        Assert.Equal(20037508.34, MapBasemap.Schema.Extent.MaxY, 6);

        // OSM numbering starts at the north west corner, the corner the navigator
        // pans within.
        Assert.Equal(-20037508.34, MapBasemap.Schema.GetOriginX(0), 6);
        Assert.Equal(20037508.34, MapBasemap.Schema.GetOriginY(0), 6);
        Assert.Equal(YAxis.OSM, MapBasemap.Schema.YAxis);
    }

    [Fact]
    public void TileSource_AddressesTheStyleThroughTheProxy()
    {
        using var temporary = new TemporaryDirectory();
        var source = MapBasemap.CreateTileSource(MapBasemap.Find("dark-matter"), temporary.DirectoryPath);

        var url = source.GetUrl(new TileInfo { Index = new TileIndex(col: 3, row: 2, level: 11) });

        Assert.Equal("https://maps-maptiler.ivao.aero/api/maps/dark-matter/11/3/2.webp", url.ToString());
        Assert.Equal("Dark Matter", source.Name);
        Assert.Equal("© MapTiler © OpenStreetMap contributors", source.Attribution.Text);
        Assert.Equal(MapBasemap.TileSize, source.Schema.GetTileWidth(11));

        // Every style keeps its tiles apart, so switching back and forth cannot
        // serve one style's picture for another.
        Assert.True(Directory.Exists(Path.Combine(temporary.DirectoryPath, "dark-matter")));
    }

    [Fact]
    public void TileSource_IdentifiesTheAppAndWebEyeOnEveryRequest()
    {
        using var temporary = new TemporaryDirectory();
        var source = MapBasemap.CreateTileSource(MapBasemap.Find("basic"), temporary.DirectoryPath);
        using var request = new HttpRequestMessage();

        source.ConfigureHttpRequestMessage!(request);

        // IVAO's proxy redirects a request that does not come from WebEye, and
        // BruTile refuses to send one without an application specific agent.
        Assert.Equal("https://webeye.ivao.aero/", request.Headers.Referrer?.ToString());
        Assert.Contains(
            request.Headers.UserAgent,
            agent => agent.Product is { Name: "TheEye", Version: "1.0" });
    }

    [Fact]
    public void Apply_DrawsTheTilesUnderEverythingAndOfflineBringsTheCountryMapBack()
    {
        using var temporary = new TemporaryDirectory();
        using var map = WorldMap.Create(Palette, new GeoCoordinate(0, 0), WorldMap.MaxResolution / 8);

        MapBasemap.Apply(map, "dark-matter", temporary.DirectoryPath);

        var basemap = map.Layers.First();
        Assert.Equal(MapBasemap.LayerName, basemap.Name);
        Assert.IsType<TileLayer>(basemap);
        Assert.False(Layer(map, WorldMap.CountriesLayerName).Enabled);
        Assert.False(Layer(map, WorldMap.CountryLabelsLayerName).Enabled);

        MapBasemap.Apply(map, MapBasemap.Offline, temporary.DirectoryPath);

        Assert.DoesNotContain(map.Layers, layer => layer.Name == MapBasemap.LayerName);
        Assert.True(Layer(map, WorldMap.CountriesLayerName).Enabled);
        Assert.True(Layer(map, WorldMap.CountryLabelsLayerName).Enabled);
        Assert.Equal(WorldMap.CountriesLayerName, map.Layers.First().Name);
    }

    [Fact]
    public void Apply_ReplacesAnEarlierStyleInsteadOfStackingTiles()
    {
        using var temporary = new TemporaryDirectory();
        using var map = WorldMap.Create(Palette, new GeoCoordinate(0, 0), WorldMap.MaxResolution / 8);

        MapBasemap.Apply(map, "basic", temporary.DirectoryPath);
        MapBasemap.Apply(map, "dark-matter", temporary.DirectoryPath);

        var basemap = Assert.IsType<TileLayer>(
            Assert.Single(map.Layers, layer => layer.Name == MapBasemap.LayerName));
        Assert.Equal("Dark Matter", basemap.TileSource.Name);

        MapBasemap.Apply(map, MapBasemap.Offline, temporary.DirectoryPath);
    }

    [Fact]
    public void Settings_KeepTheMapStyleAndFallBackToOfflineWhenUnknown()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        var settings = new TheEyeSettings { MapStyle = "Dark-Matter" };

        store.Save(settings);

        Assert.Equal("dark-matter", store.Load().MapStyle);

        settings.MapStyle = "not-a-style";
        settings.Normalize();

        Assert.Equal(MapBasemap.Offline, settings.MapStyle);
    }

    private static ILayer Layer(Map map, string name) =>
        map.Layers.First(layer => layer.Name == name);
}
