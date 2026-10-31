namespace TheEye.Core.Maps;

/// <summary>
/// Altitude bands a flown trail is painted with, from the apron up to the
/// flight levels. The order matches the legend of the IVAO web map.
/// </summary>
public enum AltitudeBand
{
    /// <summary>On the ground, or an altitude that could not be read.</summary>
    Ground,

    /// <summary>Below 3 000 ft.</summary>
    Below3000,

    /// <summary>3 000 ft to 4 999 ft.</summary>
    Below5000,

    /// <summary>5 000 ft to 6 999 ft.</summary>
    Below7000,

    /// <summary>7 000 ft to 14 999 ft.</summary>
    Below15000,

    /// <summary>15 000 ft to 29 999 ft.</summary>
    Below30000,

    /// <summary>30 000 ft to 39 999 ft.</summary>
    Below40000,

    /// <summary>40 000 ft and above.</summary>
    Above40000
}

/// <summary>
/// Colours the flown trail by the altitude the aircraft had at that point: red
/// on the ground, then orange, yellow, green, cyan, blue, dark blue and purple
/// as the flight climbs.
/// </summary>
public static class AltitudePalette
{
    public const string Ground = "#E74C3C";
    public const string Below3000 = "#E67E22";
    public const string Below5000 = "#F1C40F";
    public const string Below7000 = "#2ECC71";
    public const string Below15000 = "#22D3EE";
    public const string Below30000 = "#3498DB";
    public const string Below40000 = "#1D4ED8";
    public const string Above40000 = "#8E44AD";

    /// <summary>
    /// Places an altitude in its band. A missing altitude is treated like a
    /// ground position, which is what the fallback trail uses when the track
    /// feed carries no vertical information.
    /// </summary>
    public static AltitudeBand Band(double? altitudeFeet, bool onGround = false)
    {
        if (onGround || altitudeFeet is not { } altitude || altitude <= 0)
        {
            return AltitudeBand.Ground;
        }

        return altitude switch
        {
            < 3000 => AltitudeBand.Below3000,
            < 5000 => AltitudeBand.Below5000,
            < 7000 => AltitudeBand.Below7000,
            < 15000 => AltitudeBand.Below15000,
            < 30000 => AltitudeBand.Below30000,
            < 40000 => AltitudeBand.Below40000,
            _ => AltitudeBand.Above40000
        };
    }

    public static string For(AltitudeBand band) => band switch
    {
        AltitudeBand.Ground => Ground,
        AltitudeBand.Below3000 => Below3000,
        AltitudeBand.Below5000 => Below5000,
        AltitudeBand.Below7000 => Below7000,
        AltitudeBand.Below15000 => Below15000,
        AltitudeBand.Below30000 => Below30000,
        AltitudeBand.Below40000 => Below40000,
        _ => Above40000
    };

    public static string ForAltitude(double? altitudeFeet, bool onGround = false) =>
        For(Band(altitudeFeet, onGround));
}
