using System.Text.Json.Serialization;
using TheEye.Core.Json;

namespace TheEye.Core.Models;

public sealed class IvaoAirport
{
    [JsonPropertyName("icao")]
    public string? Icao { get; set; }

    [JsonPropertyName("iata")]
    public string? Iata { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("countryId")]
    public string? CountryId { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("military")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Military { get; set; }

    [JsonIgnore]
    public GeoCoordinate? Position =>
        Latitude is { } latitude && Longitude is { } longitude &&
        new GeoCoordinate(latitude, longitude) is var coordinate && coordinate.IsValid
            ? coordinate
            : null;

    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? Icao ?? "Unknown airport" : Name!;
}

public sealed class IvaoAirportSummary
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("icao")]
    public string? Icao { get; set; }

    [JsonPropertyName("iata")]
    public string? Iata { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("countryId")]
    public string? CountryId { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }
}

public sealed class IvaoAircraftSummary
{
    [JsonPropertyName("icaoCode")]
    public string? IcaoCode { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("wakeTurbulence")]
    public string? WakeTurbulence { get; set; }

    [JsonPropertyName("military")]
    public string? Military { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? IcaoCode ?? "Unknown" : Model!;
}
