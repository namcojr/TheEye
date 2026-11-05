using TheEye.Core.Maps;
using TheEye.Core.Settings;

namespace TheEye.Core.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void Load_ReturnsDefaultsWhenFileDoesNotExist()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));

        var settings = store.Load();

        Assert.Equal(TheEyeThemes.Dark, settings.Theme);
        Assert.Equal(30, settings.RefreshSeconds);
        Assert.Equal(TheEyeSettings.DefaultAircraftGlyphSize, settings.AircraftGlyphSize);
        Assert.True(settings.ShowPilots);
        Assert.True(settings.ShowAtc);
        Assert.True(settings.ShowAirports);
        Assert.True(settings.CenterOnMapClick);
        Assert.Equal(45, settings.MapLatitude);
        Assert.Equal(10, settings.MapLongitude);
        Assert.Equal(WorldMap.MaxZoomOutResolution, settings.MapResolution);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsFriendIds()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        var settings = new TheEyeSettings();

        Assert.True(settings.AddFriend(90210));
        Assert.True(settings.AddFriend(12345));
        Assert.False(settings.AddFriend(90210));
        Assert.False(settings.AddFriend(0));
        Assert.Equal([12345L, 90210L], settings.FriendIds);

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal([12345L, 90210L], loaded.FriendIds);
        Assert.True(loaded.IsFriend(12345));
        Assert.False(loaded.IsFriend(54321));

        // Toggling reports whether the member is followed afterwards, which is
        // what the button in the traffic lists shows.
        Assert.True(loaded.ToggleFriend(54321));
        Assert.True(loaded.IsFriend(54321));
        Assert.False(loaded.ToggleFriend(12345));
        Assert.False(loaded.IsFriend(12345));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTheTrafficColumnWidth()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        var settings = new TheEyeSettings { TrafficColumnWidth = 410 };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(TheEyeSettings.DefaultTrafficColumnWidth, new TheEyeSettings().TrafficColumnWidth);
        Assert.Equal(410, loaded.TrafficColumnWidth);
        Assert.Equal(410, settings.Clone().TrafficColumnWidth);
    }

    [Theory]
    [InlineData(0d, TheEyeSettings.MinTrafficColumnWidth)]
    [InlineData(199d, TheEyeSettings.MinTrafficColumnWidth)]
    [InlineData(9000d, TheEyeSettings.MaxTrafficColumnWidth)]
    [InlineData(double.NaN, TheEyeSettings.DefaultTrafficColumnWidth)]
    [InlineData(300d, 300d)]
    public void Normalize_ClampsTheTrafficColumnWidth(double width, double expected)
    {
        var settings = new TheEyeSettings { TrafficColumnWidth = width };

        settings.Normalize();

        Assert.Equal(expected, settings.TrafficColumnWidth);
    }

    [Theory]
    [InlineData("small", "Small", 1d)]
    [InlineData("MEDIUM", "Medium", 1.16d)]
    [InlineData("Large", "Large", 1.24d)]
    [InlineData("unknown", "Small", 1d)]
    public void FontSize_NormalizesAndCopies(string input, string expected, double scale)
    {
        var settings = new TheEyeSettings { UiFontSize = input };
        settings.Normalize();

        Assert.Equal(expected, settings.UiFontSize);
        Assert.Equal(scale, UiFontSizes.Multiplier(settings.UiFontSize));
        Assert.Equal(expected, settings.Clone().UiFontSize);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsUiFontSize()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        var settings = new TheEyeSettings { UiFontSize = UiFontSizes.Large };

        store.Save(settings);

        Assert.Equal(UiFontSizes.Large, store.Load().UiFontSize);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsNormalizedSettings()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        var settings = new TheEyeSettings
        {
            Theme = "forest",
            RefreshSeconds = 120,
            AircraftGlyphSize = 30,
            ShowPilots = false,
            ShowAtc = false,
            ShowAirports = false,
            CenterOnMapClick = false,
            MapLatitude = 52.31,
            MapLongitude = 4.76,
            MapResolution = 5000
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(TheEyeThemes.Forest, loaded.Theme);
        Assert.Equal(120, loaded.RefreshSeconds);
        Assert.Equal(30, loaded.AircraftGlyphSize);
        Assert.False(loaded.ShowPilots);
        Assert.False(loaded.ShowAtc);
        Assert.False(loaded.ShowAirports);
        Assert.False(loaded.CenterOnMapClick);
        Assert.Equal(52.31, loaded.MapLatitude);
        Assert.Equal(4.76, loaded.MapLongitude);
        Assert.Equal(5000, loaded.MapResolution);

        var json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"theme\": \"Forest\"", json);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"centerOnMapClick\": false", json);
    }

    [Fact]
    public void ExistingSettingsWithoutMapClickOptionKeepCentringEnabled()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.FilePath("settings.json");
        File.WriteAllText(path, """
            {
              "apiKey": "legacy-key",
              "theme": "light",
              "refreshSeconds": 60,
              "showPilots": true,
              "showAtc": false,
              "mapLatitude": 52.31,
              "mapLongitude": 4.76,
              "mapResolution": 5000
            }
            """);
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.True(settings.CenterOnMapClick);
        Assert.Equal(TheEyeSettings.DefaultAircraftGlyphSize, settings.AircraftGlyphSize);
        var savedJson = System.Text.Json.JsonSerializer.Serialize(settings);
        Assert.DoesNotContain("apiKey", savedJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CloneAndCopyFrom_CarryMapDisplayOptions()
    {
        var original = new TheEyeSettings { CenterOnMapClick = false, ShowAirports = false };

        var clone = original.Clone();
        clone.CenterOnMapClick = true;
        clone.ShowAirports = true;

        Assert.False(original.CenterOnMapClick);
        Assert.False(original.ShowAirports);
        Assert.True(clone.CenterOnMapClick);
        Assert.True(clone.ShowAirports);

        original.CopyFrom(clone);

        Assert.True(original.CenterOnMapClick);
        Assert.True(original.ShowAirports);
    }

    [Fact]
    public void Load_CorruptJsonFallsBackToDefaults()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.FilePath("settings.json");
        File.WriteAllText(path, "{ this is not json");
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal(TheEyeThemes.Dark, settings.Theme);
        Assert.Equal(30, settings.RefreshSeconds);
    }

    [Fact]
    public void Load_NormalizesOutOfRangeValues()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.FilePath("settings.json");
        File.WriteAllText(path, """
            {
              "apiKey": "  legacy-key  ",
              "theme": "neon",
              "refreshSeconds": 2,
              "mapLatitude": 99,
              "mapLongitude": -500,
              "mapResolution": 0.5
            }
            """);
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal(TheEyeThemes.Dark, settings.Theme);
        Assert.Equal(30, settings.RefreshSeconds);
        Assert.Equal(45, settings.MapLatitude);
        Assert.Equal(10, settings.MapLongitude);
        Assert.Equal(WorldMap.MaxZoomOutResolution, settings.MapResolution);
    }

    [Theory]
    [InlineData(9.55)]
    [InlineData(38.2)]
    [InlineData(WorldMap.MinResolution)]
    [InlineData(WorldMap.MaxZoomOutResolution)]
    public void Load_KeepsResolutionsInsideTheMapZoomRange(double resolution)
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        store.Save(new TheEyeSettings { MapResolution = resolution });

        var settings = store.Load();

        Assert.Equal(resolution, settings.MapResolution);
    }

    [Fact]
    public void Save_NormalizesValuesBeforeWriting()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        var settings = new TheEyeSettings
        {
            Theme = "LIGHT",
            RefreshSeconds = 5,
            MapLatitude = double.NaN,
            MapLongitude = 181,
            MapResolution = 999999
        };

        store.Save(settings);

        Assert.Equal(TheEyeThemes.Light, settings.Theme);
        Assert.Equal(30, settings.RefreshSeconds);
        Assert.Equal(45, settings.MapLatitude);
        Assert.Equal(10, settings.MapLongitude);
        Assert.Equal(WorldMap.MaxZoomOutResolution, settings.MapResolution);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTheSignIn()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        var settings = new TheEyeSettings
        {
            OAuthClientId = "  client-id  ",
            OAuthClientSecret = " secret ",
            OAuthRedirectUri = "",
            OAuthScopes = " ",
            OAuthAccessToken = " access ",
            OAuthRefreshToken = "refresh",
            OAuthAccount = "123456 (Jane Doe)"
        };
        settings.SetOAuthExpiry(new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero));

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("client-id", loaded.OAuthClientId);
        Assert.Equal("secret", loaded.OAuthClientSecret);
        Assert.Equal(TheEye.Core.Api.IvaoOAuthOptions.DefaultRedirectUri, loaded.OAuthRedirectUri);
        Assert.Equal(TheEye.Core.Api.IvaoOAuthOptions.DefaultScopes, loaded.OAuthScopes);
        Assert.Equal("access", loaded.OAuthAccessToken);
        Assert.Equal("refresh", loaded.OAuthRefreshToken);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero), loaded.GetOAuthExpiry());
        Assert.Equal("123456 (Jane Doe)", loaded.OAuthAccount);
        Assert.True(loaded.HasOAuthTokens);
    }

    [Fact]
    public void ClearOAuthTokens_DropsTheSignIn()
    {
        var settings = new TheEyeSettings
        {
            OAuthClientId = "client-id",
            OAuthAccessToken = "access",
            OAuthRefreshToken = "refresh",
            OAuthAccount = "123456 (Jane Doe)"
        };
        settings.SetOAuthExpiry(DateTimeOffset.UtcNow.AddHours(1));

        settings.ClearOAuthTokens();

        Assert.False(settings.HasOAuthTokens);
        Assert.Null(settings.GetOAuthExpiry());
        Assert.Equal(string.Empty, settings.OAuthAccount);
        Assert.Equal("client-id", settings.OAuthClientId);
    }

    [Fact]
    public void AircraftHighlightFor_PrefersTheOwnColourOverTheFriendColour()
    {
        var settings = new TheEyeSettings
        {
            OAuthAccessToken = "access",
            OAuthAccount = "123456 (Jane Doe)",
            OwnAircraftColor = "Red",
            FriendAircraftColor = "Cyan"
        };
        settings.AddFriend(123456);
        settings.AddFriend(654321);

        Assert.Equal(123456, settings.GetOwnUserId());
        Assert.Equal("#EF4444", settings.AircraftHighlightFor(123456));
        Assert.Equal("#22D3EE", settings.AircraftHighlightFor(654321));
        Assert.Null(settings.AircraftHighlightFor(111111));

        settings.ClearOAuthTokens();
        Assert.Equal("#22D3EE", settings.AircraftHighlightFor(123456));
    }

    [Fact]
    public void Normalize_NeverLeavesTheOwnAndFriendColoursEqual()
    {
        var settings = new TheEyeSettings { OwnAircraftColor = "white", FriendAircraftColor = "WHITE" };

        settings.Normalize();

        Assert.Equal("White", settings.OwnAircraftColor);
        Assert.NotEqual(settings.OwnAircraftColor, settings.FriendAircraftColor);
        Assert.NotNull(AircraftHighlightColors.Find(settings.FriendAircraftColor));
    }

    [Theory]
    [InlineData(null, TheEyeThemes.Dark)]
    [InlineData("", TheEyeThemes.Dark)]
    [InlineData("dark", TheEyeThemes.Dark)]
    [InlineData("LIGHT", TheEyeThemes.Light)]
    [InlineData("midnightblue", TheEyeThemes.MidnightBlue)]
    [InlineData("unknown", TheEyeThemes.Dark)]
    public void ThemeNormalization_IsCaseInsensitiveAndSafe(string? theme, string expected)
    {
        Assert.Equal(expected, TheEyeThemes.Normalize(theme));
    }

    [Theory]
    [InlineData("Light", true)]
    [InlineData("sunset", true)]
    [InlineData("neon", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_RecognizesBundledThemes(string? theme, bool expected)
    {
        Assert.Equal(expected, TheEyeThemes.IsKnown(theme));
    }
}
