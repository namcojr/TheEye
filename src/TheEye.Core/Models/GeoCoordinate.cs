namespace TheEye.Core.Models;

/// <summary>
/// A latitude/longitude pair in decimal degrees.
/// </summary>
public readonly record struct GeoCoordinate(double Latitude, double Longitude)
{
    public bool IsValid =>
        !double.IsNaN(Latitude) &&
        !double.IsNaN(Longitude) &&
        !double.IsInfinity(Latitude) &&
        !double.IsInfinity(Longitude) &&
        Latitude is >= -90 and <= 90 &&
        Longitude is >= -180 and <= 180;

    public override string ToString() =>
        FormattableString.Invariant($"{Latitude:0.#####}, {Longitude:0.#####}");
}
