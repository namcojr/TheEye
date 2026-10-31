using System.Text.Json.Serialization;
using TheEye.Core.Json;

namespace TheEye.Core.Models;

public sealed class IvaoLastTrack
{
    [JsonPropertyName("altitude")]
    public int? Altitude { get; set; }

    [JsonPropertyName("altitudeDifference")]
    public int? AltitudeDifference { get; set; }

    [JsonPropertyName("arrivalDistance")]
    public double? ArrivalDistance { get; set; }

    [JsonPropertyName("departureDistance")]
    public double? DepartureDistance { get; set; }

    [JsonPropertyName("groundSpeed")]
    public int? GroundSpeed { get; set; }

    [JsonPropertyName("heading")]
    public int? Heading { get; set; }

    [JsonPropertyName("latitude")]
    public double? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double? Longitude { get; set; }

    [JsonPropertyName("onGround")]
    public bool? OnGround { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; set; }

    [JsonPropertyName("transponder")]
    public int? Transponder { get; set; }

    [JsonPropertyName("transponderMode")]
    public string? TransponderMode { get; set; }

    [JsonPropertyName("time")]
    public long? Time { get; set; }

    [JsonIgnore]
    public GeoCoordinate? Position =>
        Latitude is { } latitude && Longitude is { } longitude &&
        new GeoCoordinate(latitude, longitude) is var coordinate && coordinate.IsValid
            ? coordinate
            : null;
}

public sealed class IvaoPilot
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("isMilitary")]
    public bool IsMilitary { get; set; }

    [JsonPropertyName("isWorldTour")]
    public bool IsWorldTour { get; set; }

    [JsonPropertyName("lastTrack")]
    public IvaoLastTrack? LastTrack { get; set; }

    [JsonPropertyName("flightPlan")]
    public IvaoFlightPlan? FlightPlan { get; set; }

    [JsonIgnore]
    public string Aircraft => FlightPlan?.Aircraft?.IcaoCode ?? FlightPlan?.AircraftId ?? "—";

    [JsonIgnore]
    public string Route => FlightPlan?.RouteLabel ?? "No flight plan";

    [JsonIgnore]
    public string State => LastTrack?.State ?? "Unknown";

    [JsonIgnore]
    public string AltitudeText => LastTrack?.Altitude is { } altitude
        ? altitude >= 18000 ? $"FL{altitude / 100:000}" : $"{altitude:N0} ft"
        : "—";

    [JsonIgnore]
    public string SpeedText => LastTrack?.GroundSpeed is { } speed ? $"{speed:N0} kt" : "—";

    [JsonIgnore]
    public string HeadingText => LastTrack?.Heading is { } heading ? $"{heading:000}°" : "—";

    [JsonIgnore]
    public string RemainingText => LastTrack?.ArrivalDistance is { } distance
        ? distance < 10 ? $"{distance:0.0} NM" : $"{distance:N0} NM"
        : "—";

    [JsonIgnore]
    public string TransponderText => LastTrack?.Transponder is { } code ? code.ToString("0000") : "—";

    [JsonIgnore]
    public bool IsOnGround => LastTrack?.OnGround == true;

    /// <summary>
    /// Estimated time of arrival from the remaining distance and the current
    /// ground speed. A flight on the ground, standing still, or with no distance
    /// left to run, has no estimate: a time derived from a taxi speed would be
    /// nonsense, and an arrival in the past would look like an error.
    /// </summary>
    public DateTimeOffset? EstimatedArrival(DateTimeOffset now)
    {
        if (LastTrack is not { ArrivalDistance: { } distance, GroundSpeed: { } speed })
        {
            return null;
        }

        if (IsOnGround || distance <= 0 || speed <= 0)
        {
            return null;
        }

        return now.AddMinutes(distance / speed * 60);
    }
}

public sealed class IvaoPilotSummary
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("isMilitary")]
    public bool IsMilitary { get; set; }

    [JsonPropertyName("isWorldTour")]
    public bool IsWorldTour { get; set; }

    [JsonPropertyName("lastTrack")]
    public IvaoLastTrack? LastTrack { get; set; }

    [JsonPropertyName("flightPlan")]
    public IvaoFlightPlan? FlightPlan { get; set; }
}
