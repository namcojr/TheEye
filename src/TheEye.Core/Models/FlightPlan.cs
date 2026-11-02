using System.Text.Json.Serialization;
using TheEye.Core.Json;

namespace TheEye.Core.Models;

/// <summary>
/// A flight plan as IVAO holds it. The tracker summaries embed a trimmed copy —
/// the airports and aircraft, but not the filed route — while
/// <c>/v2/tracker/sessions/{id}/flightPlans/latest</c> answers with the whole
/// filing: cruise speed and level, the times, the endurance, the alternate and
/// the equipment. Every member of the full answer is optional here, because a
/// revision may leave any of them out and the card shows a dash instead of
/// losing the rest of the plan.
/// </summary>
public sealed class IvaoFlightPlan
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>The session that filed this revision; used to drop stale answers.</summary>
    [JsonPropertyName("sessionId")]
    public long? SessionId { get; set; }

    /// <summary>How often the flight plan has been amended since it was filed.</summary>
    [JsonPropertyName("revision")]
    public int? Revision { get; set; }

    [JsonPropertyName("aircraftId")]
    public string? AircraftId { get; set; }

    /// <summary>The number of the aircraft in the pilot's livery, as filed.</summary>
    [JsonPropertyName("aircraftNumber")]
    public int? AircraftNumber { get; set; }

    [JsonPropertyName("departureId")]
    public string? DepartureId { get; set; }

    [JsonPropertyName("arrivalId")]
    public string? ArrivalId { get; set; }

    [JsonPropertyName("aircraft")]
    public IvaoAircraftSummary? Aircraft { get; set; }

    [JsonPropertyName("departure")]
    public IvaoAirport? Departure { get; set; }

    [JsonPropertyName("arrival")]
    public IvaoAirport? Arrival { get; set; }

    [JsonPropertyName("alternative")]
    public IvaoAirport? Alternative { get; set; }

    [JsonPropertyName("alternative2")]
    public IvaoAirport? Alternative2 { get; set; }

    /// <summary>The first alternate, as filed next to the nested airport record.</summary>
    [JsonPropertyName("alternativeId")]
    public string? AlternativeId { get; set; }

    /// <summary>The second alternate, which a long haul files in case the first is unavailable.</summary>
    [JsonPropertyName("alternative2Id")]
    public string? Alternative2Id { get; set; }

    [JsonPropertyName("route")]
    public string? Route { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    /// <summary>The filed cruise speed, e.g. <c>N0450</c> (knots) or <c>M084</c> (Mach).</summary>
    [JsonPropertyName("speed")]
    public string? Speed { get; set; }

    /// <summary>The filed cruise level, e.g. <c>FL350</c> or <c>35000</c>.</summary>
    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("flightRules")]
    public string? FlightRules { get; set; }

    [JsonPropertyName("flightType")]
    public string? FlightType { get; set; }

    /// <summary>Estimated time enroute in seconds, as IVAO reports durations.</summary>
    [JsonPropertyName("eet")]
    [JsonConverter(typeof(SecondsConverter))]
    public long? EetSeconds { get; set; }

    /// <summary>Endurance in seconds: how long the aircraft can stay airborne.</summary>
    [JsonPropertyName("endurance")]
    [JsonConverter(typeof(SecondsConverter))]
    public long? EnduranceSeconds { get; set; }

    /// <summary>
    /// The off-block time the flight was filed for, in seconds. IVAO stores the
    /// value as a count from midnight or as a Unix timestamp depending on the
    /// feed, and <see cref="DepartureTimeUtc"/> reads both.
    /// </summary>
    [JsonPropertyName("departureTime")]
    [JsonConverter(typeof(SecondsConverter))]
    public long? DepartureTimeSeconds { get; set; }

    /// <summary>When the aircraft actually left the blocks, in the same seconds form.</summary>
    [JsonPropertyName("actualDepartureTime")]
    [JsonConverter(typeof(SecondsConverter))]
    public long? ActualDepartureTimeSeconds { get; set; }

    [JsonPropertyName("peopleOnBoard")]
    public int? PeopleOnBoard { get; set; }

    /// <summary>
    /// The radio and navigation equipment the aircraft is fitted with. The filed
    /// plan sends a record per item (<c>{ "id": "E2", "name": "D-FIS ACARS" }</c>)
    /// while the summaries send the bare codes, so the converter keeps the codes.
    /// </summary>
    [JsonPropertyName("aircraftEquipments")]
    [JsonConverter(typeof(IvaoCodeListConverter))]
    public string[]? AircraftEquipments { get; set; }

    /// <summary>The transponder capabilities, e.g. <c>S</c>, <c>C</c> and <c>E</c>.</summary>
    [JsonPropertyName("aircraftTransponderTypes")]
    [JsonConverter(typeof(IvaoCodeListConverter))]
    public string[]? AircraftTransponderTypes { get; set; }

    [JsonIgnore]
    public string? AlternateIcao => First(Alternative?.Icao, AlternativeId);

    [JsonIgnore]
    public string? Alternate2Icao => First(Alternative2?.Icao, Alternative2Id);

    [JsonIgnore]
    public DateTimeOffset? DepartureTimeUtc => FromSeconds(DepartureTimeSeconds);

    [JsonIgnore]
    public DateTimeOffset? ActualDepartureTimeUtc => FromSeconds(ActualDepartureTimeSeconds);

    [JsonIgnore]
    public string DepartureIcao => Departure?.Icao ?? DepartureId ?? "????";

    [JsonIgnore]
    public string ArrivalIcao => Arrival?.Icao ?? ArrivalId ?? "????";

    [JsonIgnore]
    public string RouteLabel => $"{DepartureIcao} → {ArrivalIcao}";

    [JsonIgnore]
    public string AircraftLabel => Aircraft?.DisplayName ?? AircraftId ?? "Unknown aircraft";

    /// <summary>
    /// The radio and navigation equipment of item 10a, as one string. Empty when
    /// the revision carried none, so the card can hide the line instead of
    /// printing a dash for a plan that never filed the codes.
    /// </summary>
    [JsonIgnore]
    public string EquipmentText => CodesOf(AircraftEquipments);

    /// <summary>
    /// The surveillance capabilities of item 10b (for instance L B1 D1: a mode S
    /// transponder with ADS-B out and ADS-C), which complete the equipment block
    /// of ICAO field 10.
    /// </summary>
    [JsonIgnore]
    public string TransponderText => CodesOf(AircraftTransponderTypes);

    /// <summary>The codes of one filed list as a single space-separated string.</summary>
    private static string CodesOf(string[]? codes) =>
        codes is { Length: > 0 } list
            ? string.Join(' ', list.Where(code => !string.IsNullOrWhiteSpace(code)))
            : string.Empty;

    /// <summary>
    /// Reads a time the API reports as "seconds". A count that fits in a day is a
    /// time of day in UTC — the form a filed flight plan uses, with the date part
    /// left at the epoch — while anything larger is an absolute timestamp, which
    /// is how the tracker stores the times of the sessions it has recorded.
    /// </summary>
    private static DateTimeOffset? FromSeconds(long? seconds)
    {
        const long SecondsPerDay = 24 * 60 * 60;

        return seconds switch
        {
            null or <= 0 => null,
            <= SecondsPerDay => DateTimeOffset.UnixEpoch.AddSeconds(seconds.Value),
            _ => DateTimeOffset.FromUnixTimeSeconds(seconds.Value)
        };
    }

    private static string? First(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? (string.IsNullOrWhiteSpace(fallback) ? null : fallback) : preferred;
}
