namespace TheEye.Core.Maps;

/// <summary>
/// Theme colours handed to Mapsui. Keeping them as hex strings avoids a
/// dependency on Mapsui in the settings layer. <paramref name="Text"/> and
/// <paramref name="Shadow"/> paint the aircraft callsigns and the drop shadow of
/// their pins; left out, they fall back to the map label colour and a soft black.
/// </summary>
public sealed record MapPalette(
    string Water,
    string Land,
    string Border,
    string Label,
    string Accent,
    bool IsDark,
    string? Text = null,
    string? Shadow = null);

public static class FacilityPalette
{
    public const string Delivery = "#fb923c";
    public const string Ground = "#facc15";
    public const string Tower = "#f43f5e";
    public const string Departure = "#d970f5";
    public const string Approach = "#3b82f6";
    public const string Control = "#6e91b6";
    public const string FlightService = "#2dbeaa";
    public const string Military = "#22a05a";
    public const string Information = "#9B59B6";
    public const string Default = "#95A5A6";

    /// <summary>
    /// The ring around the badge of a station whose member is connected with
    /// supervisor rights. A supervisor is not a facility — the badge keeps the
    /// glyph and colour of what they control — so this colour is drawn around
    /// the glyph instead, which marks the member on the map without hiding the
    /// position. Both shells mirror the value as <c>SupervisorBrush</c> so the
    /// SUP list, the detail cards and the map agree on one colour.
    /// </summary>
    public const string Supervisor = "#E84393";

    public static string ForFacility(string? facility) => facility?.Trim().ToUpperInvariant() switch
    {
        "TWR" => Tower,
        "DEL" => Delivery,
        "GND" => Ground,
        "DEP" => Departure,
        "APP" or "ARR" => Approach,
        "CTR" or "CEN" => Control,
        "FSS" => FlightService,
        "MIL" => Military,
        "INFO" => Information,
        _ => Default
    };

    public static string DisplayName(string? facility) => facility?.Trim().ToUpperInvariant() switch
    {
        "TWR" => "Tower",
        "GND" => "Ground",
        "DEL" => "Delivery",
        "APP" => "Approach",
        "DEP" => "Departure",
        "ARR" => "Arrival",
        "CTR" => "Control",
        "FSS" => "Information",
        "MIL" => "Military",
        "CEN" => "Control",
        "INFO" => "Information",
        "OBS" => "Observer",
        _ => string.IsNullOrWhiteSpace(facility) ? "ATC" : facility
    };
}
