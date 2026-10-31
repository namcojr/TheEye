using System.Text.Json.Serialization;

namespace TheEye.Core.Models;

/// <summary>
/// One recorded position of a session, as returned by
/// <c>v2/tracker/sessions/{sessionId}/tracks</c>. Unlike the summary payload the
/// track feed reports fractional values, so the numeric fields are doubles
/// instead of the integers used by <see cref="IvaoLastTrack"/>.
/// </summary>
public sealed class IvaoPilotTrack
{
    [JsonPropertyName("time")]
    public long? Time { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("altitude")]
    public double? Altitude { get; set; }

    [JsonPropertyName("altitudeDifference")]
    public double? AltitudeDifference { get; set; }

    [JsonPropertyName("arrivalDistance")]
    public double? ArrivalDistance { get; set; }

    [JsonPropertyName("departureDistance")]
    public double? DepartureDistance { get; set; }

    [JsonPropertyName("bank")]
    public double? Bank { get; set; }

    [JsonPropertyName("pitch")]
    public double? Pitch { get; set; }

    [JsonPropertyName("groundSpeed")]
    public double? GroundSpeed { get; set; }

    [JsonPropertyName("heading")]
    public double? Heading { get; set; }

    [JsonPropertyName("onGround")]
    public bool? OnGround { get; set; }

    [JsonPropertyName("sandbagging")]
    public bool? Sandbagging { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("transponder")]
    public int? Transponder { get; set; }

    [JsonPropertyName("transponderMode")]
    public string? TransponderMode { get; set; }

    [JsonIgnore]
    public GeoCoordinate? Position =>
        Latitude is { } latitude && Longitude is { } longitude &&
        new GeoCoordinate(latitude, longitude) is var coordinate && coordinate.IsValid
            ? coordinate
            : null;
}
