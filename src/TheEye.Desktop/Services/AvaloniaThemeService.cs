using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TheEye.Core.Maps;
using TheEye.Presentation.Theming;

namespace TheEye.Desktop.Services;

/// <summary>
/// Installs the shared palette into the Avalonia application resources. The
/// theme table is the one the Windows shell paints from, so both shells show the
/// same five themes; only the dictionary the colours land in differs.
/// </summary>
public sealed class AvaloniaThemeService : IThemeService
{
    private ThemePalette _current = ThemeCatalog.Find(null);

    public event EventHandler? ThemeChanged;

    public string CurrentTheme => _current.Name;

    public void Apply(string? theme)
    {
        var palette = ThemeCatalog.Find(theme);
        _current = palette;

        if (Application.Current is { } application)
        {
            // Fluent draws every control it still owns (text boxes, tooltips,
            // scroll bars) from its own keys; overriding them in the
            // application resources is what keeps those chrome pieces in the
            // palette instead of the stock shell colours.
            if (palette.IsDark)
            {
                application.RequestedThemeVariant = ThemeVariant.Dark;
            }
            else
            {
                application.RequestedThemeVariant = ThemeVariant.Light;
            }

            var resources = application.Resources;
            resources["WindowBackgroundBrush"] = Brush(palette.WindowBackground);
            resources["PanelBackgroundBrush"] = Brush(palette.PanelBackground);
            resources["PanelAltBackgroundBrush"] = Brush(palette.PanelAltBackground);
            resources["HeaderBackgroundBrush"] = Brush(palette.HeaderBackground);
            resources["BorderBrush"] = Brush(palette.Border);
            resources["TextBrush"] = Brush(palette.Text);
            resources["MutedTextBrush"] = Brush(palette.MutedText);
            resources["AccentBrush"] = Brush(palette.Accent);
            resources["AccentHoverBrush"] = Brush(palette.AccentHover);
            resources["AccentTextBrush"] = Brush(palette.AccentText);
            resources["SelectionBrush"] = Brush(palette.Selection);
            resources["HoverBackgroundBrush"] = Brush(palette.HoverBackground);
            resources["GridLineBrush"] = Brush(palette.GridLine);
            resources["SuccessBrush"] = Brush(palette.Success);
            resources["WarningBrush"] = Brush(palette.Warning);
            resources["ErrorBrush"] = Brush(palette.Error);
            resources["MapWaterBrush"] = Brush(palette.MapWater);
            resources["MapLandBrush"] = Brush(palette.MapLand);
            resources["MapBorderBrush"] = Brush(palette.MapBorder);
            resources["MapLabelBrush"] = Brush(palette.MapLabel);

            ApplyFluentOverrides(resources, palette);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public MapPalette CreateMapPalette() => _current.ToMapPalette();

    private static void ApplyFluentOverrides(IResourceDictionary resources, ThemePalette palette)
    {
        resources["TextControlBackground"] = Brush(palette.PanelAltBackground);
        resources["TextControlBackgroundPointerOver"] = Brush(palette.HoverBackground);
        resources["TextControlBackgroundFocused"] = Brush(palette.PanelAltBackground);
        resources["TextControlBackgroundDisabled"] = Brush(palette.PanelBackground);

        resources["TextControlBorderBrush"] = Brush(palette.Border);
        resources["TextControlBorderBrushPointerOver"] = Brush(palette.Border);
        resources["TextControlBorderBrushFocused"] = Brush(palette.Accent);
        resources["TextControlBorderBrushDisabled"] = Brush(palette.Border);

        resources["TextControlForeground"] = Brush(palette.Text);
        resources["TextControlForegroundPointerOver"] = Brush(palette.Text);
        resources["TextControlForegroundFocused"] = Brush(palette.Text);
        resources["TextControlForegroundDisabled"] = Brush(palette.MutedText);

        resources["TextControlPlaceholderForeground"] = Brush(palette.MutedText);
        resources["TextControlPlaceholderForegroundPointerOver"] = Brush(palette.MutedText);
        resources["TextControlPlaceholderForegroundFocused"] = Brush(palette.MutedText);

        resources["ToolTipBackground"] = Brush(palette.PanelAltBackground);
        resources["ToolTipBorderBrush"] = Brush(palette.Border);
        resources["ToolTipForeground"] = Brush(palette.Text);

        var accent = Color.Parse(palette.Accent);
        resources["SystemAccentColor"] = accent;
        resources["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.12);
        resources["SystemAccentColorDark2"] = Mix(accent, Colors.Black, 0.25);
        resources["SystemAccentColorDark3"] = Mix(accent, Colors.Black, 0.40);
        resources["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.20);
        resources["SystemAccentColorLight2"] = Mix(accent, Colors.White, 0.40);
        resources["SystemAccentColorLight3"] = Mix(accent, Colors.White, 0.60);

        // The text selection colour is consumed as a plain Color, not a brush.
        resources["TextControlSelectionHighlightColor"] = accent;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private static Color Mix(Color color, Color other, double amount) =>
        Color.FromArgb(
            color.A,
            (byte)(color.R + (other.R - color.R) * amount),
            (byte)(color.G + (other.G - color.G) * amount),
            (byte)(color.B + (other.B - color.B) * amount));
}
