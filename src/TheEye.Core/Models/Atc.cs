using System.Text.Json.Serialization;

namespace TheEye.Core.Models;

public sealed class IvaoAtc
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("atcSession")]
    public IvaoAtcSession? AtcSession { get; set; }

    [JsonPropertyName("atcPosition")]
    public IvaoAtcPosition? AtcPosition { get; set; }

    [JsonPropertyName("subcenter")]
    public IvaoSubcenter? Subcenter { get; set; }

    [JsonIgnore]
    public string Facility => AtcSession?.Position ?? AtcPosition?.Position ?? Subcenter?.Position ?? "ATC";

    [JsonIgnore]
    public double? Frequency => AtcSession?.Frequency ?? Subcenter?.Frequency;

    [JsonIgnore]
    public string FrequencyText => Frequency is > 0
        ? Frequency.Value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
        : "—";

    [JsonIgnore]
    public string AtcCallsign => AtcPosition?.AtcCallsign ?? Subcenter?.AtcCallsign ?? Callsign;

    [JsonIgnore]
    public string AirportIcao => AtcPosition?.Airport?.Icao ?? AtcPosition?.AirportId ?? "—";

    [JsonIgnore]
    public string AirportName => AtcPosition?.Airport?.DisplayName ?? "—";

    [JsonIgnore]
    public string City => AtcPosition?.Airport?.City ?? "—";

    [JsonIgnore]
    public string Country => AtcPosition?.Airport?.CountryId ?? "—";

    [JsonIgnore]
    public string PositionText => AtcPosition?.Position ?? Subcenter?.Position ?? Facility;

    [JsonIgnore]
    public string SectorPointsText => PolygonPoints.Count >= 3 ? PolygonPoints.Count.ToString() : "—";

    [JsonIgnore]
    public IReadOnlyList<IvaoCoordinate> PolygonPoints =>
        AtcPosition?.RegionMapPolygon is { Count: >= 3 } airportPolygon
            ? airportPolygon
            : Subcenter?.RegionMapPolygon is { Count: >= 3 } centerPolygon
                ? centerPolygon
                : Array.Empty<IvaoCoordinate>();

    [JsonIgnore]
    public IReadOnlyList<IvaoCoordinate> RegionPoints =>
        AtcPosition?.RegionMap is { Count: >= 3 } airportRegion
            ? airportRegion
            : Subcenter?.RegionMap is { Count: >= 3 } centerRegion
                ? centerRegion
                : Array.Empty<IvaoCoordinate>();

    [JsonIgnore]
    public GeoCoordinate? Position
    {
        get
        {
            if (AtcPosition?.Airport?.Position is { } airportPosition)
            {
                return airportPosition;
            }

            if (Subcenter is { Latitude: { } latitude, Longitude: { } longitude } &&
                new GeoCoordinate(latitude, longitude) is var coordinate &&
                coordinate.IsValid)
            {
                return coordinate;
            }

            return GeoPolygon.Centroid(PolygonPoints.Select(p => p.ToGeoCoordinate()).ToList());
        }
    }

    [JsonIgnore]
    public string ScopeLabel => Subcenter is { } subcenter
        ? $"{subcenter.CenterId} {subcenter.Position}"
        : $"{AirportIcao} {PositionText}";

    [JsonIgnore]
    public bool IsMilitary => AtcPosition?.Military == true || Subcenter?.Military == true;
}

public sealed class IvaoAtcSession
{
    [JsonPropertyName("frequency")]
    public double? Frequency { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }
}

public sealed class IvaoAtcSummary
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("atcSession")]
    public IvaoAtcSession? AtcSession { get; set; }

    [JsonPropertyName("atcPosition")]
    public IvaoAtcPosition? AtcPosition { get; set; }

    [JsonPropertyName("subcenter")]
    public IvaoSubcenter? Subcenter { get; set; }
}
