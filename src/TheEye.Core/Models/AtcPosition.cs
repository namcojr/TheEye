using System.Text.Json.Serialization;
using TheEye.Core.Json;

namespace TheEye.Core.Models;

public sealed class IvaoAtcPosition
{
    [JsonPropertyName("airportId")]
    public string? AirportId { get; set; }

    [JsonPropertyName("atcCallsign")]
    public string? AtcCallsign { get; set; }

    [JsonPropertyName("military")]
    public bool Military { get; set; }

    [JsonPropertyName("middleIdentifier")]
    public string? MiddleIdentifier { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("composePosition")]
    public string? ComposePosition { get; set; }

    [JsonPropertyName("regionMap")]
    [JsonConverter(typeof(IvaoCoordinateListConverter))]
    public List<IvaoCoordinate> RegionMap { get; set; } = [];

    [JsonPropertyName("regionMapPolygon")]
    [JsonConverter(typeof(IvaoCoordinateListConverter))]
    public List<IvaoCoordinate> RegionMapPolygon { get; set; } = [];

    [JsonPropertyName("airport")]
    public IvaoAirport? Airport { get; set; }
}

public sealed class IvaoSubcenter
{
    [JsonPropertyName("centerId")]
    public string? CenterId { get; set; }

    [JsonPropertyName("atcCallsign")]
    public string? AtcCallsign { get; set; }

    [JsonPropertyName("middleIdentifier")]
    public string? MiddleIdentifier { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("composePosition")]
    public string? ComposePosition { get; set; }

    [JsonPropertyName("military")]
    public bool Military { get; set; }

    [JsonPropertyName("frequency")]
    public double? Frequency { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("regionMap")]
    [JsonConverter(typeof(IvaoCoordinateListConverter))]
    public List<IvaoCoordinate> RegionMap { get; set; } = [];

    [JsonPropertyName("regionMapPolygon")]
    [JsonConverter(typeof(IvaoCoordinateListConverter))]
    public List<IvaoCoordinate> RegionMapPolygon { get; set; } = [];
}

public sealed class IvaoCenter
{
    [JsonPropertyName("centerId")]
    public string? CenterId { get; set; }

    [JsonPropertyName("atcCallsign")]
    public string? AtcCallsign { get; set; }

    [JsonPropertyName("middleIdentifier")]
    public string? MiddleIdentifier { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("composePosition")]
    public string? ComposePosition { get; set; }

    [JsonPropertyName("military")]
    public bool Military { get; set; }

    [JsonPropertyName("frequency")]
    public double? Frequency { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("regionMap")]
    [JsonConverter(typeof(IvaoCoordinateListConverter))]
    public List<IvaoCoordinate> RegionMap { get; set; } = [];

    [JsonPropertyName("regionMapPolygon")]
    [JsonConverter(typeof(IvaoCoordinateListConverter))]
    public List<IvaoCoordinate> RegionMapPolygon { get; set; } = [];
}
