namespace TheEye.Core.Models;

/// <summary>
/// One point of an IVAO region map. IVAO returns these either as
/// <c>[longitude, latitude]</c> arrays or as <c>{ lat, lng }</c> objects.
/// </summary>
public readonly record struct IvaoCoordinate(double Latitude, double Longitude)
{
    public GeoCoordinate ToGeoCoordinate() => new(Latitude, GeoPolygon.NormalizeLongitude(Longitude));
}
