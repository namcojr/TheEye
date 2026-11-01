using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Reference;
using TheEye.Core.Services;
using TheEye.Core.Settings;
using TheEye.Presentation.Services;
using TheEye.Presentation.Theming;

namespace TheEye.Presentation.ViewModels;

public sealed class PilotItem : ObservableObject
{
    /// <summary>The colour a row that carries no mark binds, i.e. "no wash".</summary>
    private const string TransparentTint = "#00000000";

    private readonly IAircraftIconCache _icons;
    private readonly IImageCache _images;
    private IvaoPilot _pilot;
    private object? _icon;
    private AircraftGlyph? _iconGlyph;
    private IvaoSessionDetail? _detail;
    private int? _detailStatus;
    private string? _detailError;
    private bool _detailRequested;
    private IvaoFlightPlan? _flightPlan;
    private bool _flightPlanRequested;
    private int? _flightPlanStatus;
    private string? _flightPlanError;
    private object? _ratingBadge;
    private object? _flagImage;
    private double _x;
    private double _y;
    private bool _isVisible;
    private bool _isSelected;
    private bool _isFriend;
    private bool _isSupervisor;
    private string _markerColor = string.Empty;
    private int _glyphSize = TheEyeSettings.DefaultAircraftGlyphSize;

    public int GlyphSize
    {
        get => _glyphSize;
        set
        {
            if (!SetProperty(ref _glyphSize, Math.Clamp(value,
                TheEyeSettings.MinAircraftGlyphSize, TheEyeSettings.MaxAircraftGlyphSize))) return;
            OnPropertyChanged(nameof(DisplayGlyphSize));
            OnPropertyChanged(nameof(MarkerSize));
            OnPropertyChanged(nameof(HaloSize));
        }
    }

    public int DisplayGlyphSize => GlyphSize + (IsSelected ? 3 : 0);

    public int MarkerSize => Math.Max(26, GlyphSize + 5);

    public int HaloSize => MarkerSize - 1;

    public PilotItem(IvaoPilot pilot, IAircraftIconCache icons, IImageCache images)
    {
        _pilot = pilot;
        _icons = icons;
        _images = images;
        Refresh();
        LoadImages(_pilot.User);
    }

    public IvaoPilot Pilot => _pilot;

    public long Id => _pilot.Id;

    public string Callsign => _pilot.Callsign;

    public string Aircraft => _pilot.Aircraft;

    public string RouteLabel => _pilot.Route;

    public string Departure => Plan?.DepartureIcao ?? "????";

    public string Arrival => Plan?.ArrivalIcao ?? "????";

    public string Altitude => _pilot.AltitudeText;

    public string Speed => _pilot.SpeedText;

    public string Heading => _pilot.HeadingText;

    public int HeadingDegrees => AircraftIconCatalog.NormalizeHeading(_pilot.LastTrack?.Heading);

    public AircraftGlyph Glyph => AircraftIconCatalog.Classify(
        _flightPlan is { } plan &&
        (!string.IsNullOrWhiteSpace(plan.Aircraft?.IcaoCode) ||
         !string.IsNullOrWhiteSpace(plan.AircraftId) ||
         string.Equals(plan.Aircraft?.Description?.Trim(), "Helicopter", StringComparison.OrdinalIgnoreCase))
            ? plan
            : _pilot.FlightPlan);

    public string State => _pilot.State;

    public string Transponder => _pilot.TransponderText;

    public string Remaining => _pilot.RemainingText;

    public bool IsOnGround => _pilot.IsOnGround;

    public bool IsMilitary => _pilot.IsMilitary;

    public bool IsWorldTour => _pilot.IsWorldTour;

    /// <summary>
    /// The IVAO member id (VID) of the flight, known before the roster record
    /// arrives. It is the key of everything that follows a member instead of a
    /// session: the friend list and the supervisor mark.
    /// </summary>
    public long UserId => _detail?.UserId is { } id && id > 0 ? id : _pilot.UserId;

    /// <summary>
    /// The member behind the callsign. The identity feed of the traffic list
    /// names every connected pilot, so the name is known before the card is
    /// opened; the roster record of the selected session is preferred when it
    /// named the member, but it withholds the name for most sessions and must
    /// not hide the one the list already has. Empty means IVAO named the member
    /// nowhere, which is what a signed-out run looks like.
    /// </summary>
    public string MemberName => _detail?.User is { HasName: true } detail
        ? detail.FullName
        : _pilot.MemberName;

    public bool HasMemberName => MemberName.Length > 0;

    /// <summary>The name for a cell that would look broken when left empty.</summary>
    public string MemberText => HasMemberName ? MemberName : MissingProfileValue;

