using TheEye.Core.Services;

namespace TheEye.Presentation.ViewModels;

/// <summary>
/// One row of the FRIENDS list. Friends are stored as IVAO ids, so a row is
/// assembled from whatever the tracker knows about that member: the flight, the
/// station or the supervisor record, and an offline row when nothing in the
/// snapshot belongs to the VID. The row is updated in place on every refresh,
/// so the selection and the button that drops the friend survive the poll.
/// </summary>
public sealed class FriendItem : ObservableObject
{
    private PilotItem? _pilot;
    private AtcItem? _atc;

    private string _callsign = "—";
    private string _name = string.Empty;
    private string _activity = string.Empty;
    private string _detail = string.Empty;
    private string _online = "—";
    private bool _isOnline;

    public FriendItem(long userId)
    {
        UserId = userId;
        Refresh();
    }

    /// <summary>The VID the friend list stores, and the key of the row.</summary>
    public long UserId { get; }

    public string VidText => UserId > 0 ? UserId.ToString("0") : "—";

    /// <summary>The pilot row behind the friend, when the friend is flying.</summary>
    public PilotItem? Pilot => _pilot;

    /// <summary>The station row behind the friend, when the friend is controlling.</summary>
    public AtcItem? Atc => _atc;

    /// <summary>The callsign the friend is connected with, "—" when offline.</summary>
    public string Callsign
    {
        get => _callsign;
        private set => SetProperty(ref _callsign, value);
    }

    /// <summary>The name of the member, or the VID when the feed has no name.</summary>
    public string Name
    {
        get => _name;
        private set => SetProperty(ref _name, value);
    }

    /// <summary>What the friend is doing: the flight state, the facility or "Offline".</summary>
    public string ActivityText
    {
        get => _activity;
        private set => SetProperty(ref _activity, value);
    }

    /// <summary>The line under the activity: the route, the frequency or the reason.</summary>
    public string DetailText
    {
        get => _detail;
        private set => SetProperty(ref _detail, value);
    }

    /// <summary>Time connected, or "—" for a friend that is not on the network.</summary>
    public string OnlineText
    {
        get => _online;
        private set => SetProperty(ref _online, value);
    }

    /// <summary>False for a friend that is on the list but not connected.</summary>
    public bool IsOnline
    {
        get => _isOnline;
        private set => SetProperty(ref _isOnline, value);
    }

    /// <summary>The row in one line for the tooltip, which has room for all of it.</summary>
    public string TooltipText => string.Join(
        " · ",
        new[] { Callsign, Name, ActivityText, DetailText, IsOnline ? $"online {OnlineText}" : "not connected" }
            .Where(part => part.Length > 0));

    public string SearchText { get; private set; } = string.Empty;

    /// <summary>Fills the row from the flight of the friend.</summary>
    public void ShowPilot(PilotItem pilot)
    {
        _pilot = pilot;
        _atc = null;
        IsOnline = true;
        Callsign = pilot.Callsign;
        Name = pilot.HasMemberName ? pilot.MemberName : "IVAO member";
        ActivityText = pilot.State is { Length: > 0 } state ? state : "Flying";
        DetailText = $"{pilot.Departure} → {pilot.Arrival} · {pilot.Altitude}";
        OnlineText = pilot.OnlineText;
        Refresh();
    }

    /// <summary>Fills the row from the station of the friend.</summary>
    public void ShowAtc(AtcItem station)
    {
        _pilot = null;
        _atc = station;
        IsOnline = true;
        Callsign = station.Callsign;
        Name = station.HasMemberName ? station.MemberName : "IVAO member";
        ActivityText = station.FacilityName;
        DetailText = station.StationName is { Length: > 0 } stationName
            ? $"{stationName} · {station.Frequency}"
            : station.Frequency;
        OnlineText = station.OnlineText;
        Refresh();
    }

    /// <summary>
    /// Fills the row from the supervisor record, which is the only thing the
    /// tracker knows about a member that is connected as an observer.
    /// </summary>
    public void ShowSupervisor(SupervisorItem supervisor)
    {
        _pilot = null;
        _atc = null;
        IsOnline = true;
        Callsign = supervisor.Callsign;
        Name = supervisor.Name;
        ActivityText = supervisor.ConnectionText;
        DetailText = supervisor.Supervisor.HasStaffPositions
            ? $"Supervisor · {supervisor.Supervisor.StaffText}"
            : "Supervisor";
        OnlineText = supervisor.OnlineText;
        Refresh();
    }

    /// <summary>
    /// The row of a friend that is not connected. It stays on the list so the
    /// friend can be dropped while offline, which is when a stale entry is
    /// noticed.
    /// </summary>
    public void ShowOffline()
    {
        _pilot = null;
        _atc = null;
        IsOnline = false;
        Callsign = "—";
        Name = $"VID {UserId}";
        ActivityText = "Offline";
        DetailText = "Not connected";
        OnlineText = "—";
        Refresh();
    }

    private void Refresh()
    {
        SearchText = string.Join(' ', new[] { Callsign, Name, VidText, ActivityText, DetailText })
            .ToLowerInvariant();
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(TooltipText));
    }
}
