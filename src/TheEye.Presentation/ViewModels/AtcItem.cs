using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Reference;
using TheEye.Core.Services;
using TheEye.Presentation.Services;
using TheEye.Presentation.Theming;

namespace TheEye.Presentation.ViewModels;

public sealed class AtcItem : ObservableObject
{
    /// <summary>The colour a row that carries no mark binds, i.e. "no wash".</summary>
    private const string TransparentTint = "#00000000";

    private readonly IImageCache _images;
    private IvaoAtc _atc;
    private IvaoSessionDetail? _detail;
    private int? _detailStatus;
    private string? _detailError;
    private bool _detailRequested;
    private object? _ratingBadge;
    private object? _flagImage;
    private bool _isSelected;
    private bool _isFriend;
    private bool _isSupervisor;

    public AtcItem(IvaoAtc atc, IImageCache images)
    {
        _atc = atc;
        _images = images;
        Refresh();
    }

    public IvaoAtc Atc => _atc;

    public long Id => _atc.Id;

    public string Callsign => _atc.Callsign;

    /// <summary>The name IVAO publishes for the station ("Otopeni Tower").</summary>
    public string StationName => _atc.AtcCallsign;

    public string Facility => _atc.Facility;

    public string FacilityName => FacilityPalette.DisplayName(_atc.Facility);

    public string Frequency => _atc.FrequencyText;

    public string Position => _atc.PositionText;

    public string AirportIcao => _atc.AirportIcao;

    public string AirportName => _atc.AirportName;

    public string City => _atc.City;

    public string Country => _atc.Country;

    /// <summary>The airport country spelled out, which is what the card shows.</summary>
    public string CountryName => _atc.CountryName;

    /// <summary>
    /// False for a control centre, which covers a region rather than an airport:
    /// the card then hides the city and country cells instead of printing dashes.
    /// </summary>
    public bool HasAirport => _atc.HasAirport;

    public string Scope => _atc.ScopeLabel;

    public string SectorPoints => _atc.SectorPointsText;

    public string PositionText => _atc.Position is { } position
        ? $"{position.Latitude:0.0000}, {position.Longitude:0.0000}"
        : "—";

    public string ConnectionType => _atc.ConnectionType ?? "—";

    public bool IsMilitary => _atc.IsMilitary;

    public bool HasSector => _atc.PolygonPoints.Count >= 3;

    /// <summary>
    /// The IVAO member id (VID) of the controller, known before the roster
    /// record arrives. It is the key of everything that follows a member instead
    /// of a session: the friend list and the supervisor ring on the map.
    /// </summary>
    public long UserId => _detail?.UserId is { } id && id > 0 ? id : _atc.UserId;

    /// <summary>
    /// The controller behind the station. The identity feed of the traffic list
    /// names every connected station, so the name is known before the card is
    /// opened; the roster record of the selected session is preferred when it
    /// named the controller, but it withholds the name for most sessions and
    /// must not hide the one the list already has. Empty means IVAO named the
    /// member nowhere, which is what a signed-out run looks like.
    /// </summary>
    public string MemberName => _detail?.User is { HasName: true } detail
        ? detail.FullName
        : _atc.MemberName;

    public bool HasMemberName => MemberName.Length > 0;

    /// <summary>The name for a cell that would look broken when left empty.</summary>
    public string MemberText => HasMemberName ? MemberName : "—";

    /// <summary>
    /// True when the controller is on the friend list of the settings. The shell
    /// sets it after every refresh, because the list can change without the
    /// traffic changing.
    /// </summary>
    public bool IsFriend
    {
        get => _isFriend;
        set
        {
            if (SetProperty(ref _isFriend, value))
            {
                OnPropertyChanged(nameof(CallsignLabel));
            }
        }
    }

    /// <summary>
    /// True when IVAO lists the controller with supervisor rights; the station
    /// badge then carries the supervisor ring on the map and the row of the list
    /// the wash of a watched member.
    /// </summary>
    public bool IsSupervisor
    {
        get => _isSupervisor;
        set
        {
            if (SetProperty(ref _isSupervisor, value))
            {
                OnPropertyChanged(nameof(RowTint));
            }
        }
    }

