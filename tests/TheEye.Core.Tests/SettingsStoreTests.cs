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

        Assert.Equal(string.Empty, settings.ApiKey);
        Assert.Equal(TheEyeThemes.Dark, settings.Theme);
        Assert.Equal(30, settings.RefreshSeconds);
        Assert.Equal(21, settings.AircraftGlyphSize);
        Assert.True(settings.ShowPilots);
        Assert.True(settings.ShowAtc);
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
    public void SaveAndLoad_RoundTripsNormalizedSettings()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(temporary.FilePath("settings.json"));
        var settings = new TheEyeSettings
        {
            ApiKey = "  test-key  ",
            Theme = "forest",
            RefreshSeconds = 120,
            AircraftGlyphSize = 30,
            ShowPilots = false,
            ShowAtc = false,
            CenterOnMapClick = false,
            MapLatitude = 52.31,
            MapLongitude = 4.76,
            MapResolution = 5000
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("test-key", loaded.ApiKey);
        Assert.Equal(TheEyeThemes.Forest, loaded.Theme);
        Assert.Equal(120, loaded.RefreshSeconds);
        Assert.Equal(30, loaded.AircraftGlyphSize);
        Assert.False(loaded.ShowPilots);
        Assert.False(loaded.ShowAtc);
        Assert.False(loaded.CenterOnMapClick);
        Assert.Equal(52.31, loaded.MapLatitude);
        Assert.Equal(4.76, loaded.MapLongitude);
        Assert.Equal(5000, loaded.MapResolution);

        var json = File.ReadAllText(store.FilePath);
        Assert.Contains("\"theme\": \"Forest\"", json);
        Assert.Contains("\"apiKey\": \"test-key\"", json);
        Assert.Contains("\"centerOnMapClick\": false", json);
    }

    [Fact]
    public void ExistingSettingsWithoutMapClickOptionKeepCentringEnabled()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.FilePath("settings.json");
        File.WriteAllText(path, """
            {
              "apiKey": "",
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
        Assert.Equal(21, settings.AircraftGlyphSize);
    }

    [Fact]
    public void CloneAndCopyFrom_CarryTheMapClickOption()
    {
        var original = new TheEyeSettings { CenterOnMapClick = false };

        var clone = original.Clone();
        clone.CenterOnMapClick = true;

        Assert.False(original.CenterOnMapClick);
        Assert.True(clone.CenterOnMapClick);

        original.CopyFrom(clone);

        Assert.True(original.CenterOnMapClick);
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
              "apiKey": "  abc  ",
              "theme": "neon",
              "refreshSeconds": 2,
              "mapLatitude": 99,
              "mapLongitude": -500,
              "mapResolution": 0.5
            }
            """);
        var store = new SettingsStore(path);

        var settings = store.Load();

        Assert.Equal("abc", settings.ApiKey);
        Assert.Equal(TheEyeThemes.Dark, settings.Theme);
        Assert.Equal(30, settings.RefreshSeconds);
        Assert.Equal(45, settings.MapLatitude);
        Assert.Equal(10, settings.MapLongitude);
        Assert.Equal(WorldMap.MaxZoomOutResolution, settings.MapResolution);
    }

    [Theory]
    [InlineData(2.39)]
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
            ApiKey = "  ",
            Theme = "LIGHT",
            RefreshSeconds = 5,
            MapLatitude = double.NaN,
            MapLongitude = 181,
            MapResolution = 999999
        };

        store.Save(settings);

        Assert.Equal(string.Empty, settings.ApiKey);
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
    public void ClearOAuthTokens_DropsTheSignInOnly()
    {
        var settings = new TheEyeSettings
        {
            ApiKey = "test-key",
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
        Assert.Equal("test-key", settings.ApiKey);
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
