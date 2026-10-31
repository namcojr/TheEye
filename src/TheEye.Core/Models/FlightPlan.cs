using System.Text.Json.Serialization;

namespace TheEye.Core.Models;

public sealed class IvaoFlightPlan
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("aircraftId")]
    public string? AircraftId { get; set; }

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

    [JsonPropertyName("route")]
    public string? Route { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [JsonPropertyName("cruisingSpeed")]
    public string? CruisingSpeed { get; set; }

    [JsonPropertyName("cruisingLevel")]
    public string? CruisingLevel { get; set; }

    [JsonPropertyName("flightRules")]
    public string? FlightRules { get; set; }

    [JsonPropertyName("flightType")]
    public string? FlightType { get; set; }

    [JsonPropertyName("departureTime")]
    public DateTimeOffset? DepartureTime { get; set; }

    [JsonPropertyName("arrivalTime")]
    public DateTimeOffset? ArrivalTime { get; set; }

    [JsonPropertyName("estimatedTimeEnroute")]
    public string? EstimatedTimeEnroute { get; set; }

    [JsonPropertyName("endurance")]
    public string? Endurance { get; set; }

    [JsonPropertyName("alternate1Id")]
    public string? Alternate1Id { get; set; }

    [JsonPropertyName("alternate2Id")]
    public string? Alternate2Id { get; set; }

    [JsonIgnore]
    public string DepartureIcao => Departure?.Icao ?? DepartureId ?? "????";

    [JsonIgnore]
    public string ArrivalIcao => Arrival?.Icao ?? ArrivalId ?? "????";

    [JsonIgnore]
    public string RouteLabel => $"{DepartureIcao} → {ArrivalIcao}";

    [JsonIgnore]
    public string AircraftLabel => Aircraft?.DisplayName ?? AircraftId ?? "Unknown aircraft";
}
