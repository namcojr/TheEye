using System.Text.Json.Serialization;
using TheEye.Core.Reference;

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

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Seconds connected when the feed answered; the timestamp wins for display.</summary>
    [JsonPropertyName("time")]
    public long? Time { get; set; }

    /// <summary>The client the controller works with ("Aurora/mac"), on the full feed only.</summary>
    [JsonPropertyName("softwareType")]
    public IvaoSoftwareSummary? SoftwareType { get; set; }

    /// <summary>
    /// The member behind the callsign. Only the authenticated feed
    /// (<c>/v2/tracker/now/atcs</c>) carries it, so it is null on a summary and
    /// is layered on by <see cref="ApplyMember"/> once that feed has answered.
    /// </summary>
    [JsonPropertyName("user")]
    public IvaoSessionUser? User { get; set; }

    [JsonIgnore]
    public string Facility => IsObserver ? ObserverFacility : ReportedFacility;

    /// <summary>
    /// The facility token IVAO reported for the station, without the observer
    /// override: <c>atcSession</c> first, then the airport position, then the
    /// subcenter, and "ATC" when the feed named none.
    /// </summary>
    [JsonIgnore]
    public string ReportedFacility =>
        AtcSession?.Position ?? AtcPosition?.Position ?? Subcenter?.Position ?? DefaultFacility;

    /// <summary>The facility token every glyph and list prints for an observer.</summary>
    public const string ObserverFacility = "OBS";

    /// <summary>The facility token used when the feed names no position at all.</summary>
    public const string DefaultFacility = "ATC";

    /// <summary>
    /// True when the member observes instead of controlling. IVAO keeps the
    /// position field of the station being watched, so a member connected as an
    /// observer would otherwise be drawn and listed as a controller: the
    /// connection type, the <c>_OBS</c> callsign suffix and the position token
    /// are all checked, any of which marks the session as an observer.
    /// </summary>
    [JsonIgnore]
    public bool IsObserver =>
        string.Equals(ConnectionType?.Trim(), ObserverFacility, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(AtcSession?.Position?.Trim(), ObserverFacility, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(AtcPosition?.Position?.Trim(), ObserverFacility, StringComparison.OrdinalIgnoreCase) ||
        HasObserverSuffix;

    /// <summary>
    /// True when the callsign ends in <c>_OBS</c>, the suffix IVAO gives the
    /// callsign of a member who connected to watch a station.
    /// </summary>
    [JsonIgnore]
    public bool HasObserverSuffix => Callsign
        .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } segments &&
        string.Equals(segments[^1], ObserverFacility, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>The airport country spelled out ("RO" → "Romania"), for the card.</summary>
    [JsonIgnore]
    public string CountryName => DivisionCatalog.Name(AtcPosition?.Airport?.CountryId) is { Length: > 0 } name
        ? name
        : "—";

    /// <summary>
    /// True when the station serves an airport. Only an airport position carries a
    /// city and a country; a control centre covers a region, so the card has no
    /// location to print for it.
    /// </summary>
    [JsonIgnore]
    public bool HasAirport => AtcPosition?.Airport is not null;

    [JsonIgnore]
    public string PositionText => IsObserver
        ? ObserverFacility
        : AtcPosition?.Position ?? Subcenter?.Position ?? Facility;

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

    [JsonIgnore]
    public bool HasMember => User is { Id: > 0 };

    /// <summary>"Ana Silva", or empty when IVAO sent no name for the member.</summary>
    [JsonIgnore]
    public string MemberName => User is { HasName: true } member ? member.FullName : string.Empty;

    /// <summary>The client the controller works with ("IvAc", "Aurora"), empty when unknown.</summary>
    [JsonIgnore]
    public string SoftwareText => SoftwareType?.Name ?? string.Empty;

    /// <summary>"WS1-EUW2", the server the station is connected to, empty when unknown.</summary>
    [JsonIgnore]
    public string ServerText => ServerId?.Trim() ?? string.Empty;

    /// <summary>
    /// Time connected, counted from the connection timestamp rather than from
    /// the count that came with the record, so an answer that is already a few
    /// seconds old keeps counting up.
    /// </summary>
    public TimeSpan? Online(DateTimeOffset now)
    {
        if (CreatedAt is { } connectedAt)
        {
            var elapsed = now - connectedAt;
            return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        }

        return Time is { } seconds && seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
    }

    /// <summary>
    /// Copies the member details the identity feed knows onto this record. The
    /// summary the tracker starts from carries no name, so the card and the
    /// lists only show who is controlling after this has run.
    /// </summary>
    public void ApplyMember(IvaoAtc identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        User ??= identity.User;
        SoftwareType ??= identity.SoftwareType;
        ServerId ??= identity.ServerId;
        CreatedAt ??= identity.CreatedAt;
        Time ??= identity.Time;
    }
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
