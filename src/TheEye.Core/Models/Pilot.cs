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

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Seconds connected when the feed answered; the timestamp wins for display.</summary>
    [JsonPropertyName("time")]
    public long? Time { get; set; }

    [JsonPropertyName("pilotSession")]
    public IvaoPilotSession? PilotSession { get; set; }

    /// <summary>The client the pilot flies with ("Altitude/win"), on the full feed only.</summary>
    [JsonPropertyName("softwareType")]
    public IvaoSoftwareSummary? SoftwareType { get; set; }

    /// <summary>
    /// The member behind the callsign. Only the authenticated feed
    /// (<c>/v2/tracker/now/pilots</c>) carries it, so it is null on a summary and
    /// is layered on by <see cref="ApplyMember"/> once that feed has answered.
    /// </summary>
    [JsonPropertyName("user")]
    public IvaoSessionUser? User { get; set; }

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

    [JsonIgnore]
    public bool HasMember => User is { Id: > 0 };

    /// <summary>"Ana Silva", or the VID when IVAO sent no name for the member.</summary>
    [JsonIgnore]
    public string MemberName => User is { HasName: true } member ? member.FullName : string.Empty;

    /// <summary>The client the pilot flies with ("Altitude", "X-Plane"), empty when unknown.</summary>
    [JsonIgnore]
    public string SoftwareText => SoftwareType?.Name ?? string.Empty;

    /// <summary>The simulator the pilot is in ("MSFS", "X-Plane 12"), empty when unknown.</summary>
    [JsonIgnore]
    public string SimulatorText => PilotSession?.SimulatorId ?? string.Empty;

    /// <summary>"WS1-EUW2", the server the pilot is connected to, empty when unknown.</summary>
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
    /// lists only show who is flying after this has run.
    /// </summary>
    public void ApplyMember(IvaoPilot identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        User ??= identity.User;
        SoftwareType ??= identity.SoftwareType;
        PilotSession ??= identity.PilotSession;
        ServerId ??= identity.ServerId;
        CreatedAt ??= identity.CreatedAt;
        Time ??= identity.Time;
    }

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
