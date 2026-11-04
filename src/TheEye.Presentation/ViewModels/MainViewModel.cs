using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using TheEye.Core.Api;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Reference;
using TheEye.Core.Services;
using TheEye.Core.Settings;
using TheEye.Presentation.Collections;
using TheEye.Presentation.Services;
using TheEye.Presentation.Theming;
using TheEye.Presentation.Threading;

namespace TheEye.Presentation.ViewModels;

/// <summary>
/// Shell view model: owns settings, the polling tracker, the traffic lists and
/// their filters, and keeps the map and the selection in sync.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    private readonly TheEyeSettings _settings;
    private readonly IThemeService _theme;
    private readonly FilteredCollection<PilotItem> _pilotsView;
    private readonly FilteredCollection<AtcItem> _atcView;
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AviationWeatherClient _weather = new();

    // The SUP and FRIENDS lists are not traffic: the supervisor feed is its own
    // answer, and a friend is a VID that may be flying, controlling, supervising
    // or not connected at all. Both rows are kept and updated in place, so a
    // selection survives the poll.
    private readonly ObservableCollection<SupervisorItem> _supervisors = [];
    private readonly FilteredCollection<SupervisorItem> _supervisorsView;
    private readonly ObservableCollection<FriendItem> _friends = [];
    private readonly FilteredCollection<FriendItem> _friendsView;
    private readonly Dictionary<long, SupervisorItem> _supervisorsById = [];
    private readonly Dictionary<long, FriendItem> _friendsById = [];
    private readonly RelayCommand _toggleFriendCommand;

    private IvaoApiClient? _api;
    private IvaoSession? _session;
    private TrackerService? _tracker;
    private SessionTrackStore? _tracks;
    private SessionDetailStore? _details;
    private SessionFlightPlanStore? _flightPlans;
    private IvaoConnectionCounts _stats = new();
    private FriendItem? _selectedFriend;
    private SupervisorItem? _selectedSupervisor;
    private bool _syncingSelection;
    private CancellationTokenSource? _airportMetarRequest;
    private string _airportMetarText = string.Empty;
    private string _airportMetarStatus = string.Empty;
    private IReadOnlyList<RunwayEndOption> _selectedAirportRunwayEnds = [];
    private RunwayEndOption? _selectedRunwayEnd;

    // Avalonia lists drop and re-pick their selection on a Reset; that write-back must not re-select (and re-centre).
    private bool _rebuildingLists;
    private bool _started;
    private bool _disposed;

    private string _searchText = string.Empty;
    private double _trafficColumnWidth;
    private string _statusText = "Starting";
    private string _lastUpdatedText = "Waiting for the first update";
    private string _errorMessage = string.Empty;
    private string _warningText = string.Empty;
    private string _trackNotice = string.Empty;
    private bool _isRefreshing;
    private bool _hasError;
    private bool _hasWarnings;
    private bool _hasVisibleWarnings;
    private bool _hasTrackNotice;
    private int _pilotCount;
    private int _atcCount;
    private int _totalCount;
    private int _observerCount;
    private int _supervisorCount;
    private int _supervisorListCount;
    private int _friendCount;
    private int _worldTourCount;
    private int _uniqueUsers24h;

    public MainViewModel(SettingsStore store, TheEyeSettings settings, IThemeService theme, IImagePipeline images)
    {
        _store = store;
        _settings = settings;
        _theme = theme;
        _trafficColumnWidth = settings.TrafficColumnWidth;
        Map = new MapViewModel(images, navDataResolver: NavigraphRouteResolver.GetDefault);

        // The lists a search box narrows: the map keeps every aircraft and
        // station, and these two collections are the filtered projection the
        // traffic panels show.
        _pilotsView = new FilteredCollection<PilotItem>(Map.Pilots, FilterPilot);
        _atcView = new FilteredCollection<AtcItem>(Map.Atc, FilterAtc);
        Map.Pilots.CollectionChanged += OnPilotCollectionChanged;
        Map.Atc.CollectionChanged += OnAtcCollectionChanged;

        // The SUP and FRIENDS tabs are narrowed by the same search box: the
        // supervisor list in particular is long enough to need it.
        _supervisorsView = new FilteredCollection<SupervisorItem>(_supervisors, FilterSupervisor);
        _friendsView = new FilteredCollection<FriendItem>(_friends, FilterFriend);

        RefreshCommand = _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => _tracker is not null);
        LocateSelectedCommand = new RelayCommand(LocateSelected, () => SelectedPilot is not null || SelectedAtc is not null);
        ClearSelectionCommand = new RelayCommand(() => Map.ClearSelection(), () => HasSelection);
        ToggleFriendCommand = _toggleFriendCommand = new RelayCommand(ToggleFriend, parameter => ResolveUserId(parameter) > 0);
        ZoomInCommand = new RelayCommand(() => Map.ZoomIn());
        ZoomOutCommand = new RelayCommand(() => Map.ZoomOut());
        HomeCommand = new RelayCommand(() => Map.ResetView(new GeoCoordinate(45, 10), WorldMap.MaxZoomOutResolution));

        Map.SelectionChanged += OnMapSelectionChanged;
        _theme.ThemeChanged += OnThemeChanged;
    }

    public MapViewModel Map { get; }

    public FilteredCollection<PilotItem> PilotsView => _pilotsView;

    public FilteredCollection<AtcItem> AtcView => _atcView;

    /// <summary>The members connected with supervisor rights, narrowed by the search box.</summary>
    public FilteredCollection<SupervisorItem> SupervisorsView => _supervisorsView;

    /// <summary>The VIDs on the friend list, connected or not, narrowed by the search box.</summary>
    public FilteredCollection<FriendItem> FriendsView => _friendsView;

    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// Adds or drops a friend. The parameter selects the member — a row of the
    /// SUP or FRIENDS list — and a command without one acts on the selection, so
    /// the button of the selection panel needs no binding of its own.
    /// </summary>
    public RelayCommand ToggleFriendCommand { get; }

    public RelayCommand LocateSelectedCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand ZoomInCommand { get; }

    public RelayCommand ZoomOutCommand { get; }

    public RelayCommand HomeCommand { get; }

    public PilotItem? SelectedPilot
    {
        get => Map.SelectedPilot;
        set
        {
            if (Map.SelectedPilot == value || _rebuildingLists)
            {
                return;
            }

            if (!_syncingSelection)
            {
                Map.SelectPilot(value);
            }

            OnPropertyChanged();
            RaiseSelectionDependentChanges();
        }
    }

    public AtcItem? SelectedAtc
    {
        get => Map.SelectedAtc;
        set
        {
            if (Map.SelectedAtc == value || _rebuildingLists)
            {
                return;
            }

            if (!_syncingSelection)
            {
                Map.SelectAtc(value);
            }

            OnPropertyChanged();
            RaiseSelectionDependentChanges();
        }
    }

    public AirportRecord? SelectedAirport => Map.SelectedAirport;

    public IReadOnlyList<RunwayEndOption> SelectedAirportRunwayEnds => _selectedAirportRunwayEnds;

    public RunwayEndOption? SelectedRunwayEnd
    {
        get => _selectedRunwayEnd;
        set
        {
            if (SetProperty(ref _selectedRunwayEnd, value))
            {
                RaiseRunwayWindChanges();
            }
        }
    }

    public string RunwayWindDirectionText
    {
        get
        {
            if (!TryGetRunwayWind(out var wind))
            {
                return "Wind unavailable";
            }

            return wind.HasVariableDirection
                ? $"VRB / {wind.SpeedKnots:0} kt"
                : $"{wind.DirectionDegrees:000}°T / {wind.SpeedKnots:0} kt";
        }
    }

    public RunwayDiagramState? RunwayDiagram => SelectedRunwayEnd is null
        ? null
        : RunwayDiagramState.Create(SelectedRunwayEnd, MetarWindCalculator.Parse(AirportMetarText));

    /// <summary>The wind pushes from behind on the selected runway end.</summary>
    public bool IsRunwayTailwind => TryGetRunwayComponents(out var components) && components.TailwindKnots > 0;

    public string RunwayCrosswindText => TryGetRunwayWind(out var wind)
        ? wind.HasVariableDirection
            ? "Variable"
            : TryGetRunwayComponents(out var components) ? $"{components.CrosswindKnots:0.0} kt" : "--"
        : "--";

    public string RunwayCrosswindSideText => TryGetRunwayWind(out var wind) && wind.HasVariableDirection
        ? "Direction unknown"
        : TryGetRunwayComponents(out var components) && components.CrosswindKnots > 0
            ? components.CrosswindFromRight ? "From right" : "From left"
            : string.Empty;

    public string RunwayAlongWindLabel => TryGetRunwayWind(out var wind) && wind.HasVariableDirection
        ? "HEADWIND / TAILWIND"
        : TryGetRunwayComponents(out var components) && components.TailwindKnots > 0
            ? "TAILWIND"
            : "HEADWIND";

    public string RunwayAlongWindText => TryGetRunwayWind(out var wind)
        ? wind.HasVariableDirection
            ? "Variable"
            : TryGetRunwayComponents(out var components)
                ? $"{(components.TailwindKnots > 0 ? components.TailwindKnots : components.HeadwindKnots):0.0} kt"
                : "--"
        : "--";

    public string AirportMetarText
    {
        get => _airportMetarText;
        private set
        {
            if (SetProperty(ref _airportMetarText, value))
            {
                RaiseRunwayWindChanges();
            }
        }
    }

    public string AirportMetarStatus
    {
        get => _airportMetarStatus;
        private set => SetProperty(ref _airportMetarStatus, value);
    }

    public string AirportLocationText
    {
        get
        {
            if (FindSelectedAirportPayload() is not { } payload)
            {
                return string.Empty;
            }

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(payload.Iata) &&
                !string.Equals(payload.Iata, payload.Icao, StringComparison.OrdinalIgnoreCase))
            {
                details.Add(payload.Iata);
            }

            if (!string.IsNullOrWhiteSpace(payload.City))
            {
                details.Add(payload.City);
            }

            if (!string.IsNullOrWhiteSpace(payload.CountryId))
            {
                details.Add(DivisionCatalog.Name(payload.CountryId) is { Length: > 0 } country
                    ? country
                    : payload.CountryId);
            }

            return string.Join(" · ", details);
        }
    }

    public bool HasAirportLocation => AirportLocationText.Length > 0;

    public IEnumerable<AtcItem> SelectedAirportAtc => SelectedAirport is { } airport
        ? Map.Atc.Where(atc => atc.HasAirport && string.Equals(atc.AirportIcao, airport.Icao, StringComparison.OrdinalIgnoreCase))
        : [];

    public bool HasSelectedAirportAtc => SelectedAirportAtc.Any();

    public bool HasMemberSelection => SelectedPilot is not null || SelectedAtc is not null;

    public bool HasSelection => HasMemberSelection || SelectedAirport is not null;

    /// <summary>
    /// The row of the FRIENDS tab that was picked. Selecting a friend frames the
    /// flight or the station behind it, exactly as picking the same aircraft or
    /// controller in the traffic lists would, so the FRIENDS tab is a shortcut and
    /// not a dead end. A friend that is not connected has nothing to frame.
    /// </summary>
    public FriendItem? SelectedFriend
    {
        get => _selectedFriend;
        set
        {
            if (!SetProperty(ref _selectedFriend, value) || _rebuildingLists)
            {
                return;
            }

            if (value?.Pilot is { } pilot)
            {
                SelectedPilot = pilot;
            }
            else if (value?.Atc is { } station)
            {
                SelectedAtc = station;
            }
        }
    }

    /// <summary>
    /// The row of the SUP tab that was picked. A supervisor is connected as a
    /// pilot, a controller or an observer, so a pick frames the aircraft or the
    /// station the member occupies, exactly as selecting them in the traffic
    /// lists would; an observer has no position to frame and clears the panel.
    /// </summary>
    public SupervisorItem? SelectedSupervisor
    {
        get => _selectedSupervisor;
        set
        {
            if (!SetProperty(ref _selectedSupervisor, value) || _rebuildingLists || value is null || value.UserId <= 0)
            {
                return;
            }

            if (Map.Pilots.FirstOrDefault(pilot => pilot.UserId == value.UserId) is { } flight)
            {
                SelectedPilot = flight;
            }
            else if (Map.Atc.FirstOrDefault(station => station.UserId == value.UserId) is { } position)
            {
                SelectedAtc = position;
            }
            else
            {
                // An observer is on the network without a position on the map, so
                // the selection panel must not keep showing the previous flight.
                SelectedPilot = null;
                SelectedAtc = null;
            }
        }
    }

    /// <summary>True when the selection is a member IVAO lists with supervisor rights.</summary>
    public bool IsSelectionSupervisor =>
        (SelectedPilot?.IsSupervisor ?? false) || (SelectedAtc?.IsSupervisor ?? false);

    /// <summary>True when the selected member is on the friend list.</summary>
    public bool IsSelectionFriend
    {
        get
        {
            var userId = SelectedPilot?.UserId ?? SelectedAtc?.UserId ?? 0;
            return _settings.IsFriend(userId);
        }
    }

    /// <summary>The label of the star button of the selection panel.</summary>
    public string FriendActionText => IsSelectionFriend ? "★ REMOVE FRIEND" : "★ ADD FRIEND";

    /// <summary>
    /// Selection that came from a click on an aircraft marker. The centring
    /// follows the user's "centre the map when I click an aircraft or station"
    /// option, while <see cref="SelectedPilot"/> / <see cref="SelectedAtc"/> — the
    /// two the traffic lists bind to — always frame their selection, so an
    /// aircraft or a controller can be found from the list whatever the option
    /// says. Station badges and sectors are picked by the map itself, which
    /// honours the same option in <see cref="MapViewModel.SelectAt"/>.
    /// </summary>
    public void SelectPilotFromMap(PilotItem pilot) => Map.SelectPilot(pilot, Map.CenterOnMapClick);

    public bool ShowPilots
    {
        get => Map.ShowPilots;
        set
        {
            if (Map.ShowPilots == value)
            {
                return;
            }

            Map.ShowPilots = value;
            _settings.ShowPilots = value;
            OnPropertyChanged();
        }
    }

    public bool ShowAtc
    {
        get => Map.ShowAtc;
        set
        {
            if (Map.ShowAtc == value)
            {
                return;
            }

            Map.ShowAtc = value;
            _settings.ShowAtc = value;
            OnPropertyChanged();
        }
    }

    public bool ShowAirports
    {
        get => Map.ShowAirports;
        set
        {
            if (Map.ShowAirports == value)
            {
                return;
            }

            Map.ShowAirports = value;
            _settings.ShowAirports = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RebuildLists(() =>
                {
                    _pilotsView.Refresh();
                    _atcView.Refresh();
                    _supervisorsView.Refresh();
                    _friendsView.Refresh();
                });
            }
        }
    }

    /// <summary>
    /// Width of the traffic browser on the left of the main window. The shell
    /// writes it when the splitter is released, and it is persisted with the
    /// rest of the settings, so the column comes back at the width the user
    /// left it.
    /// </summary>
    public double TrafficColumnWidth
    {
        get => _trafficColumnWidth;
        set
        {
            var width = double.IsNaN(value)
                ? TheEyeSettings.DefaultTrafficColumnWidth
                : Math.Clamp(value, TheEyeSettings.MinTrafficColumnWidth, TheEyeSettings.MaxTrafficColumnWidth);
            if (!SetProperty(ref _trafficColumnWidth, width))
            {
                return;
            }

            _settings.TrafficColumnWidth = width;
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string WarningText
    {
        get => _warningText;
        private set => SetProperty(ref _warningText, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                _refreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool HasWarnings
    {
        get => _hasWarnings;
        private set => SetProperty(ref _hasWarnings, value);
    }

    public bool HasVisibleWarnings
    {
        get => _hasVisibleWarnings;
        private set => SetProperty(ref _hasVisibleWarnings, value);
    }

    /// <summary>
    /// Explains why the selected flight or station is not traced from its
    /// recorded track, e.g. because the user is not signed in. The map keeps
    /// drawing the flown part of the filed route in the meantime.
    /// </summary>
    public string TrackNotice
    {
        get => _trackNotice;
        private set
        {
            if (SetProperty(ref _trackNotice, value))
            {
                HasTrackNotice = !string.IsNullOrWhiteSpace(value);
            }
        }
    }

    public bool HasTrackNotice
    {
        get => _hasTrackNotice;
        private set => SetProperty(ref _hasTrackNotice, value);
    }

    public int PilotCount
    {
        get => _pilotCount;
        private set => SetProperty(ref _pilotCount, value);
    }

    public int AtcCount
    {
        get => _atcCount;
        private set => SetProperty(ref _atcCount, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => SetProperty(ref _totalCount, value);
    }

    public int ObserverCount
    {
        get => _observerCount;
        private set => SetProperty(ref _observerCount, value);
    }

    public int SupervisorCount
    {
        get => _supervisorCount;
        private set => SetProperty(ref _supervisorCount, value);
    }

    /// <summary>
    /// Rows of the SUP tab, which is the supervisor feed: it stays at zero while
    /// the client cannot authenticate, because the feed answers 401.
    /// </summary>
    public int SupervisorListCount
    {
        get => _supervisorListCount;
        private set => SetProperty(ref _supervisorListCount, value);
    }

    /// <summary>Rows of the FRIENDS tab, connected or not.</summary>
    public int FriendCount
    {
        get => _friendCount;
        private set => SetProperty(ref _friendCount, value);
    }

    public int WorldTourCount
    {
        get => _worldTourCount;
        private set => SetProperty(ref _worldTourCount, value);
    }

    public int UniqueUsers24h
    {
        get => _uniqueUsers24h;
        private set => SetProperty(ref _uniqueUsers24h, value);
    }

    /// <summary>True while an IVAO sign-in is in place, which is what the trail feed needs.</summary>
    public bool IsSignedIn => _session?.IsSignedIn ?? _settings.HasOAuthTokens;

    public string ConnectionDescription => IsSignedIn
        ? string.IsNullOrWhiteSpace(_settings.OAuthAccount)
            ? "Signed in to IVAO"
            : $"Signed in to IVAO as {_settings.OAuthAccount}"
        : "Public tracker summary (no sign-in, no flight trails)";

    public string RefreshDescription => $"Every {_settings.RefreshSeconds} seconds";

    public string ThemeDescription => TheEyeThemes.Normalize(_settings.Theme);

    public string SettingsPath => _store.FilePath;

    public void Initialize()
    {
        Map.Initialize(
            _theme.CreateMapPalette(),
            new GeoCoordinate(_settings.MapLatitude, _settings.MapLongitude),
            _settings.MapResolution);

        Map.ShowPilots = _settings.ShowPilots;
        Map.ShowAtc = _settings.ShowAtc;
        Map.ShowAirports = _settings.ShowAirports;
        Map.CenterOnMapClick = _settings.CenterOnMapClick;
        Map.AircraftGlyphSize = _settings.AircraftGlyphSize;
        Map.MapStyle = _settings.MapStyle;
    }

    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        ConfigureTracker();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_tracker is null || IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusText = "Updating";

        try
        {
            await _tracker.RefreshAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusText = "Offline";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public TheEyeSettings CreateSettingsSnapshot() => _settings.Clone();

    public async Task ApplySettingsAsync(TheEyeSettings updated)
    {
        updated.Normalize();
        var authChanged =
            !string.Equals(_settings.OAuthClientId, updated.OAuthClientId, StringComparison.Ordinal) ||
            !string.Equals(_settings.OAuthClientSecret, updated.OAuthClientSecret, StringComparison.Ordinal) ||
            !string.Equals(_settings.OAuthRedirectUri, updated.OAuthRedirectUri, StringComparison.Ordinal) ||
            !string.Equals(_settings.OAuthScopes, updated.OAuthScopes, StringComparison.Ordinal) ||
            !string.Equals(_settings.OAuthAccessToken, updated.OAuthAccessToken, StringComparison.Ordinal) ||
            !string.Equals(_settings.OAuthRefreshToken, updated.OAuthRefreshToken, StringComparison.Ordinal) ||
            !string.Equals(_settings.OAuthAccount, updated.OAuthAccount, StringComparison.Ordinal);
        var intervalChanged = _settings.RefreshSeconds != updated.RefreshSeconds;
        var themeChanged = !string.Equals(
            TheEyeThemes.Normalize(_settings.Theme),
            TheEyeThemes.Normalize(updated.Theme),
            StringComparison.Ordinal);

        _settings.CopyFrom(updated);

        if (themeChanged)
        {
            _theme.Apply(_settings.Theme);
        }

        Map.ShowPilots = _settings.ShowPilots;
        Map.ShowAtc = _settings.ShowAtc;
        Map.ShowAirports = _settings.ShowAirports;
        Map.CenterOnMapClick = _settings.CenterOnMapClick;
        Map.AircraftGlyphSize = _settings.AircraftGlyphSize;
        Map.MapStyle = _settings.MapStyle;
        TrafficColumnWidth = _settings.TrafficColumnWidth;
        RefreshFriendMarks();
        OnPropertyChanged(nameof(ShowPilots));
        OnPropertyChanged(nameof(ShowAtc));
        OnPropertyChanged(nameof(ShowAirports));
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(ConnectionDescription));
        OnPropertyChanged(nameof(RefreshDescription));
        OnPropertyChanged(nameof(ThemeDescription));

        SaveSettings();

        if (authChanged || intervalChanged)
        {
            ConfigureTracker();
            await RefreshAsync();
        }
    }

    /// <summary>Persists settings plus the current map view. Called when the window closes.</summary>
    public void SaveSettings()
    {
        var (center, resolution) = Map.GetView();
        _settings.MapLatitude = center.Latitude;
        _settings.MapLongitude = center.Longitude;
        _settings.MapResolution = resolution;
        _settings.ShowPilots = Map.ShowPilots;
        _settings.ShowAtc = Map.ShowAtc;
        _settings.ShowAirports = Map.ShowAirports;
        _settings.TrafficColumnWidth = TrafficColumnWidth;

        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            HasError = true;
            ErrorMessage = $"Could not save settings: {ex.Message}";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _theme.ThemeChanged -= OnThemeChanged;
        Map.SelectionChanged -= OnMapSelectionChanged;
        Map.Pilots.CollectionChanged -= OnPilotCollectionChanged;
        Map.Atc.CollectionChanged -= OnAtcCollectionChanged;
        _airportMetarRequest?.Cancel();
        _airportMetarRequest?.Dispose();
        _weather.Dispose();
        _tracker?.Dispose();
        _session?.Dispose();
        _api?.Dispose();
        Map.Dispose();
    }

    private void ConfigureTracker()
    {
        _tracker?.Dispose();
        _api?.Dispose();
        _session?.Dispose();

        // The sign-in lives across restarts: tokens come from the settings file
        // and every renewal writes them back, so the member signs in once.
        _session = new IvaoSession(
            new IvaoOAuthClient(CreateOAuthOptions()),
            CreateStoredTokens(),
            PersistTokensAsync);

        _api = new IvaoApiClient(new IvaoApiOptions
        {
            AccessTokenSource = _session
        });

        _tracker = new TrackerService(_api, TimeSpan.FromSeconds(_settings.RefreshSeconds));

        // The names, divisions and supervisor feed are authenticated, so a
        // signed-out run keeps the public summaries without polling them.
        _tracker.IncludeIdentity = _api.CanAuthenticate && IsSignedIn;
        _tracker.SnapshotUpdated += OnSnapshotUpdated;
        _tracker.RefreshFailed += OnRefreshFailed;
        _tracker.Start(refreshImmediately: false);

        // The recorded track of the selected flight is fetched on demand, because
        // only one aircraft is ever on screen. A station is never traced.
        _tracks = new SessionTrackStore(_api);

        // So is the roster record behind the selected callsign; the summary the
        // tracker already delivered carries the flight, not the member.
        _details = new SessionDetailStore(_api);

        // The summary carries the airports and the route only, so the rest of the
        // filing is fetched for the aircraft on screen and cached for a minute.
        _flightPlans = new SessionFlightPlanStore(_api);
        _refreshCommand.RaiseCanExecuteChanged();

        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(ConnectionDescription));
    }

    /// <summary>The IVAO application credentials the sign-in uses, as configured.</summary>
    private IvaoOAuthOptions CreateOAuthOptions() => new()
    {
        ClientId = _settings.OAuthClientId,
        ClientSecret = _settings.OAuthClientSecret,
        RedirectUri = _settings.OAuthRedirectUri,
        Scopes = _settings.OAuthScopes
    };

    private IvaoOAuthTokens? CreateStoredTokens() =>
        _settings.HasOAuthTokens
            ? new IvaoOAuthTokens(
                _settings.OAuthAccessToken,
                string.IsNullOrWhiteSpace(_settings.OAuthRefreshToken) ? null : _settings.OAuthRefreshToken,
                _settings.GetOAuthExpiry())
            : null;

    /// <summary>
    /// Writes a renewed (or dropped) token back to the settings file. The token
    /// layer calls this from whichever thread needed the token, so the store is
    /// written off the UI thread and the notification is marshalled back.
    /// </summary>
    private Task PersistTokensAsync(IvaoOAuthTokens? tokens)
    {
        if (tokens is null)
        {
            _settings.ClearOAuthTokens();
        }
        else
        {
            _settings.OAuthAccessToken = tokens.AccessToken;
            _settings.OAuthRefreshToken = tokens.RefreshToken ?? string.Empty;
            _settings.SetOAuthExpiry(tokens.ExpiresAt);
        }

        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The token stays in memory for this run; the next save retries.
        }

        RaiseOnDispatcher(() =>
        {
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(ConnectionDescription));
        });

        return Task.CompletedTask;
    }

    private static void RaiseOnDispatcher(Action action) => UiThread.Post(action);

    private void OnSnapshotUpdated(object? sender, TrackerSnapshot snapshot)
    {
        if (!UiThread.CheckAccess())
        {
            UiThread.Post(() => OnSnapshotUpdated(sender, snapshot));
            return;
        }

        if (!ReferenceEquals(sender, _tracker))
        {
            return;
        }

        RebuildLists(() =>
        {
            Map.UpdateTraffic(snapshot);

            // The two lists that follow members rather than sessions. They are filled
            // before the counts, because a FRIENDS row is assembled from the traffic
            // the map has just taken in.
            SyncSupervisors(snapshot.Supervisors);
            SyncFriends();
        });
        SupervisorListCount = _supervisors.Count;
        FriendCount = _friends.Count;

        // The trail ends on the live position, so it follows every refresh.
        _ = RefreshSelectionTracksAsync();

        // The card's time online and arrival estimate move with the refresh too.
        _ = RefreshSelectionDetailAsync();

        // A pilot can amend the filing mid-flight; the store keeps the answer for
        // a minute, so this costs one request per minute at most.
        _ = RefreshSelectionFlightPlanAsync();

        _stats = snapshot.Stats.Now ?? new IvaoConnectionCounts();
        PilotCount = snapshot.PilotCount;
        AtcCount = snapshot.AtcCount;
        TotalCount = _stats.Total > 0 ? _stats.Total : snapshot.PilotCount + snapshot.AtcCount;
        ObserverCount = _stats.Observers;
        SupervisorCount = _stats.Supervisors;
        WorldTourCount = _stats.WorldTours;
        UniqueUsers24h = _stats.UniqueUsers24h;
        LastUpdatedText = $"Updated {snapshot.UpdatedAt.ToLocalTime():HH:mm:ss}";
        StatusText = snapshot.IsPartial ? "Live — partial data" : "Live";
        HasWarnings = snapshot.IsPartial;
        HasVisibleWarnings = snapshot.UserWarnings.Count > 0;
        WarningText = string.Join(Environment.NewLine, snapshot.UserWarnings);
        HasError = false;
        ErrorMessage = string.Empty;
        IsRefreshing = false;

        RebuildLists(() =>
        {
            _pilotsView.Refresh();
            _atcView.Refresh();
        });
    }

    private void RebuildLists(Action rebuild)
    {
        _rebuildingLists = true;
        try
        {
            rebuild();
        }
        finally
        {
            _rebuildingLists = false;
        }
    }

    private void OnRefreshFailed(object? sender, TrackerRefreshException exception)
    {
        if (!UiThread.CheckAccess())
        {
            UiThread.Post(() => OnRefreshFailed(sender, exception));
            return;
        }

        if (!ReferenceEquals(sender, _tracker))
        {
            return;
        }

        IsRefreshing = false;
        HasError = true;
        ErrorMessage = exception.Message;
        StatusText = "Offline";
        HasWarnings = exception.Warnings.Count > 0;
        HasVisibleWarnings = exception.UserWarnings.Count > 0;
        WarningText = string.Join(Environment.NewLine, exception.UserWarnings);
    }

    private void OnMapSelectionChanged(object? sender, EventArgs e)
    {
        _syncingSelection = true;
        try
        {
            OnPropertyChanged(nameof(SelectedPilot));
            OnPropertyChanged(nameof(SelectedAtc));
            OnPropertyChanged(nameof(SelectedAirport));
            UpdateSelectedAirportRunways();
            OnPropertyChanged(nameof(SelectedAirportAtc));
            OnPropertyChanged(nameof(HasSelectedAirportAtc));
            OnPropertyChanged(nameof(AirportLocationText));
            OnPropertyChanged(nameof(HasAirportLocation));
            RaiseSelectionDependentChanges();
        }
        finally
        {
            _syncingSelection = false;
        }

        // The reason the previous selection had no trail says nothing about this
        // one, and the answer for it is not in yet.
        TrackNotice = string.Empty;
        _ = RefreshAirportMetarAsync();
        _ = RefreshSelectionTracksAsync();
        _ = RefreshSelectionDetailAsync();
        _ = RefreshSelectionFlightPlanAsync();
    }

    private void UpdateSelectedAirportRunways()
    {
        var previous = _selectedRunwayEnd;
        var airport = SelectedAirport;
        _selectedAirportRunwayEnds = airport is null ? [] : CreateRunwayEnds(airport);
        OnPropertyChanged(nameof(SelectedAirportRunwayEnds));

        SelectedRunwayEnd = _selectedAirportRunwayEnds.FirstOrDefault(option =>
            previous is not null && option.Runway == previous.Runway && option.Designation == previous.Designation)
            ?? _selectedAirportRunwayEnds.FirstOrDefault(option => option.Runway == airport?.LongestRunway)
            ?? _selectedAirportRunwayEnds.FirstOrDefault();
    }

    private static IReadOnlyList<RunwayEndOption> CreateRunwayEnds(AirportRecord airport) =>
        airport.Runways
            .OrderByDescending(runway => runway.LengthMetres)
            .SelectMany(runway =>
            {
                var designations = runway.RunwayDesignations.Split('/');
                return new[]
                {
                    new RunwayEndOption(designations[0], runway.TrueHeadingDegrees, runway),
                    new RunwayEndOption(designations[1], (runway.TrueHeadingDegrees + 180) % 360, runway)
                };
            })
            .ToArray();

    private bool TryGetRunwayWind(out MetarWind wind)
    {
        var parsed = MetarWindCalculator.Parse(AirportMetarText);
        wind = parsed.GetValueOrDefault();
        return SelectedRunwayEnd is not null && parsed.HasValue;
    }

    private bool TryGetRunwayComponents(out RunwayWindComponents components)
    {
        if (SelectedRunwayEnd is not null && TryGetRunwayWind(out var wind))
        {
            if (MetarWindCalculator.Calculate(wind, SelectedRunwayEnd.TrueHeadingDegrees) is { } calculated)
            {
                components = calculated;
                return true;
            }
        }

        components = default;
        return false;
    }

    private void RaiseRunwayWindChanges()
    {
        OnPropertyChanged(nameof(RunwayDiagram));
        OnPropertyChanged(nameof(IsRunwayTailwind));
        OnPropertyChanged(nameof(RunwayWindDirectionText));
        OnPropertyChanged(nameof(RunwayCrosswindText));
        OnPropertyChanged(nameof(RunwayCrosswindSideText));
        OnPropertyChanged(nameof(RunwayAlongWindLabel));
        OnPropertyChanged(nameof(RunwayAlongWindText));
    }

    private async Task RefreshAirportMetarAsync()
    {
        _airportMetarRequest?.Cancel();
        _airportMetarRequest?.Dispose();
        _airportMetarRequest = null;

        if (SelectedAirport is not { } airport)
        {
            AirportMetarText = string.Empty;
            AirportMetarStatus = string.Empty;
            return;
        }

        var request = _airportMetarRequest = new CancellationTokenSource();
        AirportMetarText = string.Empty;
        AirportMetarStatus = "Loading METAR";

        try
        {
            var report = await _weather.GetMetarAsync(airport.Icao, request.Token);
            if (!ReferenceEquals(_airportMetarRequest, request))
            {
                return;
            }

            AirportMetarText = report ?? string.Empty;
            AirportMetarStatus = report is null ? "No current report available" : "AVIATION WEATHER CENTER";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_airportMetarRequest, request))
            {
                AirportMetarStatus = "METAR unavailable";
            }
        }
        catch (HttpRequestException)
        {
            if (ReferenceEquals(_airportMetarRequest, request))
            {
                AirportMetarStatus = "METAR unavailable";
            }
        }
        catch (JsonException)
        {
            if (ReferenceEquals(_airportMetarRequest, request))
            {
                AirportMetarStatus = "METAR unavailable";
            }
        }
    }

    private void OnAtcCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SelectedAirportAtc));
        OnPropertyChanged(nameof(HasSelectedAirportAtc));
        OnPropertyChanged(nameof(AirportLocationText));
        OnPropertyChanged(nameof(HasAirportLocation));
    }

    private void OnPilotCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(AirportLocationText));
        OnPropertyChanged(nameof(HasAirportLocation));
    }

    private IvaoAirport? FindSelectedAirportPayload()
    {
        if (SelectedAirport is not { } airport)
        {
            return null;
        }

        static bool Matches(IvaoAirport? candidate, string icao) =>
            candidate is not null && string.Equals(candidate.Icao, icao, StringComparison.OrdinalIgnoreCase);

        return Map.Atc.Select(atc => atc.Atc.AtcPosition?.Airport)
                   .FirstOrDefault(candidate => Matches(candidate, airport.Icao))
            ?? Map.Pilots.SelectMany(pilot => new[]
                {
                    pilot.Pilot.FlightPlan?.Departure,
                    pilot.Pilot.FlightPlan?.Arrival,
                    pilot.Pilot.FlightPlan?.Alternative,
                    pilot.Pilot.FlightPlan?.Alternative2
                })
                .FirstOrDefault(candidate => Matches(candidate, airport.Icao));
    }

    /// <summary>
    /// Loads the recorded track of the selected flight so the map can trace the
    /// path it has actually flown. Only aircraft are traced: selecting a station
    /// never requests a track, because a controller session records no flown path
    /// and drawing one would put an aircraft-like line across its sector. Selecting
    /// a flight marked its route as pending, so no line is drawn until this answer
    /// arrives: the estimate of the filed route is only used once the track is
    /// known to be unavailable, and the trail therefore never jumps. A response for
    /// a selection the user has already left is dropped.
    /// </summary>
    private async Task RefreshSelectionTracksAsync()
    {
        if (_tracks is not { } tracks || !IsSignedIn)
        {
            // Nothing can fetch a track, so the map must stop waiting for one and
            // fall back to the flown part of the route.
            Map.SetSelectionTracks(null);
            return;
        }

        var sessionId = Map.SelectedPilot?.Id;
        if (sessionId is not { } id)
        {
            // A station has no trail, and a previous flight's notice does not apply.
            TrackNotice = string.Empty;
            return;
        }

        SessionTrackResult result;
        try
        {
            result = await tracks.GetTracksAsync(id);
        }
        catch
        {
            // IVAO never answered — a timeout, for example — so the map must stop
            // waiting for a track instead of staying without any line at all.
            if (Map.SelectedPilot?.Id == id)
            {
                Map.SetSelectionTracks(null);
            }

            return;
        }

        if (Map.SelectedPilot?.Id != id)
        {
            return;
        }

        Map.SetSelectionTracks(result.Tracks);
        TrackNotice = result.HasTrack ? string.Empty : DescribeMissingTrack(result);
    }

    /// <summary>
    /// Loads a roster record only when the authenticated live feed omitted the
    /// member. Normally that feed already carries the card's identity details, so
    /// selecting a live pilot or station does not need another request. A response
    /// for a selection the user has already left is dropped.
    /// </summary>
    private async Task RefreshSelectionDetailAsync()
    {
        if (_details is not { } details || !IsSignedIn)
        {
            return;
        }

        // A station is described by the same record as a flight: who is behind
        // the callsign, their ratings and the client they are connected with.
        var pilot = Map.SelectedPilot;
        var atc = Map.SelectedAtc;
        if (pilot?.Pilot.User is not null || atc?.Atc.User is not null)
        {
            return;
        }

        if ((pilot?.Id ?? atc?.Id) is not { } sessionId)
        {
            return;
        }

        SessionDetailResult result;
        try
        {
            result = await details.GetSessionAsync(sessionId);
        }
        catch
        {
            // IVAO never answered, so the card keeps the details the summary
            // already gave it rather than blanking the identity block.
            return;
        }

        if (pilot is not null)
        {
            if (Map.SelectedPilot is { } currentPilot && currentPilot.Id == sessionId)
            {
                currentPilot.ApplySessionDetail(result);
            }

            return;
        }

        if (atc is not null && Map.SelectedAtc is { } currentAtc && currentAtc.Id == sessionId)
        {
            currentAtc.ApplySessionDetail(result);
        }
    }

    /// <summary>
    /// Loads the filing IVAO keeps for the selected flight, so the card can show
    /// what the tracker summary leaves out: the cruise level and speed, the filed
    /// and actual times, the endurance, both alternates and the equipment. A
    /// station has nothing to file, so only a pilot is asked about. Like the other
    /// session feeds this endpoint is authenticated, and a response for a
    /// selection the user has already left is dropped.
    /// </summary>
    private async Task RefreshSelectionFlightPlanAsync()
    {
        if (!IsSignedIn || _flightPlans is not { } flightPlans || Map.SelectedPilot is not { } pilot ||
            pilot.Pilot.FlightPlan?.HasExtendedDetails == true)
        {
            return;
        }

        var sessionId = pilot.Id;
        SessionFlightPlanResult result;
        try
        {
            result = await flightPlans.GetFlightPlanAsync(sessionId);
        }
        catch
        {
            // IVAO never answered, so the card keeps the details the summary
            // already gave it rather than blanking the flight plan block.
            return;
        }

        if (Map.SelectedPilot is { } currentPilot && currentPilot.Id == sessionId)
        {
            Map.SetSelectionFlightPlan(result);
        }
    }

    /// <summary>
    /// Why the map is drawing the filed route instead of a recorded trail. The
    /// tracks feed is authenticated: IVAO answers 401 unless the request carries
    /// an IVAO sign-in token (what WebEye sends). An unauthorised answer is
    /// quiet; a session without a track is reported apart.
    /// </summary>
    private string DescribeMissingTrack(SessionTrackResult result) => result.StatusCode switch
    {
        401 or 403 => string.Empty,
        { } status =>
            $"IVAO answered {status} for the recorded track of this session, so the map traces the part of the route already flown.",
        null => "No recorded track for this session — the map shows the part of the route already flown."
    };

    /// <summary>
    /// Reconciles the SUP list with the supervisor feed. The rows are updated
    /// instead of rebuilt, so a list that is scrolled to stays where it is while
    /// the poll replaces the records.
    /// </summary>
    private void SyncSupervisors(IReadOnlyList<IvaoSupervisor> supervisors)
    {
        var incoming = new HashSet<long>();
        foreach (var record in supervisors)
        {
            if (!incoming.Add(record.Id))
            {
                continue;
            }

            if (_supervisorsById.TryGetValue(record.Id, out var existing))
            {
                existing.Update(record);
            }
            else
            {
                var item = new SupervisorItem(record);
                _supervisorsById.Add(record.Id, item);
                _supervisors.Add(item);
            }
        }

        foreach (var stale in _supervisors.Where(item => !incoming.Contains(item.Id)).ToList())
        {
            _supervisors.Remove(stale);
            _supervisorsById.Remove(stale.Id);
        }

        // A supervisor that signed off takes the selection with it, like a flight
        // that landed takes the traffic selection.
        if (_selectedSupervisor is { } selected && !_supervisors.Contains(selected))
        {
            SelectedSupervisor = null;
        }
    }

    /// <summary>
    /// Rebuilds the FRIENDS list from the VIDs of the settings: a friend that is
    /// flying or controlling gets the row of the traffic list, a supervisor gets
    /// the row of the supervisor feed, and a VID that is not connected keeps an
    /// offline row, because a stale friend is dropped while it is offline.
    /// </summary>
    private void SyncFriends()
    {
        // Every row that follows a member carries the star, and a member that has
        // just connected is not marked yet.
        RefreshFriendMarks();

        var byUser = new Dictionary<long, FriendItem>();

        foreach (var pilot in Map.Pilots)
        {
            if (pilot.UserId > 0 && _settings.IsFriend(pilot.UserId))
            {
                var item = GetFriend(pilot.UserId);
                item.ShowPilot(pilot);
                byUser[pilot.UserId] = item;
            }
        }

        foreach (var station in Map.Atc)
        {
            if (station.UserId > 0 && _settings.IsFriend(station.UserId))
            {
                var item = GetFriend(station.UserId);
                item.ShowAtc(station);
                byUser[station.UserId] = item;
            }
        }

        foreach (var supervisor in _supervisors)
        {
            // A supervisor that is flying or controlling is already listed above
            // with the traffic row, which carries more of the state than the feed.
            if (supervisor.UserId > 0 &&
                _settings.IsFriend(supervisor.UserId) &&
                !byUser.ContainsKey(supervisor.UserId))
            {
                var item = GetFriend(supervisor.UserId);
                item.ShowSupervisor(supervisor);
                byUser[supervisor.UserId] = item;
            }
        }

        // The rows follow the order of the friend list (a VID ascending), so the
        // list does not reshuffle when a friend connects or disconnects.
        var ordered = new List<FriendItem>(_settings.FriendIds.Count);
        foreach (var userId in _settings.FriendIds)
        {
            if (!byUser.TryGetValue(userId, out var online))
            {
                online = GetFriend(userId);
                online.ShowOffline();
            }

            ordered.Add(online);
        }

        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            var current = _friends.IndexOf(item);
            if (current < 0)
            {
                _friends.Insert(index, item);
            }
            else if (current != index)
            {
                _friends.Move(current, index);
            }
        }

        while (_friends.Count > ordered.Count)
        {
            _friends.RemoveAt(_friends.Count - 1);
        }

        foreach (var userId in _friendsById.Keys.Where(id => !_settings.IsFriend(id)).ToList())
        {
            _friendsById.Remove(userId);
        }

        if (_selectedFriend is { } selected && !_friends.Contains(selected))
        {
            SelectedFriend = null;
        }
    }

    /// <summary>The row of a friend, created the first time the VID is seen.</summary>
    private FriendItem GetFriend(long userId)
    {
        if (!_friendsById.TryGetValue(userId, out var item))
        {
            item = new FriendItem(userId);
            _friendsById.Add(userId, item);
        }

        return item;
    }

    private void RefreshFriendMarks()
    {
        foreach (var pilot in Map.Pilots)
        {
            pilot.IsFriend = _settings.IsFriend(pilot.UserId);
            pilot.MarkerColor = _settings.AircraftHighlightFor(pilot.UserId) ?? string.Empty;
        }

        foreach (var station in Map.Atc)
        {
            station.IsFriend = _settings.IsFriend(station.UserId);
        }

        foreach (var supervisor in _supervisors)
        {
            supervisor.IsFriend = _settings.IsFriend(supervisor.UserId);
        }
    }

    private void ToggleFriend(object? parameter)
    {
        var userId = ResolveUserId(parameter);
        if (userId <= 0)
        {
            return;
        }

        _settings.ToggleFriend(userId);
        SaveSettings();

        // The star sits on every row that follows the member, and the FRIENDS list
        // gains or loses its row.
        RebuildLists(SyncFriends);
        FriendCount = _friends.Count;
        OnPropertyChanged(nameof(IsSelectionFriend));
        OnPropertyChanged(nameof(FriendActionText));
    }

    /// <summary>
    /// The member a command acts on: the row it was fired from, or the selection
    /// when the button that fires it carries no parameter.
    /// </summary>
    private long ResolveUserId(object? parameter) => parameter switch
    {
        long userId => userId,
        int userId => userId,
        PilotItem pilot => pilot.UserId,
        AtcItem station => station.UserId,
        FriendItem friend => friend.UserId,
        _ => SelectedPilot?.UserId ?? SelectedAtc?.UserId ?? 0
    };

    private void RaiseSelectionDependentChanges()
    {
        OnPropertyChanged(nameof(HasMemberSelection));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsSelectionSupervisor));
        OnPropertyChanged(nameof(IsSelectionFriend));
        OnPropertyChanged(nameof(FriendActionText));
        LocateSelectedCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
        _toggleFriendCommand.RaiseCanExecuteChanged();
    }

    private bool FilterPilot(object item) =>
        string.IsNullOrWhiteSpace(_searchText) ||
        item is PilotItem pilot && pilot.SearchText.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool FilterAtc(object item) =>
        string.IsNullOrWhiteSpace(_searchText) ||
        item is AtcItem atc && atc.SearchText.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool FilterSupervisor(object item) =>
        string.IsNullOrWhiteSpace(_searchText) ||
        item is SupervisorItem supervisor &&
        supervisor.SearchText.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool FilterFriend(object item) =>
        string.IsNullOrWhiteSpace(_searchText) ||
        item is FriendItem friend &&
        friend.SearchText.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);

    private void LocateSelected()
    {
        if (SelectedPilot is { } pilot)
        {
            Map.CenterOnPilot(pilot);
        }
        else if (SelectedAtc is { } atc)
        {
            Map.CenterOnAtc(atc);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Map.ApplyTheme(_theme.CreateMapPalette());
}
