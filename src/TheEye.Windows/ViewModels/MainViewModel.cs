using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using TheEye.Core.Api;
using TheEye.Core.Models;
using TheEye.Core.Services;
using TheEye.Core.Settings;
using TheEye.Windows.Services;

namespace TheEye.Windows.ViewModels;

/// <summary>
/// Shell view model: owns settings, the polling tracker, the traffic lists and
/// their filters, and keeps the map and the selection in sync.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    private readonly TheEyeSettings _settings;
    private readonly ICollectionView _pilotsView;
    private readonly ICollectionView _atcView;
    private readonly AsyncRelayCommand _refreshCommand;

    private IvaoApiClient? _api;
    private IvaoSession? _session;
    private TrackerService? _tracker;
    private SessionTrackStore? _tracks;
    private IvaoConnectionCounts _stats = new();
    private bool _syncingSelection;
    private bool _started;
    private bool _disposed;

    private string _searchText = string.Empty;
    private string _statusText = "Starting";
    private string _lastUpdatedText = "Waiting for the first update";
    private string _errorMessage = string.Empty;
    private string _warningText = string.Empty;
    private string _trackNotice = string.Empty;
    private bool _isRefreshing;
    private bool _hasError;
    private bool _hasWarnings;
    private bool _hasTrackNotice;
    private int _pilotCount;
    private int _atcCount;
    private int _totalCount;
    private int _observerCount;
    private int _supervisorCount;
    private int _worldTourCount;
    private int _uniqueUsers24h;

    public MainViewModel(SettingsStore store, TheEyeSettings settings)
    {
        _store = store;
        _settings = settings;
        Map = new MapViewModel();

        _pilotsView = CollectionViewSource.GetDefaultView(Map.Pilots);
        _pilotsView.Filter = FilterPilot;
        _atcView = CollectionViewSource.GetDefaultView(Map.Atc);
        _atcView.Filter = FilterAtc;

        RefreshCommand = _refreshCommand = new AsyncRelayCommand(RefreshAsync, () => _tracker is not null);
        LocateSelectedCommand = new RelayCommand(LocateSelected, () => SelectedPilot is not null || SelectedAtc is not null);
        ClearSelectionCommand = new RelayCommand(() => Map.ClearSelection(), () => HasSelection);
        ZoomInCommand = new RelayCommand(() => Map.ZoomIn());
        ZoomOutCommand = new RelayCommand(() => Map.ZoomOut());
        HomeCommand = new RelayCommand(() => Map.ResetView(new GeoCoordinate(45, 10), 25000));

        Map.SelectionChanged += OnMapSelectionChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    public MapViewModel Map { get; }

    public ICollectionView PilotsView => _pilotsView;

    public ICollectionView AtcView => _atcView;

    public AsyncRelayCommand RefreshCommand { get; }

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
            if (Map.SelectedPilot == value)
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
            if (Map.SelectedAtc == value)
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

    public bool HasSelection => SelectedPilot is not null || SelectedAtc is not null;

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

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                _pilotsView.Refresh();
                _atcView.Refresh();
            }
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

    /// <summary>
    /// Explains why the selected flight or station is not traced from its
    /// recorded track, e.g. because no API key is configured. The map keeps
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

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    /// <summary>True while an IVAO sign-in is in place, which is what the trail feed needs.</summary>
    public bool IsSignedIn => _session?.IsSignedIn ?? _settings.HasOAuthTokens;

    public string ConnectionDescription => IsSignedIn
        ? string.IsNullOrWhiteSpace(_settings.OAuthAccount)
            ? "Signed in to IVAO"
            : $"Signed in to IVAO as {_settings.OAuthAccount}"
        : HasApiKey
            ? "Authenticated with a personal IVAO API key"
            : "Public tracker summary (no sign-in, no flight trails)";

    public string RefreshDescription => $"Every {_settings.RefreshSeconds} seconds";

    public string ThemeDescription => TheEyeThemes.Normalize(_settings.Theme);

    public string SettingsPath => _store.FilePath;

    public void Initialize()
    {
        Map.Initialize(
            ThemeService.CreateMapPalette(),
            new GeoCoordinate(_settings.MapLatitude, _settings.MapLongitude),
            _settings.MapResolution);

        Map.ShowPilots = _settings.ShowPilots;
        Map.ShowAtc = _settings.ShowAtc;
        Map.CenterOnMapClick = _settings.CenterOnMapClick;
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
            !string.Equals(_settings.ApiKey, updated.ApiKey, StringComparison.Ordinal) ||
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
            ThemeService.Apply(_settings.Theme);
        }

        Map.ShowPilots = _settings.ShowPilots;
        Map.ShowAtc = _settings.ShowAtc;
        Map.CenterOnMapClick = _settings.CenterOnMapClick;
        OnPropertyChanged(nameof(ShowPilots));
        OnPropertyChanged(nameof(ShowAtc));
        OnPropertyChanged(nameof(HasApiKey));
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
        ThemeService.ThemeChanged -= OnThemeChanged;
        Map.SelectionChanged -= OnMapSelectionChanged;
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
            ApiKey = string.IsNullOrWhiteSpace(_settings.ApiKey) ? null : _settings.ApiKey,
            AccessTokenSource = _session
        });

        _tracker = new TrackerService(_api, TimeSpan.FromSeconds(_settings.RefreshSeconds));
        _tracker.SnapshotUpdated += OnSnapshotUpdated;
        _tracker.RefreshFailed += OnRefreshFailed;
        _tracker.Start();

        // The recorded track of the selected flight or station is fetched on
        // demand, because only one of them is ever on screen.
        _tracks = new SessionTrackStore(_api);
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

    private static void RaiseOnDispatcher(Action action)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(action);
            return;
        }

        action();
    }

    private void OnSnapshotUpdated(object? sender, TrackerSnapshot snapshot)
    {
        if (Application.Current.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => OnSnapshotUpdated(sender, snapshot)));
            return;
        }

        if (!ReferenceEquals(sender, _tracker))
        {
            return;
        }

        Map.UpdateTraffic(snapshot);

        // The trail ends on the live position, so it follows every refresh.
        _ = RefreshSelectionTracksAsync();

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
        WarningText = string.Join(Environment.NewLine, snapshot.Warnings);
        HasError = false;
        ErrorMessage = string.Empty;
        IsRefreshing = false;

        _pilotsView.Refresh();
        _atcView.Refresh();
    }

    private void OnRefreshFailed(object? sender, TrackerRefreshException exception)
    {
        if (Application.Current.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => OnRefreshFailed(sender, exception)));
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
        WarningText = string.Join(Environment.NewLine, exception.Warnings);
    }

    private void OnMapSelectionChanged(object? sender, EventArgs e)
    {
        _syncingSelection = true;
        try
        {
            OnPropertyChanged(nameof(SelectedPilot));
            OnPropertyChanged(nameof(SelectedAtc));
            RaiseSelectionDependentChanges();
        }
        finally
        {
            _syncingSelection = false;
        }

        // The reason the previous selection had no trail says nothing about this
        // one, and the answer for it is not in yet.
        TrackNotice = string.Empty;
        _ = RefreshSelectionTracksAsync();
    }

    /// <summary>
    /// Loads the recorded track of the selected flight or station so the map can
    /// trace the path it has actually flown. Selecting a session marked its route
    /// as pending, so no line is drawn until this answer arrives: the estimate of
    /// the filed route is only used once the track is known to be unavailable, and
    /// the trail therefore never jumps. A response for a selection the user has
    /// already left is dropped.
    /// </summary>
    private async Task RefreshSelectionTracksAsync()
    {
        if (_tracks is not { } tracks)
        {
            // Nothing can fetch a track, so the map must stop waiting for one and
            // fall back to the flown part of the route.
            Map.SetSelectionTracks(null);
            return;
        }

        var sessionId = Map.SelectedPilot?.Id ?? Map.SelectedAtc?.Id;
        if (sessionId is not { } id)
        {
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
            if (Map.SelectedPilot?.Id == id || Map.SelectedAtc?.Id == id)
            {
                Map.SetSelectionTracks(null);
            }

            return;
        }

        if (Map.SelectedPilot?.Id != id && Map.SelectedAtc?.Id != id)
        {
            return;
        }

        Map.SetSelectionTracks(result.Tracks);
        TrackNotice = result.HasTrack ? string.Empty : DescribeMissingTrack(result);
    }

    /// <summary>
    /// Why the map is drawing the filed route instead of a recorded trail. The
    /// tracks feed is authenticated: IVAO answers 401 unless the request carries
    /// an IVAO sign-in token (what WebEye sends) or a personal consumer key. An
    /// unauthorised answer is named as such, because it is the one cause the
    /// user can fix; a session without a track is reported apart.
    /// </summary>
    private string DescribeMissingTrack(SessionTrackResult result) => result.StatusCode switch
    {
        401 or 403 when IsSignedIn =>
            "IVAO refused the recorded track of this session (401) even though you are signed in — the IVAO application was not approved for the tracker scope, so it cannot read recorded sessions.",
        401 or 403 when HasApiKey =>
            "IVAO refused the recorded track of this session (401) even with an API key configured — the tracks feed needs a sign-in or a valid consumer key.",
        401 or 403 =>
            "IVAO refused the recorded track request (401 not authenticated). Sign in with IVAO in Settings, or paste a personal IVAO API key: the map traces the part of the route already flown.",
        { } status =>
            $"IVAO answered {status} for the recorded track of this session, so the map traces the part of the route already flown.",
        null => "No recorded track for this session — the map shows the part of the route already flown."
    };

    private void RaiseSelectionDependentChanges()
    {
        OnPropertyChanged(nameof(HasSelection));
        LocateSelectedCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
    }

    private bool FilterPilot(object item) =>
        string.IsNullOrWhiteSpace(_searchText) ||
        item is PilotItem pilot && pilot.SearchText.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool FilterAtc(object item) =>
        string.IsNullOrWhiteSpace(_searchText) ||
        item is AtcItem atc && atc.SearchText.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase);

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

    private void OnThemeChanged(object? sender, EventArgs e) => Map.ApplyTheme(ThemeService.CreateMapPalette());
}
