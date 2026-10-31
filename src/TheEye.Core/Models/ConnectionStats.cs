using System.Text.Json.Serialization;

namespace TheEye.Core.Models;

public sealed class IvaoConnectionStats
{
    [JsonPropertyName("now")]
    public IvaoConnectionCounts Now { get; set; } = new();

    [JsonPropertyName("max")]
    public IvaoConnectionCounts Max { get; set; } = new();
}

public sealed class IvaoConnectionCounts
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("supervisors")]
    public int Supervisors { get; set; }

    [JsonPropertyName("atc")]
    public int Atc { get; set; }

    [JsonPropertyName("observers")]
    public int Observers { get; set; }

    [JsonPropertyName("pilots")]
    public int Pilots { get; set; }

    [JsonPropertyName("worldTours")]
    public int WorldTours { get; set; }

    [JsonPropertyName("followMes")]
    public int FollowMes { get; set; }

    [JsonPropertyName("uniqueUsers24h")]
    public int UniqueUsers24h { get; set; }
}
