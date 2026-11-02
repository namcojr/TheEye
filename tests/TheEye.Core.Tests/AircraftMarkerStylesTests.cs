using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Services;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

/// <summary>
/// The colours of an aircraft pin follow the basemap, not the shell theme: the
/// theme paints the offline map, but a raster style paints itself, so a pin in
/// the theme's text colour can vanish the moment the tiles are switched.
/// </summary>
public sealed class AircraftMarkerStylesTests
{
    private static readonly MapPalette DarkTheme = new(
        Water: "#0B1721",
        Land: "#1B2A35",
        Border: "#4A6070",
        Label: "#6FE3F5",
        Accent: "#38BDF8",
        IsDark: true);

    private static readonly MapPalette LightTheme = new(
        Water: "#DCE9F5",
        Land: "#F4F7FA",
        Border: "#B4C3D1",
        Label: "#1F2933",
        Accent: "#1D6FB8",
        IsDark: false);

    [Theory]
    [InlineData("dark-matter")]
    [InlineData("satellite-hybrid")]
    [InlineData("  Dark-Matter  ")]
    public void IconColor_DarkBasemap_DrawsLightArtwork(string style)
    {
        Assert.Equal(AircraftMarkerStyles.LightIcon, AircraftMarkerStyles.IconColor(LightTheme, style));
        Assert.Equal(AircraftMarkerStyles.LightIcon, AircraftMarkerStyles.IconColor(DarkTheme, style));
        Assert.True(AircraftMarkerStyles.IsDarkMap(LightTheme, style));
    }

    [Theory]
    [InlineData("basic")]
    [InlineData("osm-bright")]
    [InlineData("osm-liberty")]
    [InlineData("openstreetmap")]
    [InlineData("positron")]
    [InlineData("voyager")]
    [InlineData("toner")]
    public void IconColor_LightBasemap_DrawsDarkArtwork(string style)
    {
        Assert.Equal(AircraftMarkerStyles.DarkIcon, AircraftMarkerStyles.IconColor(DarkTheme, style));
        Assert.Equal(AircraftMarkerStyles.DarkIcon, AircraftMarkerStyles.IconColor(LightTheme, style));
        Assert.False(AircraftMarkerStyles.IsDarkMap(DarkTheme, style));
    }

    [Theory]
    [InlineData("Offline")]
    [InlineData("offline")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-style")]
    public void IconColor_OfflineStyle_FollowsThePaletteThatPaintsIt(string? style)
    {
        // The offline map is drawn with the theme palette, so there the theme is
        // the map and an unknown style falls back to the same answer.
        Assert.Equal(AircraftMarkerStyles.LightIcon, AircraftMarkerStyles.IconColor(DarkTheme, style));
        Assert.Equal(AircraftMarkerStyles.DarkIcon, AircraftMarkerStyles.IconColor(LightTheme, style));
    }

    [Fact]
    public void IconColors_AreDistinctHexValues()
    {
        Assert.NotEqual(AircraftMarkerStyles.LightIcon, AircraftMarkerStyles.DarkIcon);
        Assert.Matches("^#[0-9A-F]{6}$", AircraftMarkerStyles.LightIcon);
        Assert.Matches("^#[0-9A-F]{6}$", AircraftMarkerStyles.DarkIcon);
    }

    [Fact]
    public void PinsStartWhiteOnTheDarkOfflineMap()
    {
        using var images = new Images();
        var item = new PilotItem(new IvaoPilot(), images, images);

        Assert.Equal(AircraftMarkerStyles.LightIcon, item.IconColor);
    }

    [Fact]
    public void MapRepaintsTheTrafficWhenTheBasemapChanges()
    {
        using var temporary = new TemporaryDirectory();
        using var map = new MapViewModel(new Images(), basemapCacheDirectory: temporary.DirectoryPath);
        map.Initialize(DarkTheme, new GeoCoordinate(50, 0), 10000);
        map.Map!.Navigator.SetSize(800, 600);
        map.UpdateTraffic(Snapshot());

        var item = map.Pilots.Single();
        Assert.Equal(AircraftMarkerStyles.LightIcon, item.IconColor);

        // A light raster style brings dark artwork, a dark one takes it back.
        map.MapStyle = "positron";
        Assert.Equal(AircraftMarkerStyles.DarkIcon, item.IconColor);

        map.MapStyle = "dark-matter";
        Assert.Equal(AircraftMarkerStyles.LightIcon, item.IconColor);

        // Traffic that arrives later is painted with the style in force.
        map.UpdateTraffic(Snapshot(second: true));
        Assert.All(map.Pilots, pilot => Assert.Equal(AircraftMarkerStyles.LightIcon, pilot.IconColor));
    }

    [Fact]
    public void MapRepaintsTheTrafficWhenTheThemeTurnsTheOfflineMapLight()
    {
        using var map = new MapViewModel(new Images());
        map.Initialize(DarkTheme, new GeoCoordinate(50, 0), 10000);
        map.Map!.Navigator.SetSize(800, 600);
        map.UpdateTraffic(Snapshot());

        var item = map.Pilots.Single();
        Assert.Equal(AircraftMarkerStyles.LightIcon, item.IconColor);

        map.ApplyTheme(LightTheme);

        Assert.Equal(AircraftMarkerStyles.DarkIcon, item.IconColor);
    }

    private static TrackerSnapshot Snapshot(bool second = false)
    {
        var first = new IvaoPilot
        {
            Id = 1,
            LastTrack = new IvaoLastTrack { Latitude = 50, Longitude = 0 },
            FlightPlan = new IvaoFlightPlan { AircraftId = "B738" }
        };

        return second
            ? new TrackerSnapshot(
                [first, new IvaoPilot { Id = 2, LastTrack = first.LastTrack }],
                [],
                [],
                new IvaoConnectionStats(),
                DateTimeOffset.UtcNow,
                [])
            : new TrackerSnapshot([first], [], [], new IvaoConnectionStats(), DateTimeOffset.UtcNow, []);
    }

    private sealed class Images : IImagePipeline, IAircraftIconCache, IImageCache
    {
        public IAircraftIconCache AircraftIcons => this;
        public IImageCache SessionImages => this;
        public object Get(AircraftGlyph glyph) => glyph;
        public object? TryGet(string? url) => null;
        public Task<object?> GetAsync(string? url, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);
        public void Dispose() { }
    }
}
