namespace TheEye.Core.Maps;

/// <summary>
/// The colours an aircraft pin is painted with. The basemap decides them, not
/// the shell theme: a pin drawn in the theme's text colour disappears as soon as
/// the map under it is a raster style of the other brightness. The offline map
/// is painted with the theme palette, so for that style the theme is the map.
/// </summary>
public static class AircraftMarkerStyles
{
    /// <summary>Artwork on a dark basemap.</summary>
    public const string LightIcon = "#FFFFFF";

    /// <summary>
    /// Artwork on a light basemap. Dark grey rather than black, so the pin does
    /// not read like a hole punched into the map next to its black outlines.
    /// </summary>
    public const string DarkIcon = "#1F2933";

    /// <summary>
    /// True when the map the pins are drawn on is dark. The offline style is
    /// answered by the palette, because that is what paints it; every other
    /// style is answered by <see cref="MapBasemap.IsDark"/>.
    /// </summary>
    public static bool IsDarkMap(MapPalette palette, string? mapStyle) =>
        string.Equals(MapBasemap.Normalize(mapStyle), MapBasemap.Offline, StringComparison.Ordinal)
            ? palette.IsDark
            : MapBasemap.IsDark(mapStyle);

    /// <summary>The colour of the aircraft artwork for the map currently drawn.</summary>
    public static string IconColor(MapPalette palette, string? mapStyle) =>
        IsDarkMap(palette, mapStyle) ? LightIcon : DarkIcon;
}
