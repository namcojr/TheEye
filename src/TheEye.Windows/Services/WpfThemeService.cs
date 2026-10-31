using TheEye.Core.Maps;
using TheEye.Presentation.Theming;

namespace TheEye.Windows.Services;

/// <summary>
/// The interface the shared view models see. The static <see cref="ThemeService"/>
/// still owns the WPF dictionaries and the DWM frame hook; this only forwards,
/// so the Windows shell keeps painting its palette from the same XAML the rest
/// of the theme system has always used.
/// </summary>
public sealed class WpfThemeService : IThemeService
{
    public event EventHandler? ThemeChanged
    {
        add => ThemeService.ThemeChanged += value;
        remove => ThemeService.ThemeChanged -= value;
    }

    public string CurrentTheme => ThemeService.CurrentTheme;

    public void Apply(string? theme) => ThemeService.Apply(theme);

    public MapPalette CreateMapPalette() => ThemeService.CreateMapPalette();
}
