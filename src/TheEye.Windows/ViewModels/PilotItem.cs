using System.Windows.Media.Imaging;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Windows.Services;

namespace TheEye.Windows.ViewModels;

public sealed class PilotItem : ObservableObject
{
    private readonly AircraftIconCache _icons;
    private IvaoPilot _pilot;
    private BitmapImage? _icon;
    private int? _iconHeading;
    private double _x;
    private double _y;
    private bool _isVisible;
    private bool _isSelected;

    public PilotItem(IvaoPilot pilot, AircraftIconCache icons)
    {
        _pilot = pilot;
        _icons = icons;
        Refresh();
    }

    public IvaoPilot Pilot => _pilot;

    public long Id => _pilot.Id;

    public string Callsign => _pilot.Callsign;

    public string Aircraft => _pilot.Aircraft;

    public string RouteLabel => _pilot.Route;

    public string Departure => _pilot.FlightPlan?.DepartureIcao ?? "????";

    public string Arrival => _pilot.FlightPlan?.ArrivalIcao ?? "????";

    public string Altitude => _pilot.AltitudeText;

    public string Speed => _pilot.SpeedText;

    public string Heading => _pilot.HeadingText;

    public string State => _pilot.State;

    public string Transponder => _pilot.TransponderText;

    public string Remaining => _pilot.RemainingText;

    public bool IsOnGround => _pilot.IsOnGround;

    public bool IsMilitary => _pilot.IsMilitary;

    public bool IsWorldTour => _pilot.IsWorldTour;

    public bool HasFlightPlan => _pilot.FlightPlan is not null;

    public string FlightRules => _pilot.FlightPlan?.FlightRules ?? "—";

    public string FlightType => _pilot.FlightPlan?.FlightType ?? "—";

    public string CruisingLevel => _pilot.FlightPlan?.CruisingLevel ?? "—";

    public string CruisingSpeed => _pilot.FlightPlan?.CruisingSpeed ?? "—";

    public string EstimatedEnroute => _pilot.FlightPlan?.EstimatedTimeEnroute ?? "—";

    public string Endurance => _pilot.FlightPlan?.Endurance ?? "—";

    public string Alternate => _pilot.FlightPlan?.Alternate1Id ?? "—";

    public string DepartureName => _pilot.FlightPlan?.Departure?.DisplayName ?? "—";

    public string ArrivalName => _pilot.FlightPlan?.Arrival?.DisplayName ?? "—";

    public string Remarks => _pilot.FlightPlan?.Remarks ?? string.Empty;

    public string Route => _pilot.FlightPlan?.Route ?? string.Empty;

    public string RouteText => string.IsNullOrWhiteSpace(Route) ? "No route filed" : Route;

    public string PositionText => _pilot.LastTrack?.Position is { } position ? position.ToString() : "—";

    public string LastSeenText => _pilot.LastTrack?.Timestamp is { } timestamp
        ? timestamp.ToLocalTime().ToString("HH:mm:ss")
        : "—";

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

    public void SetScreenPosition(double x, double y, bool visible)
    {
        X = x;
        Y = y;
        IsVisible = visible;
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
            _pilot.UserId.ToString()
        }).ToLowerInvariant();

        OnPropertyChanged(string.Empty);
    }
}
