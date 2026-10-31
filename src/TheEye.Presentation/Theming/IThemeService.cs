using TheEye.Core.Maps;

namespace TheEye.Presentation.Theming;

/// <summary>
/// Swaps the colour resources of the running shell. The Windows shell reads the
/// palette from its WPF dictionaries, the Avalonia one from its application
/// resources, but the view models only need the two operations below.
/// </summary>
public interface IThemeService
{
    /// <summary>Raised after <see cref="Apply"/> has installed a new palette.</summary>
    event EventHandler? ThemeChanged;

    /// <summary>The normalised name of the palette in use.</summary>
    string CurrentTheme { get; }

    /// <summary>Installs the palette and re-themes every open window.</summary>
    void Apply(string? theme);

    /// <summary>The same colours expressed for the Mapsui layers.</summary>
    MapPalette CreateMapPalette();
}
