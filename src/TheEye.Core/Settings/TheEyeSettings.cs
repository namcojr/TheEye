namespace TheEye.Core.Settings;

using TheEye.Core.Maps;

public static class TheEyeThemes
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string MidnightBlue = "MidnightBlue";
    public const string Forest = "Forest";
    public const string Sunset = "Sunset";

    public static IReadOnlyList<string> All { get; } =
    [
        Dark,
        Light,
        MidnightBlue,
        Forest,
        Sunset
    ];

    public static bool IsKnown(string? theme) =>
        !string.IsNullOrWhiteSpace(theme) &&
        All.Contains(theme, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? theme) =>
        All.FirstOrDefault(t => string.Equals(t, theme, StringComparison.OrdinalIgnoreCase)) ?? Dark;
}

/// <summary>
/// User preferences persisted as JSON in %AppData%\TheEyeIVAO\settings.json.
/// Keeping this in the core library means a future Linux shell can use the
/// exact same configuration.
/// </summary>
public sealed class TheEyeSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public string Theme { get; set; } = TheEyeThemes.Dark;

    public int RefreshSeconds { get; set; } = 30;

    public bool ShowPilots { get; set; } = true;

    /// <summary>Draws every controller layer: sectors, sector labels and station badges.</summary>
    public bool ShowAtc { get; set; } = true;

    public double MapLatitude { get; set; } = 45;

    public double MapLongitude { get; set; } = 10;

    public double MapResolution { get; set; } = 25000;

    public TheEyeSettings Clone() => new()
    {
        ApiKey = ApiKey,
        Theme = Theme,
        RefreshSeconds = RefreshSeconds,
        ShowPilots = ShowPilots,
        ShowAtc = ShowAtc,
        MapLatitude = MapLatitude,
        MapLongitude = MapLongitude,
        MapResolution = MapResolution
    };

    public void CopyFrom(TheEyeSettings other)
    {
        ApiKey = other.ApiKey;
        Theme = other.Theme;
        RefreshSeconds = other.RefreshSeconds;
        ShowPilots = other.ShowPilots;
        ShowAtc = other.ShowAtc;
        MapLatitude = other.MapLatitude;
        MapLongitude = other.MapLongitude;
        MapResolution = other.MapResolution;
    }

    public void Normalize()
    {
        ApiKey = ApiKey?.Trim() ?? string.Empty;
        Theme = TheEyeThemes.Normalize(Theme);

        if (RefreshSeconds is < 10 or > 600)
        {
            RefreshSeconds = 30;
        }

        if (double.IsNaN(MapLatitude) || MapLatitude is < -85 or > 85)
        {
            MapLatitude = 45;
        }

        if (double.IsNaN(MapLongitude) || MapLongitude is < -180 or > 180)
        {
            MapLongitude = 10;
        }

        if (double.IsNaN(MapResolution) || MapResolution is < WorldMap.MinResolution or > WorldMap.MaxResolution)
        {
            MapResolution = 25000;
        }
    }
}