    /// <summary>
    /// The wash the traffic list paints behind the whole row of a supervisor:
    /// the pink of the ring the map draws around the badge, at a low opacity. A
    /// station nobody watches binds a fully transparent colour, so the view
    /// needs no trigger to un-tint it.
    /// </summary>
    public string RowTint => IsSupervisor ? ThemeCatalog.SupervisorRowTint : TransparentTint;

    /// <summary>
    /// The callsign as the traffic list prints it. A star in front of the
    /// callsign marks a friend, because a three-column list has no room for a
    /// column of its own and the star survives a narrow panel.
    /// </summary>
    public string CallsignLabel => IsFriend ? $"★ {Callsign}" : Callsign;

    /// <summary>The IVAO member id (VID) of the controller.</summary>
    public string ControllerId
    {
        get
        {
            var id = _detail?.UserId ?? 0;
            if (id <= 0)
            {
                id = _atc.UserId;
            }

            return id > 0 ? id.ToString("0") : "—";
        }
    }

    /// <summary>
    /// The controller behind the station, once IVAO described the session. The
    /// identity block is only drawn when this is true, because a blank header
    /// with a badge slot would look like a failure rather than an answer that has
    /// not arrived.
    /// </summary>
    public bool HasProfile => _detail?.User is not null;

    /// <summary>
    /// The name of the card header. The record of the session may leave the name
    /// out, so the one the traffic list already has is printed instead of the
    /// placeholder the roster record would produce.
    /// </summary>
    public string ProfileName => MemberName is { Length: > 0 } name ? name : "—";

    /// <summary>The division the controller belongs to, spelled out from its code.</summary>
    public string HomeText => _detail?.User?.DivisionName is { Length: > 0 } name ? name : "—";

    /// <summary>Controller rating in short form ("AS3"), the level IVAO grants.</summary>
    public string RatingText
    {
        get
        {
            var rating = _detail?.User?.Rating;
            if (!string.IsNullOrWhiteSpace(rating?.AtcRating?.ShortName))
            {
                return rating.AtcRating.ShortName;
            }

            if (!string.IsNullOrWhiteSpace(rating?.AtcRating?.Name))
            {
                return rating.AtcRating.Name;
            }

            return !string.IsNullOrWhiteSpace(rating?.PilotRating?.ShortName) ? rating.PilotRating.ShortName : "—";
        }
    }

    /// <summary>The rating spelled out ("Advanced ATC Trainee"), shown on hover.</summary>
    public string RatingName => _detail?.User?.Rating?.AtcRating?.Name is { Length: > 0 } name ? name : "—";

    /// <summary>
    /// "Aurora/mac 1.0.38b", the client the controller is connected with. The
    /// summary the tracker delivers already names it, so the cell is filled
    /// before the roster record arrives; the record wins because it carries the
    /// version too.
    /// </summary>
    public string SoftwareText => _detail?.SoftwareText is { Length: > 0 } software
        ? software
        : _atc.SoftwareText is { Length: > 0 } summary ? summary : "—";

    public string ServerText => _detail?.ServerText is { Length: > 0 } server
        ? server
        : _atc.ServerText is { Length: > 0 } summary ? summary : "—";

    /// <summary>
    /// Time on position, counted from the connection timestamp rather than from
    /// the count that came with the record, so a cached answer keeps counting up.
    /// The summary knows the connection as well, so the cell is filled before the
    /// card has asked for the session.
    /// </summary>
    public string OnlineText =>
        (_detail?.Online(DateTimeOffset.Now) ?? _atc.Online(DateTimeOffset.Now)) is { } online
            ? DurationText.Format(online)
            : "—";

    public string ConnectedText => _detail?.CreatedAt is { } connectedAt
        ? connectedAt.ToLocalTime().ToString("HH:mm")
        : "—";

    /// <summary>The controller rating badge, drawn once the image is decoded.</summary>
    public object? RatingBadge
    {
        get => _ratingBadge;
        private set => SetProperty(ref _ratingBadge, value);
    }

