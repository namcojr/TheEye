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

    /// <summary>
    /// Staff positions the member holds, e.g. the director of a division. Only
    /// the supervisor feed carries them; a session record has none.
    /// </summary>
    [JsonPropertyName("staffPositions")]
    public List<IvaoStaffPosition>? StaffPositions { get; set; }

    /// <summary>True when IVAO named the member, which is what a card shows instead of the VID.</summary>
    [JsonIgnore]
    public bool HasName =>
        !string.IsNullOrWhiteSpace(FirstName) || !string.IsNullOrWhiteSpace(LastName);

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

    /// <summary>"Director, Training Advisor", the staff positions spelled out.</summary>
    [JsonIgnore]
    public string StaffText => StaffPositions is { Count: > 0 } positions
        ? string.Join(", ", positions
            .Select(position => position.Label)
            .Where(label => label.Length > 0))
        : string.Empty;

    [JsonIgnore]
    public bool HasStaffPositions => StaffText.Length > 0;
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

/// <summary>
/// A staff position the member holds, e.g. <c>PT-DIR</c> for the director of
/// the Portuguese division. The supervisor feed nests the position and the
/// division; the label flattens both into something a row can print.
/// </summary>
public sealed class IvaoStaffPosition
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("staffPositionId")]
    public string? StaffPositionId { get; set; }

    [JsonPropertyName("staffPosition")]
    public IvaoStaffPositionInfo? StaffPosition { get; set; }

    [JsonPropertyName("division")]
    public IvaoStaffDivision? Division { get; set; }

    /// <summary>
    /// "Director (Portugal)", the position spelled out, or the code when IVAO
    /// sent no description of it.
    /// </summary>
    [JsonIgnore]
    public string Label
    {
        get
        {
            var name = StaffPosition?.Name is { Length: > 0 } spelled ? spelled : StaffPositionId ?? Id;
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            return Division?.Name is { Length: > 0 } division ? $"{name} ({division})" : name;
        }
    }
}

/// <summary>The name and description of a staff position ("Director").</summary>
public sealed class IvaoStaffPositionInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>The division a staff position belongs to ("Portugal").</summary>
public sealed class IvaoStaffDivision
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
