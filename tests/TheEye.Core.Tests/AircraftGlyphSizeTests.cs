using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Settings;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

public sealed class AircraftGlyphSizeTests
{
    [Theory]
    [InlineData(19, 20)]
    [InlineData(20, 20)]
    [InlineData(21, 21)]
    [InlineData(30, 30)]
    [InlineData(31, 30)]
    public void SettingsNormalizeCloneAndCopySize(int value, int expected)
    {
        var settings = new TheEyeSettings { AircraftGlyphSize = value };
        settings.Normalize();
        Assert.Equal(expected, settings.AircraftGlyphSize);
        Assert.Equal(expected, settings.Clone().AircraftGlyphSize);
        var copy = new TheEyeSettings();
        copy.CopyFrom(settings);
        Assert.Equal(expected, copy.AircraftGlyphSize);
    }

    [Fact]
    public void OptionsDraftDoesNotChangeOriginalUntilSaved()
    {
        var original = new TheEyeSettings();
        var options = new SettingsViewModel(original.Clone());
        options.AircraftGlyphSize = 30;
        Assert.Equal("30 px", options.AircraftGlyphSizeLabel);
        Assert.Equal(21, original.AircraftGlyphSize);
        Assert.Equal(30, options.CreateSettings().AircraftGlyphSize);
    }

    [Theory]
    [InlineData(20, 26)]
    [InlineData(21, 26)]
    [InlineData(30, 35)]
    public void MarkerAndSelectionSizesContainRotatingArtwork(int size, int markerSize)
    {
        using var images = new Images();
        var item = new PilotItem(new IvaoPilot(), images, images) { GlyphSize = size };
        var icon = item.Icon;
        Assert.Equal(size, item.DisplayGlyphSize);
        Assert.Equal(markerSize, item.MarkerSize);
        Assert.Equal(markerSize - 1, item.HaloSize);
        item.IsSelected = true;
        Assert.Equal(size + 3, item.DisplayGlyphSize);
        Assert.True(item.MarkerSize > item.DisplayGlyphSize);
        Assert.Same(icon, item.Icon);
        item.IsSelected = false;
        Assert.Equal(size, item.DisplayGlyphSize);
    }

    [Fact]
    public void MapResizesAroundCoordinatesAndSizesNewTraffic()
    {
        using var map = new MapViewModel(new Images());
        var palette = new MapPalette("#16324F", "#1B2A35", "#2F4858", "#6FE3F5", "#4CC9F0", true);
        map.Initialize(palette, new GeoCoordinate(50, 0), 10000);
        map.Map!.Navigator.SetSize(800, 600);
        var pilot = new IvaoPilot
        {
            Id = 1,
            LastTrack = new IvaoLastTrack { Latitude = 50, Longitude = 0 },
            FlightPlan = new IvaoFlightPlan { AircraftId = "B738" }
        };
        var snapshot = new TheEye.Core.Services.TrackerSnapshot(
            [pilot], [], [], new IvaoConnectionStats(), DateTimeOffset.UtcNow, []);
        map.UpdateTraffic(snapshot);
        var item = map.Pilots.Single();
        var centreX = item.X + item.MarkerSize / 2d;
        var centreY = item.Y + item.MarkerSize / 2d;
        map.AircraftGlyphSize = 30;
        Assert.Equal(centreX, item.X + item.MarkerSize / 2d, 6);
        Assert.Equal(centreY, item.Y + item.MarkerSize / 2d, 6);
        Assert.Same(item, map.HitTestPilot(new Mapsui.Manipulations.ScreenPosition(centreX, centreY)));
        map.UpdateTraffic(snapshot with { Pilots = [pilot, new IvaoPilot { Id = 2, LastTrack = pilot.LastTrack }] });
        Assert.All(map.Pilots, marker => Assert.Equal(30, marker.GlyphSize));
    }

    [Fact]
    public void MapAppliesSizeToExistingMarkersAndKeepsHitTestCentred()
    {
        using var map = new MapViewModel(new Images());
        using var images = new Images();
        var item = new PilotItem(new IvaoPilot(), images, images);
        map.Pilots.Add(item);
        map.AircraftGlyphSize = 30;
        Assert.Equal(30, item.GlyphSize);
        item.SetScreenPosition(100, 200, true);
        Assert.Same(item, map.HitTestPilot(new Mapsui.Manipulations.ScreenPosition(117.5, 217.5)));
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