    /// <summary>
    /// True when the member is on the friend list of the settings. The shell
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
    /// True when IVAO lists the member with supervisor rights. The traffic list
    /// keeps the callsign of the position; the row carries the mark instead of a
    /// glyph beside the callsign, so it reads at a glance.
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
    /// row of a pilot nobody watches binds a fully transparent colour, so the
    /// view needs no trigger to un-tint it.
    /// </summary>
    public string RowTint => IsSupervisor ? ThemeCatalog.SupervisorRowTint : TransparentTint;

    /// <summary>
    /// The callsign as the traffic list prints it. A star in front of the
    /// callsign marks a friend, because a three-column list has no room for a
    /// column of its own and the star survives a narrow panel.
    /// </summary>
    public string CallsignLabel => IsFriend ? $"★ {Callsign}" : Callsign;

    /// <summary>
    /// The "#RRGGBB" the map pin is painted with when the aircraft is the user's
    /// own or a friend's; empty leaves the theme's text colour.
    /// </summary>
    public string MarkerColor
    {
        get => _markerColor;
        set
        {
            if (SetProperty(ref _markerColor, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasMarkerColor));
            }
        }
    }

    public bool HasMarkerColor => _markerColor.Length > 0;

    public bool HasFlightPlan => Plan is not null;

    /// <summary>
    /// The plan to read: the full filing once IVAO described it, otherwise the
    /// trimmed copy embedded in the tracker summary. The summary only carries the
    /// airports, the aircraft and the route, so the card fills in the rest as
    /// soon as <see cref="ApplyFlightPlan"/> has the answer.
    /// </summary>
    private IvaoFlightPlan? Plan => _flightPlan ?? _pilot.FlightPlan;

    public string FlightRules => Plan?.FlightRules is { Length: > 0 } rules ? rules : MissingFlightPlanValue;

    public string FlightType => Plan?.FlightType is { Length: > 0 } type ? type : MissingFlightPlanValue;

    public string CruisingLevel => _flightPlan?.Level is { Length: > 0 } level ? level : MissingFlightPlanValue;

    public string CruisingSpeed => _flightPlan?.Speed is { Length: > 0 } speed ? speed : MissingFlightPlanValue;

    public string EstimatedEnroute => _flightPlan?.EetSeconds is { } eet
        ? DurationText.FromSeconds(eet)
        : MissingFlightPlanValue;

    public string Endurance => _flightPlan?.EnduranceSeconds is { } endurance
        ? DurationText.FromSeconds(endurance)
        : MissingFlightPlanValue;

    public string Alternate => Plan?.AlternateIcao ?? MissingFlightPlanValue;

    public string Alternate2 => Plan?.Alternate2Icao ?? MissingFlightPlanValue;

    /// <summary>The off-block time the flight was filed for, in UTC.</summary>
    public string FiledDepartureText => _flightPlan?.DepartureTimeUtc is { } time
        ? time.UtcDateTime.ToString("HH:mm") + "z"
        : MissingFlightPlanValue;

    /// <summary>When the aircraft actually left the blocks, in UTC.</summary>
    public string ActualDepartureText => _flightPlan?.ActualDepartureTimeUtc is { } time
        ? time.UtcDateTime.ToString("HH:mm") + "z"
        : MissingFlightPlanValue;

    public string PeopleOnBoardText => _flightPlan?.PeopleOnBoard is { } people ? $"{people:N0}" : MissingFlightPlanValue;

    public string EquipmentText => _flightPlan?.EquipmentText is { Length: > 0 } equipment ? equipment : MissingFlightPlanValue;

    /// <summary>The surveillance capabilities of the filing, e.g. "L B1 D1".</summary>
    public string TransponderTypeText => _flightPlan?.TransponderText is { Length: > 0 } types ? types : MissingFlightPlanValue;

    /// <summary>True once IVAO described the filing, which is what the detail rows need.</summary>
    public bool HasFlightPlanDetails => _flightPlan is not null;

    public bool HasSecondAlternate => _flightPlan?.Alternate2Icao is { Length: > 0 };

    public string DepartureName => Plan?.Departure?.DisplayName ?? "—";

    public string ArrivalName => Plan?.Arrival?.DisplayName ?? "—";

    public string Remarks => First(_flightPlan?.Remarks, _pilot.FlightPlan?.Remarks) ?? string.Empty;

    public string Route => Plan?.Route ?? string.Empty;

    public string RouteText => string.IsNullOrWhiteSpace(Route) ? "No route filed" : Route;

    public string PositionText => _pilot.LastTrack?.Position is { } position ? position.ToString() : "—";

    public string LastSeenText => _pilot.LastTrack?.Timestamp is { } timestamp
        ? timestamp.ToLocalTime().ToString("HH:mm:ss")
        : "—";

    /// <summary>
    /// When the flight should arrive, from the remaining distance and the live
    /// ground speed. The estimate moves with every refresh, so it is recomputed
    /// instead of stored.
    /// </summary>
    public string EtaText => _pilot.EstimatedArrival(DateTimeOffset.Now) is { } eta
        ? eta.ToLocalTime().ToString("HH:mm")
        : "—";

    /// <summary>The IVAO member id (VID) of the flight, known before the roster record arrives.</summary>
    public string PilotId
    {
        get
        {
            var id = _detail?.UserId ?? 0;
            if (id <= 0)
            {
                id = _pilot.UserId;
            }

            return id > 0 ? id.ToString("0") : MissingProfileValue;
        }
    }

    /// <summary>
    /// The member behind the callsign, once IVAO described the session. The
    /// identity block of the card is only drawn when this is true, because a
    /// blank header with a flag slot would look like a failure rather than an
    /// answer that has not arrived.
    /// </summary>
    public bool HasProfile => _detail?.User is not null || _pilot.User is not null || IsUnauthorized(_detailStatus);

    private string MissingProfileValue => IsUnauthorized(_detailStatus) ? "-" : "—";

    private string MissingFlightPlanValue => IsUnauthorized(_flightPlanStatus) ? "-" : "—";

    /// <summary>
    /// The name of the card header. The record of the session may leave the name
    /// out, so the one the traffic list already has is printed instead of the
    /// placeholder the roster record would produce.
    /// </summary>
    public string ProfileName => MemberName is { Length: > 0 } name ? name : MissingProfileValue;

    /// <summary>The division the member flies for, spelled out from its ISO code.</summary>
    public string HomeText => (_detail?.User ?? _pilot.User)?.DivisionName is { Length: > 0 } name ? name : MissingProfileValue;

    /// <summary>Pilot rating in short form ("FS3"), the level IVAO grants the member.</summary>
    public string RatingText
    {
        get
        {
            var rating = (_detail?.User ?? _pilot.User)?.Rating;
            if (!string.IsNullOrWhiteSpace(rating?.PilotRating?.ShortName))
            {
                return rating.PilotRating.ShortName;
            }

            if (!string.IsNullOrWhiteSpace(rating?.PilotRating?.Name))
            {
                return rating.PilotRating.Name;
            }

            return !string.IsNullOrWhiteSpace(rating?.AtcRating?.ShortName) ? rating.AtcRating.ShortName : MissingProfileValue;
        }
    }

    /// <summary>
    /// "Altitude 0.4.1", the client the member is connected with. The summary
    /// the tracker delivers already names it, so the cell is filled before the
    /// roster record arrives; the record wins because it carries the version too.
    /// </summary>
    public string SoftwareText => _detail?.SoftwareText is { Length: > 0 } software
        ? software
        : _pilot.SoftwareText is { Length: > 0 } summary ? summary : "—";

    public string SimulatorText => _detail?.SimulatorText is { Length: > 0 } simulator
        ? simulator
        : _pilot.SimulatorText is { Length: > 0 } summary ? summary : "—";

    public string ServerText => _detail?.ServerText is { Length: > 0 } server
        ? server
        : _pilot.ServerText is { Length: > 0 } summary ? summary : "—";

    /// <summary>
    /// Time online, counted from the connection timestamp rather than from the
    /// count that came with the record, so a cached answer keeps counting up.
    /// The summary knows the connection as well, so the cell is filled before the
    /// card has asked for the session.
    /// </summary>
    public string OnlineText =>
        (_detail?.Online(DateTimeOffset.Now) ?? _pilot.Online(DateTimeOffset.Now)) is { } online
            ? DurationText.Format(online)
            : "—";

    public string ConnectedText => (_detail?.CreatedAt ?? _pilot.CreatedAt) is { } connectedAt
        ? connectedAt.ToLocalTime().ToString("HH:mm")
        : "—";

    /// <summary>The pilot rating badge, drawn once the image is decoded.</summary>
    public object? RatingBadge
    {
        get => _ratingBadge;
        private set => SetProperty(ref _ratingBadge, value);
    }

    /// <summary>The flag of the member's division, drawn once the image is decoded.</summary>
    public object? FlagImage
    {
        get => _flagImage;
        private set => SetProperty(ref _flagImage, value);
    }

    /// <summary>
    /// Why the card has no identity block. The roster endpoint is authenticated,
    /// so the common cause is a missing sign-in or key and the text says which
    /// one to add; nothing is reported while the first answer is still on its way.
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
                401 or 403 => string.Empty,
                404 => "IVAO has no record of this session (404) — the flight may have just disconnected.",
                { } status => $"IVAO answered {status} for this session, so the pilot details are unavailable.",
                _ when !string.IsNullOrWhiteSpace(_detailError) =>
                    $"The pilot details could not be loaded — {_detailError}",
                _ => "The pilot details could not be loaded."
            };
        }
    }

    public bool HasProfileNotice => ProfileNotice.Length > 0;

    /// <summary>
    /// Why the filed details of the flight plan are missing. A session that has
    /// no plan to describe is not an error and says nothing; a refusal is named,
    /// because a missing sign-in is the one cause the user can fix. Nothing is
    /// reported while the answer is still on its way.
    /// </summary>
    public string FlightPlanNotice
    {
        get
        {
            if (_flightPlan is not null || !_flightPlanRequested)
            {
                return string.Empty;
            }

            return _flightPlanStatus switch
            {
                401 or 403 => string.Empty,
                _ when !string.IsNullOrWhiteSpace(_flightPlanError) =>
                    $"The filed flight plan could not be loaded — {_flightPlanError}",
                _ => string.Empty
            };
        }
    }

    public bool HasFlightPlanNotice => FlightPlanNotice.Length > 0;

    public string SearchText { get; private set; } = string.Empty;

    public object? Icon => _icon;

    public double X
    {
        get => _x;
        private set => SetProperty(ref _x, value);
    }

    public double Y
    {
        get => _y;
        private set => SetProperty(ref _y, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(DisplayGlyphSize));
        }
    }

    public string Tooltip =>
        $"{Callsign}  {RouteLabel}\n{Aircraft}  ·  {Altitude}  ·  {Speed}  ·  {Heading}\n{State}";

    public void Update(IvaoPilot pilot)
    {
        var userChanged = _pilot.User?.Id != pilot.User?.Id;
        _pilot = pilot;
        Refresh();
        if (userChanged)
        {
            LoadImages(_pilot.User);
        }
    }

    /// <summary>
    /// Layers the roster record of the session on top of the summary: the name
    /// behind the callsign, the division, the ratings and the connected software.
    /// The record arrives after the card is already on screen, so everything it
    /// fills in is a plain property change; a failure leaves the flight details
    /// the summary already gave and explains the gap instead.
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
            _detail?.User?.ApplyNameFrom(_pilot.User);

            RatingBadge = null;
            FlagImage = null;
            LoadImages(_detail?.User ?? _pilot.User);
        }

        Refresh();
    }

    public void SetScreenPosition(double x, double y, bool visible)
    {
        X = x;
        Y = y;
        IsVisible = visible;
    }

    /// <summary>
    /// Layers the filing IVAO keeps for the session on top of the summary: the
    /// cruise figures, the times, the endurance, the alternate and the equipment
    /// the tracker feed leaves out. Like the roster record it arrives after the
    /// card is already on screen and is a plain property change; a refusal or a
    /// session without a plan leaves the summary's own details in place.
    /// </summary>
    public void ApplyFlightPlan(SessionFlightPlanResult result)
    {
        _flightPlanStatus = result.StatusCode;
        _flightPlanError = result.Error;
        _flightPlanRequested = true;

        if (result.FlightPlan is { } plan)
        {
            _flightPlan = plan;
        }

        Refresh();
    }

    private void Refresh()
    {
        var glyph = Glyph;
        if (_icon is null || _iconGlyph != glyph)
        {
            _icon = _icons.Get(glyph);
            _iconGlyph = glyph;
        }

        SearchText = string.Join(' ', new[]
        {
            Callsign,
            Aircraft,
            RouteLabel,
            State,
            Transponder,
            Remarks,
            _pilot.UserId.ToString(),
            MemberName,
            (_detail?.User ?? _pilot.User)?.DivisionName,
            (_detail?.User ?? _pilot.User)?.StaffText,
            _detail?.SoftwareText,
            _pilot.SoftwareText
        }).ToLowerInvariant();

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Paints the badge and the flag: whatever is already decoded is set at once,
    /// and whatever is not is fetched in the background and dropped in when it
    /// arrives, so an answer from IVAO never waits for a picture.
    /// </summary>
    private void LoadImages(IvaoSessionUser? user)
    {
        var badgeUrl = user?.PilotBadgeUrl;
        var flagUrl = user?.FlagUrl;

        RatingBadge = _images.TryGet(badgeUrl);
        FlagImage = _images.TryGet(flagUrl);

        _ = SessionImageBinder.ApplyAsync(_images, badgeUrl, image => RatingBadge = image);
        _ = SessionImageBinder.ApplyAsync(_images, flagUrl, image => FlagImage = image);
    }

    private static string? First(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? (string.IsNullOrWhiteSpace(fallback) ? null : fallback) : preferred;

    private static bool IsUnauthorized(int? status) => status is 401 or 403;
}
