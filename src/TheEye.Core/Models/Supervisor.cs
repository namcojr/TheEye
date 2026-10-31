using System.Text.Json.Serialization;

namespace TheEye.Core.Models;

/// <summary>
/// A member connected with supervisor rights
/// (<c>/v2/tracker/now/supervisors</c>). The feed describes the connection
/// rather than the traffic: the callsign the supervisor is connected with, the
/// member behind it and the staff positions they hold. A supervisor is not part
/// of the pilot or ATC lists, so their row is built from this record alone.
/// Like the other identity feeds it is authenticated.
/// </summary>
public sealed class IvaoSupervisor
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Seconds connected when the feed answered; the timestamp wins for display.</summary>
    [JsonPropertyName("time")]
    public long? Time { get; set; }

    /// <summary>True for a supervisor who asked IVAO not to be listed to the network.</summary>
    [JsonPropertyName("hideSupervisor")]
    public bool HideSupervisor { get; set; }

    /// <summary>The member behind the callsign: name, division and staff positions.</summary>
    [JsonPropertyName("user")]
    public IvaoSessionUser? User { get; set; }

    /// <summary>The name to show, falling back to the VID when IVAO sent no name.</summary>
    [JsonIgnore]
    public string Name => User is { HasName: true } user
        ? user.FullName
        : UserId > 0 ? $"VID {UserId}" : "IVAO member";

    [JsonIgnore]
    public string DivisionName => User?.DivisionName ?? string.Empty;

    /// <summary>"Director", the staff positions the member holds, comma separated.</summary>
    [JsonIgnore]
    public string StaffText => User?.StaffText ?? string.Empty;

    [JsonIgnore]
    public bool HasStaffPositions => StaffText.Length > 0;

    /// <summary>What the supervisor is connected with: flying, controlling or observing.</summary>
    [JsonIgnore]
    public string ConnectionLabel => ConnectionType?.ToUpperInvariant() switch
    {
        "PILOT" => "Flying",
        "ATC" => "Controlling",
        "OBS" => "Observing",
        _ => ConnectionType is { Length: > 0 } type ? type : "Connected"
    };

    /// <summary>The connection type in the short form the traffic lists use ("ATC", "OBS").</summary>
    [JsonIgnore]
    public string ConnectionCode => ConnectionType?.ToUpperInvariant() switch
    {
        "PILOT" => "PILOT",
        "ATC" => "ATC",
        "OBS" => "OBS",
        _ => "SUP"
    };

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
}
