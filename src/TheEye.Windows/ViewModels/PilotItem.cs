using System.Windows.Media.Imaging;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Reference;
using TheEye.Core.Services;
using TheEye.Windows.Services;

namespace TheEye.Windows.ViewModels;

public sealed class PilotItem : ObservableObject
{
    private readonly AircraftIconCache _icons;
    private readonly RemoteImageCache _images;
    private IvaoPilot _pilot;
    private BitmapImage? _icon;
    private int? _iconHeading;
    private IvaoSessionDetail? _detail;
    private int? _detailStatus;
    private string? _detailError;
    private bool _detailRequested;
    private IvaoFlightPlan? _flightPlan;
    private bool _flightPlanRequested;
    private int? _flightPlanStatus;
    private string? _flightPlanError;
    private BitmapImage? _ratingBadge;
    private BitmapImage? _flagImage;
    private double _x;
    private double _y;
    private bool _isVisible;
    private bool _isSelected;

    public PilotItem(IvaoPilot pilot, AircraftIconCache icons, RemoteImageCache images)
    {
        _pilot = pilot;
        _icons = icons;
        _images = images;
        Refresh();
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

    public string State => _pilot.State;

    public string Transponder => _pilot.TransponderText;

    public string Remaining => _pilot.RemainingText;

    public bool IsOnGround => _pilot.IsOnGround;

    public bool IsMilitary => _pilot.IsMilitary;

    public bool IsWorldTour => _pilot.IsWorldTour;

    public bool HasFlightPlan => Plan is not null;

    /// <summary>
    /// The plan to read: the full filing once IVAO described it, otherwise the
    /// trimmed copy embedded in the tracker summary. The summary only carries the
    /// airports, the aircraft and the route, so the card fills in the rest as
    /// soon as <see cref="ApplyFlightPlan"/> has the answer.
    /// </summary>
    private IvaoFlightPlan? Plan => _flightPlan ?? _pilot.FlightPlan;

    public string FlightRules => Plan?.FlightRules is { Length: > 0 } rules ? rules : "—";

    public string FlightType => Plan?.FlightType is { Length: > 0 } type ? type : "—";

    public string CruisingLevel => _flightPlan?.Level is { Length: > 0 } level ? level : "—";

    public string CruisingSpeed => _flightPlan?.Speed is { Length: > 0 } speed ? speed : "—";

    public string EstimatedEnroute => DurationText.FromSeconds(_flightPlan?.EetSeconds);

    public string Endurance => DurationText.FromSeconds(_flightPlan?.EnduranceSeconds);

    public string Alternate => Plan?.AlternateIcao ?? "—";

    public string Alternate2 => Plan?.Alternate2Icao ?? "—";

    /// <summary>The off-block time the flight was filed for, in UTC.</summary>
    public string FiledDepartureText => _flightPlan?.DepartureTimeUtc is { } time
        ? time.UtcDateTime.ToString("HH:mm") + "z"
        : "—";

    /// <summary>When the aircraft actually left the blocks, in UTC.</summary>
    public string ActualDepartureText => _flightPlan?.ActualDepartureTimeUtc is { } time
        ? time.UtcDateTime.ToString("HH:mm") + "z"
        : "—";

    public string PeopleOnBoardText => _flightPlan?.PeopleOnBoard is { } people ? $"{people:N0}" : "—";

    public string EquipmentText => _flightPlan?.EquipmentText is { Length: > 0 } equipment ? equipment : "—";

    /// <summary>The surveillance capabilities of the filing, e.g. "L B1 D1".</summary>
    public string TransponderTypeText => _flightPlan?.TransponderText is { Length: > 0 } types ? types : "—";

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

            return id > 0 ? id.ToString("0") : "—";
        }
    }

    /// <summary>
    /// The member behind the callsign, once IVAO described the session. The
    /// identity block of the card is only drawn when this is true, because a
    /// blank header with a flag slot would look like a failure rather than an
    /// answer that has not arrived.
    /// </summary>
    public bool HasProfile => _detail?.User is not null;

    public string ProfileName => _detail?.User?.FullName ?? "—";

    /// <summary>The division the member flies for, spelled out from its ISO code.</summary>
    public string HomeText => _detail?.User?.DivisionName is { Length: > 0 } name ? name : "—";

    /// <summary>Pilot rating in short form ("FS3"), the level IVAO grants the member.</summary>
    public string RatingText
    {
        get
        {
            var rating = _detail?.User?.Rating;
            if (!string.IsNullOrWhiteSpace(rating?.PilotRating?.ShortName))
            {
                return rating.PilotRating.ShortName;
            }

            if (!string.IsNullOrWhiteSpace(rating?.PilotRating?.Name))
            {
                return rating.PilotRating.Name;
            }

            return !string.IsNullOrWhiteSpace(rating?.AtcRating?.ShortName) ? rating.AtcRating.ShortName : "—";
        }
    }

    /// <summary>"Altitude 0.4.1", the client the member is connected with.</summary>
    public string SoftwareText => _detail?.SoftwareText is { Length: > 0 } software ? software : "—";

    public string SimulatorText => _detail?.SimulatorText is { Length: > 0 } simulator ? simulator : "—";

    public string ServerText => _detail?.ServerText is { Length: > 0 } server ? server : "—";

    /// <summary>
    /// Time online, counted from the connection timestamp rather than from the
    /// count that came with the record, so a cached answer keeps counting up.
    /// </summary>
    public string OnlineText => _detail?.Online(DateTimeOffset.Now) is { } online
        ? DurationText.Format(online)
        : "—";

    public string ConnectedText => _detail?.CreatedAt is { } connectedAt
        ? connectedAt.ToLocalTime().ToString("HH:mm")
        : "—";

    /// <summary>The pilot rating badge, drawn once the image is decoded.</summary>
    public BitmapImage? RatingBadge
    {
        get => _ratingBadge;
        private set => SetProperty(ref _ratingBadge, value);
    }

    /// <summary>The flag of the member's division, drawn once the image is decoded.</summary>
    public BitmapImage? FlagImage
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
                401 or 403 =>
                    "IVAO refused the pilot details (401 not authenticated). Sign in with IVAO in Settings, or add a personal API key, to see who is flying.",
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
                401 or 403 =>
                    "IVAO refused the flight plan (401 not authenticated). Sign in with IVAO in Settings, or add a personal API key, to see the filed details.",
                _ when !string.IsNullOrWhiteSpace(_flightPlanError) =>
                    $"The filed flight plan could not be loaded — {_flightPlanError}",
                _ => string.Empty
            };
        }
    }

    public bool HasFlightPlanNotice => FlightPlanNotice.Length > 0;

    public string SearchText { get; private set; } = string.Empty;

    public BitmapImage? Icon => _icon;

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
        set => SetProperty(ref _isSelected, value);
    }

    public string Tooltip =>
        $"{Callsign}  {RouteLabel}\n{Aircraft}  ·  {Altitude}  ·  {Speed}  ·  {Heading}\n{State}";

    public void Update(IvaoPilot pilot)
    {
        _pilot = pilot;
        Refresh();
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
            RatingBadge = null;
            FlagImage = null;

            if (_detail is { } detail)
            {
                LoadImages(detail);
            }
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
        var heading = AircraftIconCatalog.NormalizeHeading(_pilot.LastTrack?.Heading);
        if (_icon is null || _iconHeading != heading)
        {
            _icon = _icons.Get(heading);
            _iconHeading = heading;
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
            _detail?.User?.FullName,
            _detail?.User?.DivisionName,
            _detail?.SoftwareText
        }).ToLowerInvariant();

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Paints the badge and the flag: whatever is already decoded is set at once,
    /// and whatever is not is fetched in the background and dropped in when it
    /// arrives, so an answer from IVAO never waits for a picture.
    /// </summary>
    private void LoadImages(IvaoSessionDetail detail)
    {
        var badgeUrl = detail.User?.PilotBadgeUrl;
        var flagUrl = detail.User?.FlagUrl;

        RatingBadge = _images.TryGet(badgeUrl);
        FlagImage = _images.TryGet(flagUrl);

        _ = SessionImageBinder.ApplyAsync(_images, badgeUrl, image => RatingBadge = image);
        _ = SessionImageBinder.ApplyAsync(_images, flagUrl, image => FlagImage = image);
    }

    private static string? First(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? (string.IsNullOrWhiteSpace(fallback) ? null : fallback) : preferred;
}
