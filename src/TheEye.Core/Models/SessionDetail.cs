using System.Text.Json.Serialization;
using TheEye.Core.Json;

namespace TheEye.Core.Models;

/// <summary>
/// The full record of one live session (<c>/v2/tracker/sessions/{id}</c>). The
/// tracker summaries carry the flight plan and the last position only; this is
/// the answer to "who is flying, since when and with which software", which is
/// what the pilot card shows. The endpoint is authenticated, so callers must
/// cope with a 401 the way the recorded-track feed does.
/// </summary>
public sealed class IvaoSessionDetail
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("callsign")]
    public string Callsign { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public long UserId { get; set; }

    [JsonPropertyName("connectionType")]
    public string? ConnectionType { get; set; }

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    /// <summary>Time online in seconds, as reported when the record was made.</summary>
    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("softwareTypeId")]
    public string? SoftwareTypeId { get; set; }

    [JsonPropertyName("softwareVersion")]
    public string? SoftwareVersion { get; set; }

    [JsonPropertyName("sandbagging")]
    public bool Sandbagging { get; set; }

    [JsonPropertyName("isMilitary")]
    public bool IsMilitary { get; set; }

    [JsonPropertyName("isWorldTour")]
    public bool IsWorldTour { get; set; }

    [JsonPropertyName("softwareType")]
    public IvaoSoftwareSummary? SoftwareType { get; set; }

    [JsonPropertyName("user")]
    public IvaoSessionUser? User { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("isDoubleConnected")]
    public bool IsDoubleConnected { get; set; }

    [JsonPropertyName("hideSupervisor")]
    public bool HideSupervisor { get; set; }

    [JsonPropertyName("pilotSession")]
    public IvaoPilotSession? PilotSession { get; set; }

    [JsonPropertyName("atcSession")]
    public IvaoAtcSession? AtcSession { get; set; }

    [JsonIgnore]
    public bool IsPilot => string.Equals(ConnectionType, "PILOT", StringComparison.OrdinalIgnoreCase);

    /// <summary>"Altitude 0.4.1", the client the member is connected with.</summary>
    [JsonIgnore]
    public string SoftwareText
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(SoftwareType?.Name) ? SoftwareTypeId : SoftwareType.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(SoftwareVersion) ? name : $"{name} {SoftwareVersion}";
        }
    }

    [JsonIgnore]
    public string SimulatorText => PilotSession?.SimulatorId ?? string.Empty;

    [JsonIgnore]
    public string ServerText => ServerId ?? string.Empty;

    /// <summary>
    /// How long the member has been connected. The record carries both the
    /// connection timestamp and the seconds online at the time of the answer;
    /// the timestamp wins so a cached record still counts up.
    /// </summary>
    public TimeSpan? Online(DateTimeOffset now)
    {
        if (CreatedAt is { } connectedAt)
        {
            var elapsed = now - connectedAt;
            return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        }

        return Time > 0 ? TimeSpan.FromSeconds(Time) : null;
    }
}

/// <summary>The client software a member is connected with.</summary>
public sealed class IvaoSoftwareSummary
{
    /// <summary>
    /// IVAO identifies the client with a slug ("altitude/win", "aurora/mac")
    /// and only then repeats it as <c>softwareTypeId</c>; older records carried a
    /// number there instead. Reading the id as text accepts both, because the
    /// record the card is built from must not be lost over a key it never shows.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>The pilot half of a session: which simulator and livery is in use.</summary>
public sealed class IvaoPilotSession
{
    [JsonPropertyName("simulatorId")]
    public string? SimulatorId { get; set; }

    [JsonPropertyName("textureId")]
    public int? TextureId { get; set; }
}
