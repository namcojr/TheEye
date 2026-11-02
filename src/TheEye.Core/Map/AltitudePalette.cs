namespace TheEye.Core.Maps;

/// <summary>
/// Altitude bands a flown trail is painted with, from the apron up to the
/// flight levels. The limits are the ones IVAO WebEye colours its tracks by.
/// </summary>
public enum AltitudeBand
{
    /// <summary>On the ground, unknown, or up to 900 ft.</summary>
    Ground,

    /// <summary>901 ft to 2 900 ft.</summary>
    Below3000,

    /// <summary>2 901 ft to 4 900 ft.</summary>
    Below5000,

    /// <summary>4 901 ft to 9 900 ft.</summary>
    Below10000,

    /// <summary>9 901 ft to 14 900 ft.</summary>
    Below15000,

    /// <summary>14 901 ft to 19 900 ft.</summary>
    Below20000,

    /// <summary>19 901 ft to 24 900 ft.</summary>
    Below25000,

    /// <summary>24 901 ft to 29 900 ft.</summary>
    Below30000,

    /// <summary>29 901 ft to 42 000 ft.</summary>
    Below42000,

    /// <summary>Above 42 000 ft.</summary>
    Above42000
}

/// <summary>
/// Colours the flown trail by the altitude the aircraft had at that point, with
/// the saturated spectrum of IVAO WebEye: red near the ground, through orange,
/// yellow, green and cyan to blue and violet at the highest levels.
/// </summary>
public static class AltitudePalette
{
    public const string Ground     = "#FF0000";
    public const string Below3000  = "#FF7F00";
    public const string Below5000  = "#FFFF00";
    public const string Below10000 = "#7FFF00";
    public const string Below15000 = "#00FF00";
    public const string Below20000 = "#00FF7F";
    public const string Below25000 = "#00FFFF";
    public const string Below30000 = "#007FFF";
    public const string Below42000 = "#0000FF";
    public const string Above42000 = "#7F00FF";

    /// <summary>
    /// Places an altitude in its band. A missing altitude is treated like a
    /// ground position, which is what the fallback trail uses when the track
    /// feed carries no vertical information.
    /// </summary>
    public static AltitudeBand Band(double? altitudeFeet, bool onGround = false)
    {
        if (onGround || altitudeFeet is not { } altitude)
        {
            return AltitudeBand.Ground;
        }

        return altitude switch
        {
            <= 900 => AltitudeBand.Ground,
            <= 2900 => AltitudeBand.Below3000,
            <= 4900 => AltitudeBand.Below5000,
            <= 9900 => AltitudeBand.Below10000,
            <= 14900 => AltitudeBand.Below15000,
            <= 19900 => AltitudeBand.Below20000,
            <= 24900 => AltitudeBand.Below25000,
            <= 29900 => AltitudeBand.Below30000,
            <= 42000 => AltitudeBand.Below42000,
            _ => AltitudeBand.Above42000
        };
    }

    public static string For(AltitudeBand band) => band switch
    {
        AltitudeBand.Ground => Ground,
        AltitudeBand.Below3000 => Below3000,
        AltitudeBand.Below5000 => Below5000,
        AltitudeBand.Below10000 => Below10000,
        AltitudeBand.Below15000 => Below15000,
        AltitudeBand.Below20000 => Below20000,
        AltitudeBand.Below25000 => Below25000,
        AltitudeBand.Below30000 => Below30000,
        AltitudeBand.Below42000 => Below42000,
        _ => Above42000
    };

    public static string ForAltitude(double? altitudeFeet, bool onGround = false) =>
        For(Band(altitudeFeet, onGround));
}
