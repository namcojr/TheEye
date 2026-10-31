using TheEye.Core.Models;
using TheEye.Core.Services;

namespace TheEye.Presentation.ViewModels;

/// <summary>
/// One row of the SUP list. A member with supervisor rights is in neither the
/// pilot nor the ATC list of the tracker — they are connected as a pilot, a
/// controller or an observer — so their row is built from the supervisor record
/// alone: the callsign they are connected with, the member behind it and the
/// staff positions they hold. The record comes from an authenticated feed, so
/// the list stays empty until the client can sign in.
/// </summary>
public sealed class SupervisorItem : ObservableObject
{
    private IvaoSupervisor _supervisor;
    private bool _isFriend;

    public SupervisorItem(IvaoSupervisor supervisor, bool isFriend = false)
    {
        _supervisor = supervisor;
        _isFriend = isFriend;
        SearchText = BuildSearchText();
    }

    public IvaoSupervisor Supervisor => _supervisor;

    /// <summary>Id of the connection, which is what the feed lists.</summary>
    public long Id => _supervisor.Id;

    /// <summary>The IVAO member id (VID), the key of the friend list.</summary>
    public long UserId => _supervisor.UserId;

    public string Callsign => _supervisor.Callsign;

    /// <summary>The name of the member, falling back to the VID.</summary>
    public string Name => _supervisor.Name;

    public string VidText => UserId > 0 ? UserId.ToString("0") : "—";

    /// <summary>The division the member belongs to, spelled out from its code.</summary>
    public string DivisionText => _supervisor.DivisionName is { Length: > 0 } division ? division : "—";

    /// <summary>The IVAO staff positions the member holds, comma separated.</summary>
    public string StaffText => _supervisor.HasStaffPositions ? _supervisor.StaffText : "—";

    /// <summary>What the supervisor is connected with: flying, controlling or observing.</summary>
    public string ConnectionText => _supervisor.ConnectionLabel;

    /// <summary>"SUP", "ATC", "PILOT" or "OBS", the short form the rows print.</summary>
    public string ConnectionCode => _supervisor.ConnectionCode;

    public string ServerText => _supervisor.ServerId is { Length: > 0 } server ? server : "—";

    /// <summary>Time connected, counting up between refreshes like every other row.</summary>
    public string OnlineText => _supervisor.Online(DateTimeOffset.Now) is { } online
        ? DurationText.Format(online)
        : "—";

    /// <summary>
    /// Everything the panel has no room for, in the tooltip of the row: the
    /// callsign, the member, what they are connected with, their division, their
    /// staff positions, the server and the time connected.
    /// </summary>
    public string TooltipText => string.Join(
        " · ",
        new[] { Callsign, Name, ConnectionText, DivisionText, StaffText, ServerText, OnlineText }
            .Where(part => part.Length > 0));

    /// <summary>
    /// True when the member is on the friend list of the settings. The shell
    /// sets it after every refresh, because the list can change without the
    /// supervisor list changing.
    /// </summary>
    public bool IsFriend
    {
        get => _isFriend;
        set
        {
            if (SetProperty(ref _isFriend, value))
            {
                OnPropertyChanged(nameof(FriendMark));
                OnPropertyChanged(nameof(FriendActionText));
            }
        }
    }

    /// <summary>The star of the friend button of the row: filled while followed.</summary>
    public string FriendMark => IsFriend ? "★" : "☆";

    public string FriendActionText => IsFriend ? "Remove from friends" : "Add to friends";

    public string SearchText { get; private set; }

    /// <summary>
    /// Takes a newer record of the same connection. The time connected is
    /// counted from the timestamp the feed repeats, so the row is refreshed
    /// instead of recreated: the selection and the friend flag survive the poll.
    /// </summary>
    public void Update(IvaoSupervisor supervisor)
    {
        _supervisor = supervisor;
        SearchText = BuildSearchText();
        OnPropertyChanged(string.Empty);
    }

    private string BuildSearchText() => string.Join(' ', new[]
    {
        Callsign,
        Name,
        VidText,
        ConnectionText,
        ConnectionCode,
        DivisionText,
        StaffText,
        ServerText
    }).ToLowerInvariant();
}
