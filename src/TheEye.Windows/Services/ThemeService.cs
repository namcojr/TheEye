using System.Windows;
using System.Windows.Media;
using TheEye.Core.Maps;
using TheEye.Core.Settings;

namespace TheEye.Windows.Services;

/// <summary>
/// Swaps the colour dictionary at runtime. The Base and Controls dictionaries use
/// DynamicResource for every brush, so replacing the first merged dictionary
/// re-themes the entire shell without rebuilding any views.
/// </summary>
public static class ThemeService
{
    public static event EventHandler? ThemeChanged;

    public static string CurrentTheme { get; private set; } = TheEyeThemes.Dark;

    public static void Apply(string? theme)
    {
        var normalized = TheEyeThemes.Normalize(theme);
        var application = Application.Current
            ?? throw new InvalidOperationException("The theme service needs a running WPF application.");

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/TheEye;component/Themes/{normalized}.xaml", UriKind.Absolute)
        };

        var dictionaries = application.Resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            dictionaries.Add(dictionary);
            dictionaries.Add(CreateBaseDictionary());
        }
        else
        {
            dictionaries[0] = dictionary;
            ReloadBaseDictionary(dictionaries);
        }

        CurrentTheme = normalized;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static ResourceDictionary CreateBaseDictionary() =>
        new()
        {
            Source = new Uri("pack://application:,,,/TheEye;component/Themes/Base.xaml", UriKind.Absolute)
        };

    private static void ReloadBaseDictionary(IList<ResourceDictionary> dictionaries)
    {
        // The brushes in Base.xaml bind their colours to the theme dictionary.
        // Replacing Base as well as the theme forces already-created controls to
        // re-resolve those brushes when the theme changes at runtime.
        for (var index = 1; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString.Replace('\\', '/');
            if (source?.EndsWith("/Themes/Base.xaml", StringComparison.OrdinalIgnoreCase) == true)
            {
                dictionaries[index] = CreateBaseDictionary();
                return;
            }
        }

        dictionaries.Add(CreateBaseDictionary());
    }

    public static MapPalette CreateMapPalette()
    {
        var dark = Luminance(GetColor("WindowBackgroundColor")) < 0.45;
        return new MapPalette(
            ToHex(GetColor("MapWaterColor")),
            ToHex(GetColor("MapLandColor")),
            ToHex(GetColor("MapBorderColor")),
            ToHex(GetColor("MapLabelColor")),
            ToHex(GetColor("AccentColor")),
            dark,
            ToHex(GetColor("TextColor")),
            ToHexWithAlpha(GetColor("ShadowColor")));
    }

    public static Color GetColor(string key) =>
        Application.Current?.TryFindResource(key) is Color color ? color : Colors.Gray;

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ToHexWithAlpha(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
}
