using TheEye.Core.Maps;

namespace TheEye.Presentation.Theming;

/// <summary>
/// The colours of one shell theme. The values are the ones the WPF dictionaries
/// carry: a port of the Windows shell draws from the same table, so both look
/// identical and the preview tiles in Settings always match the real palette.
/// </summary>
public sealed record ThemePalette(
    string Name,
    string Label,
    string WindowBackground,
    string PanelBackground,
    string PanelAltBackground,
    string HeaderBackground,
    string Border,
    string Text,
    string MutedText,
    string Accent,
    string AccentHover,
    string AccentText,
    string Selection,
    string HoverBackground,
    string GridLine,
    string Success,
    string Warning,
    string Error,
    string MapWater,
    string MapLand,
    string MapBorder,
    string MapLabel,
    string Shadow)
{
    /// <summary>True for a theme whose background is dark, which the map uses for its labels.</summary>
    public bool IsDark => Luminance(WindowBackground) < 0.45;

    /// <summary>The palette the offline world map is drawn with.</summary>
    public MapPalette ToMapPalette() =>
        new(MapWater, MapLand, MapBorder, MapLabel, Accent, IsDark);

    private static double Luminance(string hex)
    {
        var (red, green, blue) = ThemeCatalog.ParseRgb(hex);
        return (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255d;
    }
}

/// <summary>The five themes the app ships, as data the shells can share.</summary>
public static class ThemeCatalog
{
    public static IReadOnlyList<ThemePalette> All { get; } =
    [
        new(
            "Dark", "Dark",
            "#12171C", "#171E25", "#1E2730", "#0E1317", "#2A3642", "#E8EDF2", "#93A3B3",
            "#38BDF8", "#7DD3FC", "#06121B", "#1F3A4D", "#222D38", "#26323D",
            "#34D399", "#FBBF24", "#F87171",
            "#0B1721", "#1B2A35", "#4A6070", "#6FE3F5", "#66000000"),

        new(
            "Light", "Light",
            "#F2F5F8", "#FFFFFF", "#EDF2F6", "#FFFFFF", "#D4DEE7", "#16222E", "#5A6C7D",
            "#0369A1", "#075985", "#FFFFFF", "#DCEFFB", "#E9F2F8", "#E2E9F0",
            "#15803D", "#B45309", "#B91C1C",
            "#C9E3F2", "#F7F4EA", "#9AAEBC", "#0E6C7C", "#33000000"),

        new(
            "MidnightBlue", "Midnight Blue",
            "#07101F", "#0C1930", "#102240", "#050C17", "#1E3657", "#E6EEF8", "#8EA6C4",
            "#4C9FFF", "#79B7FF", "#04101F", "#16365E", "#132A4A", "#1A3050",
            "#34D399", "#FBBF24", "#FB7185",
            "#061120", "#10243D", "#3A5A7E", "#7FE4FF", "#66000000"),

        new(
            "Forest", "Forest",
            "#0D1713", "#12211B", "#182C24", "#09110E", "#284438", "#E6F1EB", "#95B2A4",
            "#6EE7B7", "#A7F3D0", "#062016", "#1D3D30", "#1A3228", "#233B30",
            "#4ADE80", "#FCD34D", "#FCA5A5",
            "#0A1713", "#183028", "#3C5F4E", "#8CF0D4", "#66000000"),

        new(
            "Sunset", "Sunset",
            "#1A1210", "#221713", "#2C1E19", "#130D0B", "#453027", "#F4E9E2", "#BFA394",
            "#FB923C", "#FDBA74", "#1E0E05", "#4A2C1C", "#33231D", "#3D2A22",
            "#86EFAC", "#FDE047", "#FCA5A5",
            "#17100D", "#2B211C", "#6B4A3A", "#FFD7A8", "#66000000")
    ];

    public static ThemePalette Find(string? theme) =>
        All.FirstOrDefault(palette => string.Equals(palette.Name, theme, StringComparison.OrdinalIgnoreCase))
        ?? All[0];

    /// <summary>Splits "#RRGGBB" (or "#AARRGGBB") into its red, green and blue channels.</summary>
    public static (int Red, int Green, int Blue) ParseRgb(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length == 8)
        {
            value = value[2..];
        }

        if (value.Length != 6 || !int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return (128, 128, 128);
        }

        return ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    /// <summary>Formats an ARGB colour as "#AARRGGBB", which both shells understand.</summary>
    public static string ToHex(int alpha, int red, int green, int blue) =>
        $"#{alpha:X2}{red:X2}{green:X2}{blue:X2}";
}
