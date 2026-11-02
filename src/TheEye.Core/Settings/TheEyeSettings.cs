namespace TheEye.Core.Settings;

using System.Globalization;
using TheEye.Core.Api;
using TheEye.Core.Maps;

public static class TheEyeThemes
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string MidnightBlue = "MidnightBlue";
    public const string Forest = "Forest";
    public const string Sunset = "Sunset";

    public static IReadOnlyList<string> All { get; } =
    [
        Dark,
        Light,
        MidnightBlue,
        Forest,
        Sunset
    ];

    public static bool IsKnown(string? theme) =>
        !string.IsNullOrWhiteSpace(theme) &&
        All.Contains(theme, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string? theme) =>
        All.FirstOrDefault(t => string.Equals(t, theme, StringComparison.OrdinalIgnoreCase)) ?? Dark;
}

/// <summary>A colour the user can paint their own or a friend's aircraft with.</summary>
public sealed record AircraftHighlightColor(string Name, string Label, string Hex);

public static class AircraftHighlightColors
{
    public const string DefaultOwn = "Green";
    public const string DefaultFriend = "Yellow";

    public static IReadOnlyList<AircraftHighlightColor> All { get; } =
    [
        new("Green", "Green", "#22C55E"),
        new("White", "White", "#FFFFFF"),
        new("Red", "Red", "#EF4444"),
        new("Yellow", "Yellow", "#FACC15"),
        new("Orange", "Orange", "#F97316"),
        new("Magenta", "Magenta", "#E040FB"),
        new("Cyan", "Cyan", "#22D3EE")
    ];

