using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Reference;
using TheEye.Core.Services;
using TheEye.Windows.Services;

namespace TheEye.Windows.ViewModels;

public sealed class AtcItem : ObservableObject
{
    private readonly RemoteImageCache _images;
    private IvaoAtc _atc;
    private IvaoSessionDetail? _detail;
    private int? _detailStatus;
    private string? _detailError;
    private bool _detailRequested;
    private BitmapImage? _ratingBadge;
    private BitmapImage? _flagImage;
    private bool _isSelected;

    public AtcItem(IvaoAtc atc, RemoteImageCache images)
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

    public string ProfileName => _detail?.User?.FullName ?? "—";

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

    /// <summary>"Aurora/mac 1.0.38b", the client the controller is connected with.</summary>
    public string SoftwareText => _detail?.SoftwareText is { Length: > 0 } software ? software : "—";

    public string ServerText => _detail?.ServerText is { Length: > 0 } server ? server : "—";

    /// <summary>
    /// Time on position, counted from the connection timestamp rather than from
    /// the count that came with the record, so a cached answer keeps counting up.
    /// </summary>
    public string OnlineText => _detail?.Online(DateTimeOffset.Now) is { } online
        ? DurationText.Format(online)
        : "—";

    public string ConnectedText => _detail?.CreatedAt is { } connectedAt
        ? connectedAt.ToLocalTime().ToString("HH:mm")
        : "—";

    /// <summary>The controller rating badge, drawn once the image is decoded.</summary>
    public BitmapImage? RatingBadge
    {
        get => _ratingBadge;
        private set => SetProperty(ref _ratingBadge, value);
    }

    /// <summary>The flag of the controller's division, drawn once the image is decoded.</summary>
    public BitmapImage? FlagImage
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

    public Brush AccentBrush { get; private set; } = Brushes.Gray;

    public Brush AccentSoftBrush { get; private set; } = Brushes.Transparent;

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
        var color = (Color)ColorConverter.ConvertFromString(FacilityPalette.ForFacility(_atc.Facility));
        AccentBrush = Freeze(new SolidColorBrush(color));
        AccentSoftBrush = Freeze(new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)));

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
            _detail?.User?.FullName,
            _detail?.User?.DivisionName,
            _detail?.SoftwareText
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

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
