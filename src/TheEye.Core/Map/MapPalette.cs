namespace TheEye.Core.Maps;

/// <summary>
/// Theme colours handed to Mapsui. Keeping them as hex strings avoids a
/// dependency on Mapsui in the settings layer.
/// </summary>
public sealed record MapPalette(
    string Water,
    string Land,
    string Border,
    string Label,
    string Accent,
    bool IsDark);

public static class FacilityPalette
{
    public const string Tower = "#2ECC71";
    public const string Ground = "#F1C40F";
    public const string Approach = "#E67E22";
    public const string Control = "#3498DB";
    public const string Information = "#9B59B6";
    public const string Default = "#95A5A6";

    public static string ForFacility(string? facility) => facility?.Trim().ToUpperInvariant() switch
    {
        "TWR" => Tower,
        "GND" or "DEL" => Ground,
        "APP" or "DEP" => Approach,
        "CTR" or "FSS" => Control,
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
        "CTR" => "Control",
        "FSS" => "Flight Service",
        "INFO" => "Information",
        _ => string.IsNullOrWhiteSpace(facility) ? "ATC" : facility
    };
}