    public static AircraftHighlightColor? Find(string? name) =>
        All.FirstOrDefault(color => string.Equals(color.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// User preferences persisted as JSON in %AppData%\TheEyeIVAO\settings.json.
/// Keeping this in the core library means a future Linux shell can use the
/// exact same configuration.
/// </summary>
public sealed class TheEyeSettings
{
    public const int MinAircraftGlyphSize = 20;
    public const int MaxAircraftGlyphSize = 40;
    public const int DefaultAircraftGlyphSize = 25;

    public int AircraftGlyphSize { get; set; } = DefaultAircraftGlyphSize;

    /// <summary>
    /// Client id of the IVAO application that signs the member in. Empty when
    /// no application has been configured.
    /// </summary>
    public string OAuthClientId { get; set; } = string.Empty;

    /// <summary>Secret of the same IVAO application: optional, PKCE guards the exchange anyway.</summary>
    public string OAuthClientSecret { get; set; } = string.Empty;

    /// <summary>Loopback URL the browser returns to. It must be allowed by the IVAO application.</summary>
    public string OAuthRedirectUri { get; set; } = IvaoOAuthOptions.DefaultRedirectUri;

    /// <summary>Scopes requested at sign-in; IVAO grants them per application.</summary>
    public string OAuthScopes { get; set; } = IvaoOAuthOptions.DefaultScopes;

    /// <summary>
    /// The sign-in itself. Tokens are readable by the Windows account that runs
    /// the app.
    /// </summary>
    public string OAuthAccessToken { get; set; } = string.Empty;

    public string OAuthRefreshToken { get; set; } = string.Empty;

    /// <summary>ISO-8601 expiry of <see cref="OAuthAccessToken"/>; empty when unknown.</summary>
    public string OAuthExpiresAt { get; set; } = string.Empty;

    /// <summary>Who signed in, e.g. "123456 (Jane Doe)": a label for the UI, not a credential.</summary>
    public string OAuthAccount { get; set; } = string.Empty;

    public string Theme { get; set; } = TheEyeThemes.Dark;

    /// <summary>
    /// The basemap the map is drawn with. <see cref="MapBasemap.Offline"/> keeps
    /// the bundled country map; any other id streams that raster style from
    /// IVAO's tile service.
    /// </summary>
    public string MapStyle { get; set; } = MapBasemap.Offline;

    public int RefreshSeconds { get; set; } = 30;

    public bool ShowPilots { get; set; } = true;

    /// <summary>Draws every controller layer: sectors, sector labels and station badges.</summary>
    public bool ShowAtc { get; set; } = true;

    /// <summary>
    /// Moves the map onto whatever is picked on the map itself: an aircraft
    /// marker, a station badge or a sector. Picking a row in the traffic lists
    /// always centres the selection, whatever this is set to.
    /// </summary>
    public bool CenterOnMapClick { get; set; } = true;

    public double MapLatitude { get; set; } = 45;

    public double MapLongitude { get; set; } = 10;

    public double MapResolution { get; set; } = WorldMap.MaxZoomOutResolution;

    /// <summary>Colour of the signed-in member's own aircraft, by name in <see cref="AircraftHighlightColors"/>.</summary>
    public string OwnAircraftColor { get; set; } = AircraftHighlightColors.DefaultOwn;

    /// <summary>Colour of a friend's aircraft; never the same as <see cref="OwnAircraftColor"/>.</summary>
    public string FriendAircraftColor { get; set; } = AircraftHighlightColors.DefaultFriend;

    /// <summary>
    /// The VID of the signed-in member, read from <see cref="OAuthAccount"/>
    /// ("123456 (Jane Doe)"); 0 when nobody is signed in or the id is unknown.
    /// </summary>
    public long GetOwnUserId()
    {
        if (!HasOAuthTokens || string.IsNullOrWhiteSpace(OAuthAccount))
        {
            return 0;
        }

        var account = OAuthAccount.Trim();
        var length = 0;
        while (length < account.Length && char.IsAsciiDigit(account[length]))
        {
            length++;
        }

        return length > 0 &&
            (length == account.Length || account[length] == ' ') &&
            long.TryParse(account.AsSpan(0, length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : 0;
    }

    /// <summary>
    /// The colour an aircraft of the member is painted with, or null for the
    /// default. The user's own aircraft wins over the friend colour.
    /// </summary>
    public string? AircraftHighlightFor(long userId)
    {
        if (userId <= 0)
        {
            return null;
        }

        if (userId == GetOwnUserId())
        {
            return AircraftHighlightColors.Find(OwnAircraftColor)?.Hex;
        }

        return IsFriend(userId) ? AircraftHighlightColors.Find(FriendAircraftColor)?.Hex : null;
    }

    /// <summary>
    /// The members the user follows, by IVAO id (VID). A friend is marked in the
    /// traffic lists, collected on the FRIENDS tab, and keeps the name the app
    /// knows even before IVAO names the connection.
    /// </summary>
    public List<long> FriendIds { get; set; } = [];

    /// <summary>True when the member is one the user follows.</summary>
    public bool IsFriend(long userId) => userId > 0 && FriendIds.Contains(userId);

    /// <summary>Follows a member. Returns false when the id is unknown or already followed.</summary>
    public bool AddFriend(long userId)
    {
        if (userId <= 0 || FriendIds.Contains(userId))
        {
            return false;
        }

        FriendIds.Add(userId);
        FriendIds.Sort();
        return true;
    }

    /// <summary>Stops following a member. Returns false when it was not followed.</summary>
    public bool RemoveFriend(long userId) => userId > 0 && FriendIds.Remove(userId);

    /// <summary>Follows a member that is not followed yet and stops following one that is.</summary>
    public bool ToggleFriend(long userId) =>
        IsFriend(userId) ? !RemoveFriend(userId) : AddFriend(userId);

    /// <summary>True when a sign-in token is stored, whatever its age.</summary>
    public bool HasOAuthTokens =>
        !string.IsNullOrWhiteSpace(OAuthAccessToken) || !string.IsNullOrWhiteSpace(OAuthRefreshToken);

    public DateTimeOffset? GetOAuthExpiry() =>
        DateTimeOffset.TryParse(
            OAuthExpiresAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var expiresAt)
            ? expiresAt
            : null;

    public void SetOAuthExpiry(DateTimeOffset? expiresAt) =>
        OAuthExpiresAt = expiresAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            ?? string.Empty;

    /// <summary>Drops the sign-in, e.g. when IVAO revoked the grant.</summary>
    public void ClearOAuthTokens()
    {
        OAuthAccessToken = string.Empty;
        OAuthRefreshToken = string.Empty;
        OAuthExpiresAt = string.Empty;
        OAuthAccount = string.Empty;
    }

    public TheEyeSettings Clone() => new()
    {
        OAuthClientId = OAuthClientId,
        OAuthClientSecret = OAuthClientSecret,
        OAuthRedirectUri = OAuthRedirectUri,
        OAuthScopes = OAuthScopes,
        OAuthAccessToken = OAuthAccessToken,
        OAuthRefreshToken = OAuthRefreshToken,
        OAuthExpiresAt = OAuthExpiresAt,
        OAuthAccount = OAuthAccount,
        Theme = Theme,
        MapStyle = MapStyle,
        RefreshSeconds = RefreshSeconds,
        AircraftGlyphSize = AircraftGlyphSize,
        ShowPilots = ShowPilots,
        ShowAtc = ShowAtc,
        CenterOnMapClick = CenterOnMapClick,
        MapLatitude = MapLatitude,
        MapLongitude = MapLongitude,
        MapResolution = MapResolution,
        OwnAircraftColor = OwnAircraftColor,
        FriendAircraftColor = FriendAircraftColor,
        FriendIds = [.. FriendIds]
    };

    public void CopyFrom(TheEyeSettings other)
    {
        OAuthClientId = other.OAuthClientId;
        OAuthClientSecret = other.OAuthClientSecret;
        OAuthRedirectUri = other.OAuthRedirectUri;
        OAuthScopes = other.OAuthScopes;
        OAuthAccessToken = other.OAuthAccessToken;
        OAuthRefreshToken = other.OAuthRefreshToken;
        OAuthExpiresAt = other.OAuthExpiresAt;
        OAuthAccount = other.OAuthAccount;
        Theme = other.Theme;
        MapStyle = other.MapStyle;
        RefreshSeconds = other.RefreshSeconds;
        AircraftGlyphSize = other.AircraftGlyphSize;
        ShowPilots = other.ShowPilots;
        ShowAtc = other.ShowAtc;
        CenterOnMapClick = other.CenterOnMapClick;
        MapLatitude = other.MapLatitude;
        MapLongitude = other.MapLongitude;
        MapResolution = other.MapResolution;
        OwnAircraftColor = other.OwnAircraftColor;
        FriendAircraftColor = other.FriendAircraftColor;
        FriendIds = [.. other.FriendIds];
    }

    public void Normalize()
    {
        OAuthClientId = OAuthClientId?.Trim() ?? string.Empty;
        OAuthClientSecret = OAuthClientSecret?.Trim() ?? string.Empty;
        OAuthRedirectUri = string.IsNullOrWhiteSpace(OAuthRedirectUri)
            ? IvaoOAuthOptions.DefaultRedirectUri
            : OAuthRedirectUri.Trim();
        OAuthScopes = string.IsNullOrWhiteSpace(OAuthScopes)
            ? IvaoOAuthOptions.DefaultScopes
            : OAuthScopes.Trim();
        OAuthAccessToken = OAuthAccessToken?.Trim() ?? string.Empty;
        OAuthRefreshToken = OAuthRefreshToken?.Trim() ?? string.Empty;
        OAuthExpiresAt = OAuthExpiresAt?.Trim() ?? string.Empty;
        OAuthAccount = OAuthAccount?.Trim() ?? string.Empty;
        Theme = TheEyeThemes.Normalize(Theme);
        MapStyle = MapBasemap.Normalize(MapStyle);
        AircraftGlyphSize = Math.Clamp(AircraftGlyphSize, MinAircraftGlyphSize, MaxAircraftGlyphSize);

        OwnAircraftColor = AircraftHighlightColors.Find(OwnAircraftColor)?.Name ?? AircraftHighlightColors.DefaultOwn;
        FriendAircraftColor = AircraftHighlightColors.Find(FriendAircraftColor)?.Name ?? AircraftHighlightColors.DefaultFriend;
        if (string.Equals(OwnAircraftColor, FriendAircraftColor, StringComparison.Ordinal))
        {
            FriendAircraftColor = AircraftHighlightColors.All
                .First(color => !string.Equals(color.Name, OwnAircraftColor, StringComparison.Ordinal)).Name;
        }

        if (RefreshSeconds is < 10 or > 600)
        {
            RefreshSeconds = 30;
        }

        if (double.IsNaN(MapLatitude) || MapLatitude is < -85 or > 85)
        {
            MapLatitude = 45;
        }

        if (double.IsNaN(MapLongitude) || MapLongitude is < -180 or > 180)
        {
            MapLongitude = 10;
        }

        if (double.IsNaN(MapResolution) || MapResolution is < WorldMap.MinResolution or > WorldMap.MaxZoomOutResolution)
        {
            MapResolution = WorldMap.MaxZoomOutResolution;
        }

        // A settings file edited by hand could repeat an id or hold a placeholder.
        FriendIds = (FriendIds ?? [])
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }
}
