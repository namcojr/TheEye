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
    private TrackerService? _tracker;
    private IvaoConnectionCounts _stats = new();
    private bool _syncingSelection;
    private bool _started;
    private bool _disposed;

    private string _searchText = string.Empty;
    private string _statusText = "Starting";
    private string _lastUpdatedText = "Waiting for the first update";
    private string _errorMessage = string.Empty;
    private string _warningText = string.Empty;
    private bool _isRefreshing;
    private bool _hasError;
    private bool _hasWarnings;
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

    public bool ShowAtcRegions
    {
        get => Map.ShowAtcRegions;
        set
        {
            if (Map.ShowAtcRegions == value)
            {
                return;
            }

            Map.ShowAtcRegions = value;
            _settings.ShowAtcRegions = value;
            OnPropertyChanged();
        }
    }

    public bool ShowAtcStations
    {
        get => Map.ShowAtcStations;
        set
        {
            if (Map.ShowAtcStations == value)
            {
                return;
            }

            Map.ShowAtcStations = value;
            _settings.ShowAtcStations = value;
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

    public string ConnectionDescription => HasApiKey
        ? "Authenticated with a personal IVAO API key"
        : "Public tracker summary (no API key configured)";

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
        Map.ShowAtcRegions = _settings.ShowAtcRegions;
        Map.ShowAtcStations = _settings.ShowAtcStations;
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
        var apiChanged = !string.Equals(_settings.ApiKey, updated.ApiKey, StringComparison.Ordinal);
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
        Map.ShowAtcRegions = _settings.ShowAtcRegions;
        Map.ShowAtcStations = _settings.ShowAtcStations;
        OnPropertyChanged(nameof(ShowPilots));
        OnPropertyChanged(nameof(ShowAtcRegions));
        OnPropertyChanged(nameof(ShowAtcStations));
        OnPropertyChanged(nameof(HasApiKey));
        OnPropertyChanged(nameof(ConnectionDescription));
        OnPropertyChanged(nameof(RefreshDescription));
        OnPropertyChanged(nameof(ThemeDescription));

        SaveSettings();

        if (apiChanged || intervalChanged)
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
        _settings.ShowAtcRegions = Map.ShowAtcRegions;
        _settings.ShowAtcStations = Map.ShowAtcStations;

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
        _api?.Dispose();
        Map.Dispose();
    }

    private void ConfigureTracker()
    {
        _tracker?.Dispose();
        _api?.Dispose();

        _api = new IvaoApiClient(new IvaoApiOptions
        {
            ApiKey = string.IsNullOrWhiteSpace(_settings.ApiKey) ? null : _settings.ApiKey
        });

        _tracker = new TrackerService(_api, TimeSpan.FromSeconds(_settings.RefreshSeconds));
        _tracker.SnapshotUpdated += OnSnapshotUpdated;
        _tracker.RefreshFailed += OnRefreshFailed;
        _tracker.Start();
        _refreshCommand.RaiseCanExecuteChanged();
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
    }

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
