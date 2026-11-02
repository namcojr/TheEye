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
    [InlineData(39, 39)]
    [InlineData(40, 40)]
    [InlineData(41, 40)]
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
        Assert.Equal(TheEyeSettings.DefaultAircraftGlyphSize, original.AircraftGlyphSize);
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

        // Hovering grows the artwork instead of drawing a halo around it, so the
        // hit area and the position of the pin stay what they were.
        Assert.Equal(size + 2, Grow(item, hovered: true));
        Assert.Equal(size, Grow(item, hovered: false));
        item.IsSelected = true;
        Assert.Equal(size + 3, item.DisplayGlyphSize);
        Assert.Equal(size + 5, Grow(item, hovered: true));
        Assert.True(item.MarkerSize >= item.DisplayGlyphSize);
        Assert.Same(icon, item.Icon);
        item.IsSelected = false;
        Assert.Equal(size + 2, item.DisplayGlyphSize);
        Assert.Equal(size, Grow(item, hovered: false));

        // The artwork of a pin that is both selected and hovered still fits the
        // box the pin was laid out with, so growing never clips or shifts it.
        item.IsSelected = true;
        Grow(item, hovered: true);
        Assert.True(item.MarkerSize >= item.DisplayGlyphSize);
    }

    private static int Grow(PilotItem item, bool hovered)
    {
        item.IsHovered = hovered;
        return item.DisplayGlyphSize;
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

    [Fact]
    public void MapHoverGrowsOnlyTheHoveredPin()
    {
        using var map = new MapViewModel(new Images());
        var palette = new MapPalette("#16324F", "#1B2A35", "#2F4858", "#6FE3F5", "#4CC9F0", true);
        map.Initialize(palette, new GeoCoordinate(50, 0), 10000);
        map.Map!.Navigator.SetSize(800, 600);
        var pilots = new[]
        {
            new IvaoPilot { Id = 1, LastTrack = new IvaoLastTrack { Latitude = 50, Longitude = 0 } },
            new IvaoPilot { Id = 2, LastTrack = new IvaoLastTrack { Latitude = 51, Longitude = 1 } }
        };
        var snapshot = new TheEye.Core.Services.TrackerSnapshot(
            pilots, [], [], new IvaoConnectionStats(), DateTimeOffset.UtcNow, []);
        map.UpdateTraffic(snapshot);
        var first = map.Pilots.Single(item => item.Id == 1);
        var second = map.Pilots.Single(item => item.Id == 2);
        var size = first.DisplayGlyphSize;

        map.SetHoveredPilot(first);
        Assert.True(first.IsHovered);
        Assert.Equal(size + 2, first.DisplayGlyphSize);
        Assert.Equal(size, second.DisplayGlyphSize);

        // Moving the pointer to the other pin restores the one it left.
        map.SetHoveredPilot(second);
        Assert.False(first.IsHovered);
        Assert.Equal(size, first.DisplayGlyphSize);
        Assert.Equal(size + 2, second.DisplayGlyphSize);

        // The selected aircraft keeps the size of its selection while hovered.
        map.SelectPilot(second, center: false);
        Assert.False(second.IsHovered);
        Assert.Equal(size + 3, second.DisplayGlyphSize);
        map.SetHoveredPilot(second);
        Assert.Equal(size + 3, second.DisplayGlyphSize);

        map.SetHoveredPilot(null);
        Assert.False(first.IsHovered);
        Assert.False(second.IsHovered);
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
