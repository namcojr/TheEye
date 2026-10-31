namespace TheEye.Core.Reference;

/// <summary>
/// The rating badges IVAO publishes for its members. The tracker reports a
/// rating as an id and a short name ("FS3"), so the card needs this base address
/// to show the badge the member earned. A rating that has no published image
/// answers 404, which the image cache treats as "nothing to draw".
/// </summary>
public static class RatingCatalog
{
    private const string BaseAddress = "https://www.ivao.aero/data/images/ratings/";

    /// <summary>Pilot rating badge ("FS3"), or null when the member has no pilot rating.</summary>
    public static string? PilotBadgeUrl(int? ratingId) =>
        ratingId is > 0 ? $"{BaseAddress}pilot/{ratingId.Value}.gif" : null;

    /// <summary>ATC rating badge, or null when the member has no controller rating.</summary>
    public static string? AtcBadgeUrl(int? ratingId) =>
        ratingId is > 0 ? $"{BaseAddress}atc/{ratingId.Value}.gif" : null;
}
