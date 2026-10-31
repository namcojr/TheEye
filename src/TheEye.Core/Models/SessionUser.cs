using System.Text.Json.Serialization;
using TheEye.Core.Reference;

namespace TheEye.Core.Models;

/// <summary>The member behind a session: name, home division and ratings.</summary>
public sealed class IvaoSessionUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; set; }

    [JsonPropertyName("divisionId")]
    public string? DivisionId { get; set; }

    [JsonPropertyName("rating")]
    public IvaoUserRating? Rating { get; set; }

    [JsonIgnore]
    public string FullName
    {
        get
        {
            var name = string.Join(' ', new[] { FirstName, LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
            return name.Length > 0 ? name : "IVAO member";
        }
    }

    [JsonIgnore]
    public string DivisionName => DivisionCatalog.Name(DivisionId);

    /// <summary>Flag of the member's division, or null when it has none.</summary>
    [JsonIgnore]
    public string? FlagUrl => DivisionCatalog.FlagUrl(DivisionId);

    [JsonIgnore]
    public string? PilotBadgeUrl => RatingCatalog.PilotBadgeUrl(Rating?.PilotRating?.Id);

    [JsonIgnore]
    public string? AtcBadgeUrl => RatingCatalog.AtcBadgeUrl(Rating?.AtcRating?.Id);

    [JsonIgnore]
    public string PilotRatingText => Rating?.PilotRating?.ShortName ?? string.Empty;

    [JsonIgnore]
    public string AtcRatingText => Rating?.AtcRating?.ShortName ?? string.Empty;
}

/// <summary>The ratings IVAO gave the member, as the session record reports them.</summary>
public sealed class IvaoUserRating
{
    [JsonPropertyName("isAtc")]
    public bool IsAtc { get; set; }

    [JsonPropertyName("isPilot")]
    public bool IsPilot { get; set; }

    [JsonPropertyName("pilotRating")]
    public IvaoRating? PilotRating { get; set; }

    [JsonPropertyName("atcRating")]
    public IvaoRating? AtcRating { get; set; }

    [JsonPropertyName("networkRating")]
    public IvaoNetworkRating? NetworkRating { get; set; }
}

/// <summary>A pilot or controller rating: id, name and the short form ("FS3").</summary>
public sealed class IvaoRating
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("shortName")]
    public string? ShortName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>A network (supervisor) rating, which has no short name.</summary>
public sealed class IvaoNetworkRating
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