    /// <summary>The flag of the controller's division, drawn once the image is decoded.</summary>
    public object? FlagImage
    {
        get => _flagImage;
        private set => SetProperty(ref _flagImage, value);
    }

    /// <summary>
    /// Why the card has no identity block. The roster endpoint is authenticated,
    /// so the common cause is a missing sign-in or key and the text says which one
    /// to add; nothing is reported while the first answer is still on its way.
    /// </summary>
    public string ProfileNotice
    {
        get
        {
            if (_detail is not null || !_detailRequested)
            {
                return string.Empty;
            }

            return _detailStatus switch
            {
                401 or 403 =>
                    "IVAO refused the controller details (401 not authenticated). Sign in with IVAO in Settings, or add a personal API key, to see who is on the frequency.",
                404 => "IVAO has no record of this session (404) — the controller may have just disconnected.",
                { } status => $"IVAO answered {status} for this session, so the controller details are unavailable.",
                _ when !string.IsNullOrWhiteSpace(_detailError) =>
                    $"The controller details could not be loaded — {_detailError}",
                _ => "The controller details could not be loaded."
            };
        }
    }

    public bool HasProfileNotice => ProfileNotice.Length > 0;

    /// <summary>
    /// The colour that identifies the facility, as "#RRGGBB": green for a ground
    /// station, up through the tower and approach shades to the blue of a centre.
    /// The view turns the string into its own brush so the same colour reaches
    /// both shells.
    /// </summary>
    public string AccentColor { get; private set; } = "#808080";

    /// <summary>The same colour at a quarter opacity, for the pill behind the facility name.</summary>
    public string AccentSoftColor { get; private set; } = "#00000000";

    public string SearchText { get; private set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void Update(IvaoAtc atc)
    {
        _atc = atc;
        Refresh();
    }

    /// <summary>
    /// Layers the roster record of the session on top of the station summary: the
    /// controller behind the callsign, the division, the rating and the client they
    /// are connected with. The record arrives after the card is already on screen,
    /// so everything it fills in is a plain property change; a failure leaves the
    /// station details the summary already gave and explains the gap instead.
    /// </summary>
    public void ApplySessionDetail(SessionDetailResult result)
    {
        _detailStatus = result.StatusCode;
        _detailError = result.Error;
        _detailRequested = true;

        if (!ReferenceEquals(_detail, result.Session))
        {
            _detail = result.Session;

            // The record of a single session names the member for a minority of
            // sessions; the identity feed of the traffic list knows the name of
            // every connection, so the header prints it instead of "IVAO member".
            _detail?.User?.ApplyNameFrom(_atc.User);

            RatingBadge = null;
            FlagImage = null;

            if (_detail is { } detail)
            {
                LoadImages(detail);
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        var (red, green, blue) = ThemeCatalog.ParseRgb(FacilityPalette.ForFacility(_atc.Facility));
        AccentColor = ThemeCatalog.ToHex(0xFF, red, green, blue);
        AccentSoftColor = ThemeCatalog.ToHex(40, red, green, blue);

        SearchText = string.Join(' ', new[]
        {
            Callsign,
            StationName,
            FacilityName,
            Frequency,
            AirportIcao,
            AirportName,
            City,
            CountryName,
            Scope,
            ControllerId,
            MemberName,
            _detail?.User?.DivisionName,
            _detail?.User?.StaffText,
            _detail?.SoftwareText,
            _atc.SoftwareText
        }).ToLowerInvariant();

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Paints the controller badge and the division flag: whatever is already
    /// decoded is set at once, and whatever is not is fetched in the background
    /// and dropped in when it arrives.
    /// </summary>
    private void LoadImages(IvaoSessionDetail detail)
    {
        var badgeUrl = detail.User?.AtcBadgeUrl;
        var flagUrl = detail.User?.FlagUrl;

        RatingBadge = _images.TryGet(badgeUrl);
        FlagImage = _images.TryGet(flagUrl);

        _ = SessionImageBinder.ApplyAsync(_images, badgeUrl, image => RatingBadge = image);
        _ = SessionImageBinder.ApplyAsync(_images, flagUrl, image => FlagImage = image);
    }
}
