using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Settings;
using TheEye.Presentation.Services;
using TheEye.Presentation.Theming;
using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

/// <summary>
/// The traffic browser column is resizable in both shells and the width the user
/// drags is kept with the rest of the settings, so the next launch opens with the
/// same layout.
/// </summary>
public sealed class TrafficColumnWidthTests
{
    [Theory]
    [InlineData(TheEyeSettings.MinTrafficColumnWidth - 40, TheEyeSettings.MinTrafficColumnWidth)]
    [InlineData(TheEyeSettings.MaxTrafficColumnWidth + 40, TheEyeSettings.MaxTrafficColumnWidth)]
    [InlineData(double.NaN, TheEyeSettings.DefaultTrafficColumnWidth)]
    [InlineData(410, 410)]
    public void ViewModel_KeepsTheWidthInsideTheSplitterLimits(double width, double expected)
    {
        using var temporary = new TemporaryDirectory();
        var settings = new TheEyeSettings();
        using var viewModel = new MainViewModel(
            new SettingsStore(temporary.FilePath("settings.json")),
            settings,
            new Theme(),
            new Images());

        viewModel.TrafficColumnWidth = width;

        Assert.Equal(expected, viewModel.TrafficColumnWidth);
        Assert.Equal(expected, settings.TrafficColumnWidth);
    }

    [Fact]
    public void ViewModel_SavesTheWidthWithTheRestOfTheSettings()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        using var viewModel = new MainViewModel(store, new TheEyeSettings(), new Theme(), new Images())
        {
            TrafficColumnWidth = 396
        };

        viewModel.SaveSettings();

        Assert.Equal(396, store.Load().TrafficColumnWidth);
    }

    [Fact]
    public void ViewModel_StartsAtThePersistedWidth()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        store.Save(new TheEyeSettings { TrafficColumnWidth = 452 });

        using var viewModel = new MainViewModel(store, store.Load(), new Theme(), new Images());

        Assert.Equal(452, viewModel.TrafficColumnWidth);
    }

    private sealed class Theme : IThemeService
    {
        public event EventHandler? ThemeChanged;

        public string CurrentTheme => TheEyeThemes.Dark;

        public void Apply(string? theme) => ThemeChanged?.Invoke(this, EventArgs.Empty);

        public MapPalette CreateMapPalette() => new(
            Water: "#0B1721",
            Land: "#14222C",
            Border: "#25333D",
            Label: "#E2E8F0",
            Accent: "#38BDF8",
            IsDark: true);
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
