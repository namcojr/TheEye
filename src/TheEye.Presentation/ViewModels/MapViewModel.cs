using System.Collections.ObjectModel;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Reference;
using TheEye.Core.Services;
using TheEye.Core.Settings;
using TheEye.Presentation.Services;
using TheEye.Presentation.Threading;

namespace TheEye.Presentation.ViewModels;

/// <summary>
/// Owns the Mapsui map, the theme palettes handed to it, and the live pilot and
/// ATC collections. Everything on the map, aircraft included, is drawn by Mapsui
/// in one pass, so nothing can land on a different frame than the map under it;
/// the pointer is matched against the same positions here.
/// </summary>
public sealed class MapViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Smallest area framed around a selected flight, so a short hop does not
    /// zoom all the way down to a single apron.
    /// </summary>
    private const double MinimumFrameSize = 40000;

    /// <summary>
    /// How far a visible aircraft has to have moved, in pixels, before the map is
    /// redrawn for it. Airliners crawl at the zooms that show a continent, so most
    /// of the aircraft timer's ticks would otherwise repaint the same picture.
    /// </summary>
    private const double MotionThreshold = 0.25;

    /// <summary>The drop shadow of a pin when the palette names none.</summary>
    private const string DefaultAircraftShadow = "#66000000";

    /// <summary>The shortest and the longest wait before looking at the aircraft again.</summary>
    private static readonly TimeSpan ShortestMotionWait = TimeSpan.FromMilliseconds(30);

    private static readonly TimeSpan LongestMotionWait = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How long after a refresh motion is followed at the full rate: the aircraft
    /// ease towards their new reports, which the speed they fly at says nothing of.
    /// </summary>
    private static readonly TimeSpan CorrectionWindow = AircraftMotion.CorrectionDuration + TimeSpan.FromMilliseconds(100);

    private readonly IAircraftIconCache _icons;
    private readonly IImageCache _images;
    private readonly IImagePipeline _pipeline;
    private readonly TimeProvider _clock;
    private readonly string? _basemapCacheDirectory;
    private readonly Func<NavigraphRouteResolver?>? _navDataResolver;
    private GeoCoordinate? _hoverRoutePosition;
    private GeoCoordinate? _selectedRoutePosition;
    private readonly Dictionary<long, PilotItem> _pilotsById = [];
    private readonly Dictionary<long, AtcItem> _atcById = [];
    private IReadOnlySet<long> _supervisorUserIds = new HashSet<long>();

    private Map? _map;
    private MapPalette? _palette;
    private AtcRegionIndex? _regionIndex;
    private MemoryLayer? _atcRegionsLayer;
    private MemoryLayer? _atcLabelsLayer;
    private MemoryLayer? _atcStationsLayer;
    private MemoryLayer? _routeLayer;
    private MemoryLayer? _liveTrailLayer;
    private MemoryLayer? _flightPlanRoutesLayer;
    private MemoryLayer? _hoverRouteLayer;
    private PilotItem? _hoveredPilot;
    private MemoryLayer? _selectedAtcLayer;
    private MemoryLayer? _airportLayer;
    private AircraftLayer? _aircraftLayer;
    private bool _aircraftListChanged;
    private double[] _motionBuffer = [];
    private long _nextMotionCheck;
    private long _lastMotion;
    private long _correctingUntil;
    private bool _isPointerOverAircraft;
    private int? _atcSignature;
    private PilotItem? _selectedPilot;
    private AtcItem? _selectedAtc;
    private AirportRecord? _selectedAirport;
    private AtcItem? _hoveredAtc;
    private double _atcHoverX;
    private double _atcHoverY;
    private string? _mapTooltipText;
    private double _mapTooltipX;
    private double _mapTooltipY;
    private bool _showPilots = true;
    private bool _showAtc = true;
    private bool _showAirports = true;
    private IReadOnlyList<IvaoAtc> _atc = [];
    private IReadOnlyList<IvaoPilotTrack> _selectionTracks = [];
    private bool _selectionTracksPending;
    private IReadOnlyList<IvaoMapLayers.AtcStationPlacement> _stationPlacements = [];
    private AtcStationHitTarget[] _stationTargets = [];
    private IReadOnlyList<AirportRecord> _trafficAirports = [];
    private readonly List<(AirportRecord Airport, double X, double Y)> _trafficAirportTargets = [];
    private string _airportsKey = string.Empty;

    /// <summary>
    /// True when the viewport moved since the screen positions the pointer logic
    /// uses were worked out. The map can move on every frame and the pointer only
    /// needs them when it asks, so they wait for that. Set from any thread.
    /// </summary>
    private volatile bool _screenPositionsStale;

    private int _pointerResetQueued;
    private bool _disposed;
    private int _aircraftGlyphSize = TheEyeSettings.DefaultAircraftGlyphSize;
    private string _mapStyle = MapBasemap.Offline;

    public int AircraftGlyphSize
    {
        get => _aircraftGlyphSize;
        set
        {
            var size = Math.Clamp(value, TheEyeSettings.MinAircraftGlyphSize, TheEyeSettings.MaxAircraftGlyphSize);
            if (!SetProperty(ref _aircraftGlyphSize, size)) return;
            foreach (var pilot in Pilots) pilot.GlyphSize = size;
            UpdatePilotPositions();
            Map?.RefreshGraphics();
        }
    }

    /// <summary>The interface font scale, which the callsigns on the map follow.</summary>
    public double AircraftLabelScale
    {
        get => _aircraftLabelScale;
        set
        {
            if (SetProperty(ref _aircraftLabelScale, value) && _aircraftLayer is { } layer)
            {
                layer.LabelScale = value;
                Map?.RefreshGraphics();
            }
        }
    }

    private double _aircraftLabelScale = 1;

    /// <summary>
    /// The basemap the map is drawn with. <see cref="MapBasemap.Offline"/> paints
    /// the bundled country map; every other id streams that raster style from
    /// IVAO's tile service and hides the bundled fill in the meantime. The traffic
    /// layers ignore this, so a style switch leaves the aircraft where they are.
    /// </summary>
    public string MapStyle
    {
        get => _mapStyle;
        set
        {
            var style = MapBasemap.Normalize(value);
            if (!SetProperty(ref _mapStyle, style))
            {
                return;
            }

            if (Map is { } map)
            {
                MapBasemap.Apply(map, style, _basemapCacheDirectory);
            }

            // The pins are painted white or dark grey depending on the basemap,
            // so the same switch that swaps the tiles repaints the traffic.
            UpdateAircraftIconColors();
            _airportsKey = string.Empty;
            UpdateAirports();
            Map?.RefreshGraphics();
        }
    }

    /// <summary>The colour of the artwork for the basemap and theme currently drawn.</summary>
    private string AircraftIconColor => _palette is { } palette
        ? AircraftMarkerStyles.IconColor(palette, _mapStyle)
        : AircraftMarkerStyles.LightIcon;

    /// <summary>
    /// Repaints every pin after the basemap or the theme changed. New traffic picks
    /// the colour up when it is created.
    /// </summary>
    private void UpdateAircraftIconColors()
    {
        var color = AircraftIconColor;
        foreach (var pilot in Pilots)
        {
            pilot.IconColor = color;
        }
    }

    /// <summary>
    /// Wires the map to a theme palette. <paramref name="basemapCacheDirectory"/>
    /// overrides where the raster styles keep their tiles; the shells leave it
    /// null so the cache lives in the application data directory, a test points
    /// it at a temporary one.
    /// </summary>
    public MapViewModel(
        IImagePipeline images,
        TimeProvider? timeProvider = null,
        string? basemapCacheDirectory = null,
        Func<NavigraphRouteResolver?>? navDataResolver = null)
    {
        ArgumentNullException.ThrowIfNull(images);

        _pipeline = images;
        _clock = timeProvider ?? TimeProvider.System;
        _basemapCacheDirectory = basemapCacheDirectory;
        _navDataResolver = navDataResolver;
        _icons = images.AircraftIcons;
        _images = images.SessionImages;
        Pilots = [];
        Atc = [];
        Pilots.CollectionChanged += (_, _) => _aircraftListChanged = true;
    }

    public event EventHandler? SelectionChanged;

    public ObservableCollection<PilotItem> Pilots { get; }

    public ObservableCollection<AtcItem> Atc { get; }

    public Map? Map
    {
        get => _map;
        private set => SetProperty(ref _map, value);
    }

    public PilotItem? SelectedPilot
    {
        get => _selectedPilot;
        private set => SetProperty(ref _selectedPilot, value);
    }

    public AtcItem? SelectedAtc
    {
        get => _selectedAtc;
        private set => SetProperty(ref _selectedAtc, value);
    }

    public AirportRecord? SelectedAirport
    {
        get => _selectedAirport;
        private set => SetProperty(ref _selectedAirport, value);
    }

    public AtcItem? HoveredAtc
    {
        get => _hoveredAtc;
        private set
        {
            if (SetProperty(ref _hoveredAtc, value))
            {
                OnPropertyChanged(nameof(HasHoveredAtc));
            }
        }
    }

    public bool HasHoveredAtc => HoveredAtc is not null;

    /// <summary>
    /// True while the pointer rests on an aircraft. The pins are painted rather
    /// than being controls, so the shells read this to show the hand cursor.
    /// </summary>
    public bool IsPointerOverAircraft
    {
        get => _isPointerOverAircraft;
        private set => SetProperty(ref _isPointerOverAircraft, value);
    }

    public double AtcHoverX
    {
        get => _atcHoverX;
        private set => SetProperty(ref _atcHoverX, value);
    }

    public double AtcHoverY
    {
        get => _atcHoverY;
        private set => SetProperty(ref _atcHoverY, value);
    }

    public string? MapTooltipText
    {
        get => _mapTooltipText;
        private set
        {
            if (SetProperty(ref _mapTooltipText, value))
            {
                OnPropertyChanged(nameof(HasMapTooltip));
            }
        }
    }

    public bool HasMapTooltip => !string.IsNullOrWhiteSpace(MapTooltipText);

    public double MapTooltipX
    {
        get => _mapTooltipX;
        private set => SetProperty(ref _mapTooltipX, value);
    }

    public double MapTooltipY
    {
        get => _mapTooltipY;
        private set => SetProperty(ref _mapTooltipY, value);
    }

    public bool ShowPilots
    {
        get => _showPilots;
        set
        {
            if (!SetProperty(ref _showPilots, value))
            {
                return;
            }

            SetLayerEnabled(_aircraftLayer, value);
            if (!value)
            {
                SetHoveredPilot(null);
                IsPointerOverAircraft = false;
            }
        }
    }

    /// <summary>
    /// When true, picking something on the map — an aircraft marker, a station
    /// badge or a sector — also frames it. The traffic lists pass
    /// <c>center: true</c> explicitly, so finding an aircraft or a controller
    /// from the list keeps working even when this is switched off.
    /// </summary>
    public bool CenterOnMapClick { get; set; } = true;

    /// <summary>Sectors, sector labels and station badges share a single switch.</summary>
    public bool ShowAtc
    {
        get => _showAtc;
        set
        {
            if (SetProperty(ref _showAtc, value))
            {
                SetLayerEnabled(_atcRegionsLayer, value);
                SetLayerEnabled(_atcLabelsLayer, value);
                SetLayerEnabled(_atcStationsLayer, value);
                if (!value) SetHoveredAtc(null);
            }
        }
    }

    public bool ShowAirports
    {
        get => _showAirports;
        set
        {
            if (SetProperty(ref _showAirports, value))
            {
                _airportsKey = string.Empty;
                if (!value)
                {
                    MapTooltipText = null;
                }

                UpdateAirports();
            }
        }
    }

    public void Initialize(MapPalette palette, GeoCoordinate center, double resolution)
    {
        _palette = palette;
        var map = WorldMap.Create(palette, center, resolution);

        _atcRegionsLayer = IvaoMapLayers.CreateAtcRegions([], palette);
        _atcLabelsLayer = IvaoMapLayers.CreateAtcLabels([], palette);
        _atcStationsLayer = CreateStationLayer([]);
        _routeLayer = EmptyRouteLayer();
        _liveTrailLayer = IvaoMapLayers.CreateLiveTrail(null, null);
        _flightPlanRoutesLayer = IvaoMapLayers.CreateFlightPlanRoutes([], palette, navData: null);
        _hoverRouteLayer = IvaoMapLayers.CreateHoverRoute(null, palette);
        _selectedAtcLayer = IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), palette);

        _airportLayer = IvaoMapLayers.CreateAirports(
            [], palette, map.Navigator.Viewport.Resolution, showRunways: false);

        map.Layers.Add(_airportLayer);
        map.Layers.Add(_atcRegionsLayer);
        map.Layers.Add(_atcLabelsLayer);
        map.Layers.Add(_atcStationsLayer);
        map.Layers.Add(_routeLayer);
        map.Layers.Add(_liveTrailLayer);
        map.Layers.Add(_flightPlanRoutesLayer);
        map.Layers.Add(_hoverRouteLayer);
        map.Layers.Add(_selectedAtcLayer);

        // Last, so the aircraft stay above everything else on the map.
        _aircraftLayer = new AircraftLayer { LabelScale = _aircraftLabelScale };
        ApplyAircraftColors(palette);
        map.Layers.Add(_aircraftLayer);
        SetLayerEnabled(_aircraftLayer, _showPilots);

        SetLayerEnabled(_atcRegionsLayer, _showAtc);
        SetLayerEnabled(_atcLabelsLayer, _showAtc);
        SetLayerEnabled(_atcStationsLayer, _showAtc);

        // A saved tile style is restored with the view, so a restart returns to
        // the basemap the user left instead of the offline map.
        MapBasemap.Apply(map, _mapStyle, _basemapCacheDirectory);

        map.Navigator.ViewportChanged += OnViewportChanged;
        Map = map;
        UpdateAircraftIconColors();
        _screenPositionsStale = true;
        UpdateAirports();
        map.Refresh();
    }

    public void ApplyTheme(MapPalette palette)
    {
        _palette = palette;
        if (Map is not { } map)
        {
            return;
        }

        WorldMap.ApplyTheme(map, palette);
        ReplaceLayer(ref _atcRegionsLayer, IvaoMapLayers.CreateAtcRegions(Atc.Select(item => item.Atc), palette));
        ReplaceLayer(ref _atcLabelsLayer, IvaoMapLayers.CreateAtcLabels(Atc.Select(item => item.Atc), palette));
        ReplaceLayer(ref _atcStationsLayer, CreateStationLayer(Atc.Select(item => item.Atc)));
        ReplaceLayer(ref _selectedAtcLayer, SelectedAtc is { } selected
            ? IvaoMapLayers.CreateSelectedAtc(selected.Atc, palette, StationZoom)
            : IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), palette, StationZoom));
        ApplyAircraftColors(palette);

        // Repaints the route with the new palette colours; the recorded track of
        // the selection is kept, so a theme switch does not drop the trail.
        RebuildRouteLayer();
        RebuildFlightPlanRoutesLayer();
        RebuildHoverRouteLayer();

        // The airport pavement derives its colour from the palette, so it is
        // rebuilt even when the viewport is looking at the same airports.
        _airportsKey = string.Empty;
        UpdateAirports();

        // The offline map is painted with the palette, so a theme switch can turn
        // a dark map light and has to repaint the pins with it.
        UpdateAircraftIconColors();
        map.Refresh();
    }

    public void UpdateTraffic(TrackerSnapshot snapshot)
    {
        if (Map is null || _palette is null)
        {
            return;
        }

        SyncPilots(snapshot.Pilots);
        SyncAtc(snapshot.Atc);

        // The members with supervisor rights. The feed names the connection, so
        // the station badges they occupy can be ringed and the traffic rows of the
        // selection panel can say so; the list is rebuilt from every snapshot
        // because a supervisor may sign off without the traffic changing.
        _supervisorUserIds = snapshot.Supervisors
            .Where(supervisor => supervisor.UserId > 0)
            .Select(supervisor => supervisor.UserId)
            .ToHashSet();
        foreach (var pilot in Pilots)
        {
            pilot.IsSupervisor = _supervisorUserIds.Contains(pilot.UserId);
        }

        foreach (var item in Atc)
        {
            item.IsSupervisor = _supervisorUserIds.Contains(item.UserId);
        }

        var atc = snapshot.Atc;
        _atc = atc;

        // Controllers stay on for hours, so most refreshes leave the sectors as
        // they were; rebuilding them would also throw away their cached paths.
        var signature = AtcSignature(atc, _supervisorUserIds);
        if (signature != _atcSignature)
        {
            _atcSignature = signature;
            ReplaceLayer(ref _atcRegionsLayer, IvaoMapLayers.CreateAtcRegions(atc, _palette));
            ReplaceLayer(ref _atcLabelsLayer, IvaoMapLayers.CreateAtcLabels(atc, _palette));
            ReplaceLayer(ref _atcStationsLayer, CreateStationLayer(atc));
            _regionIndex = AtcRegionIndex.Build(atc);
        }

        if (HoveredAtc is { } hovered && !_atcById.ContainsKey(hovered.Id))
        {
            SetHoveredAtc(null);
        }
        UpdatePilotPositions();
        RebuildRouteLayer();
        RebuildFlightPlanRoutesLayer();
        RebuildHoverRouteLayer();
        UpdateAirports();

        // The new reports ease in over a second, so the motion clock starts over.
        var now = _clock.GetTimestamp();
        _lastMotion = now;
        _nextMotionCheck = now;
        _correctingUntil = now + (long)(CorrectionWindow.TotalSeconds * _clock.TimestampFrequency);
        Map.RefreshGraphics();
    }

    /// <summary>A fingerprint of what the ATC layers are built from.</summary>
    private static int AtcSignature(IReadOnlyList<IvaoAtc> atc, IReadOnlySet<long> supervisors)
    {
        var hash = new HashCode();
        foreach (var station in atc)
        {
            hash.Add(station.Id);
            hash.Add(station.Callsign);
            hash.Add(station.Facility);
            hash.Add(station.FrequencyText);
            hash.Add(station.Position);
            hash.Add(supervisors.Contains(station.UserId));
            foreach (var point in station.PolygonPoints)
            {
                hash.Add(point);
            }
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Reports where the pointer is, or null when it left the map or started a
    /// drag: everything under it reacts from here, the aircraft included, because
    /// they are painted and receive no pointer events of their own.
    /// </summary>
    public void SetHoveredAtc(ScreenPosition? position)
    {
        if (position is not { } screen || Map is not { } map)
        {
            IsPointerOverAircraft = false;
            SetHoveredPilot(null);
            HoveredAtc = null;
            MapTooltipText = null;
            return;
        }

        var viewport = map.Navigator.Viewport;
        var pointer = OntoNearestCopy(screen);
        var marker = PilotMarkerAt(screen);
        IsPointerOverAircraft = marker is not null;

        // A selected aircraft draws its route anyway, so it takes the plain tooltip below.
        if (marker is not null && !ReferenceEquals(marker, SelectedPilot))
        {
            HoveredAtc = null;
            SetHoveredPilot(marker);
            return;
        }

        SetHoveredPilot(null);
        if (HitTestPilot(screen) is { } pilot)
        {
            HoveredAtc = null;
            SetMapTooltip(pilot.Tooltip, screen, viewport);
            return;
        }

        AirportRecord? airport = null;
        var airportDistance = 8d;
        foreach (var target in _trafficAirportTargets)
        {
            var dx = target.X - pointer.X;
            var dy = target.Y - pointer.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= airportDistance)
            {
                airport = target.Airport;
                airportDistance = distance;
            }
        }

        if (airport is not null)
        {
            HoveredAtc = null;
            SetMapTooltip($"{airport.DisplayName} ({airport.Icao})", screen, viewport);
            return;
        }

        if (!_showAtc)
        {
            HoveredAtc = null;
            MapTooltipText = null;
            return;
        }

        var world = viewport.ScreenToWorld(screen.X, screen.Y);
        var station = AtcStationHitTest.Find(_stationTargets, pointer.X, pointer.Y,
            regions: _regionIndex?.FindAllAt(world));
        HoveredAtc = station is not null && _atcById.TryGetValue(station.Id, out var item) ? item : null;
        AtcHoverX = Math.Clamp(screen.X + 14, 0, Math.Max(0, viewport.Width - 160));
        AtcHoverY = Math.Clamp(screen.Y + 14, 0, Math.Max(0, viewport.Height - 56));
        SetMapTooltip(HoveredAtc is { } hovered ? $"{hovered.Callsign}\n{hovered.Frequency}" : null,
            screen, viewport);
    }

    public void SetHoveredPilot(PilotItem? pilot)
    {
        pilot = _showPilots && !ReferenceEquals(pilot, SelectedPilot) ? pilot : null;
        if (ReferenceEquals(_hoveredPilot, pilot))
        {
            if (pilot is not null && Map is { } currentMap)
            {
                SetMapTooltip(pilot.Tooltip,
                    new ScreenPosition(pilot.X + pilot.MarkerSize, pilot.Y), currentMap.Navigator.Viewport);
            }
            return;
        }

        // The pin under the pointer grows; the one it left returns to its own size.
        if (_hoveredPilot is { } previous)
        {
            previous.IsHovered = false;
        }

        _hoveredPilot = pilot;

        if (pilot is not null)
        {
            pilot.IsHovered = true;
            HoveredAtc = null;
            if (Map is { } map)
            {
                SetMapTooltip(pilot.Tooltip,
                    new ScreenPosition(pilot.X + pilot.MarkerSize, pilot.Y), map.Navigator.Viewport);
            }
        }
        else
        {
            MapTooltipText = null;
        }

        RebuildHoverRouteLayer();
    }

    public void SelectPilot(PilotItem? pilot) => SelectPilot(pilot, center: true);

    /// <summary>
    /// Selects an aircraft and, when <paramref name="center"/> is true, frames
    /// it together with its route. Map clicks pass the user's choice so the
    /// selectable markers can stay put, while the traffic list always frames
    /// the flight it just picked.
    /// </summary>
    public void SelectPilot(PilotItem? pilot, bool center)
    {
        SetHoveredPilot(null);
        if (ReferenceEquals(SelectedPilot, pilot))
        {
            return;
        }

        if (SelectedPilot is { } previous)
        {
            previous.IsSelected = false;
        }

        if (SelectedAtc is { } previousAtc)
        {
            previousAtc.IsSelected = false;
            SelectedAtc = null;
            ReplaceLayer(ref _selectedAtcLayer, IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), _palette ?? DefaultPalette));
        }

        SelectedAirport = null;
        SelectedPilot = pilot;
        _selectionTracks = [];
        _selectionTracksPending = pilot is not null;
        if (pilot is not null)
        {
            pilot.IsSelected = true;
        }

        RebuildRouteLayer();
        RebuildFlightPlanRoutesLayer();

        if (pilot is not null && center)
        {
            // A row picked in the traffic list must also bring the aircraft and
            // its route into view; a click on the marker only does so when the
            // user kept map-click centring enabled.
            CenterOnPilot(pilot);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAtc(AtcItem? atc) => SelectAtc(atc, center: true);

    /// <summary>
    /// Selects a station and, when <paramref name="center"/> is true, brings it
    /// into view. See <see cref="SelectPilot(PilotItem?, bool)"/> for why the
    /// caller decides.
    /// </summary>
    public void SelectAtc(AtcItem? atc, bool center)
    {
        if (ReferenceEquals(SelectedAtc, atc))
        {
            return;
        }

        if (SelectedPilot is { } previousPilot)
        {
            previousPilot.IsSelected = false;
            SelectedPilot = null;
        }

        if (SelectedAtc is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedAirport = null;
        SelectedAtc = atc;
        _selectionTracks = [];

        // Only aircraft are traced, so a station never waits for a track.
        _selectionTracksPending = false;
        if (atc is not null)
        {
            atc.IsSelected = true;
            ReplaceLayer(ref _selectedAtcLayer, IvaoMapLayers.CreateSelectedAtc(atc.Atc, _palette ?? DefaultPalette, StationZoom));

            // Selecting a station in the ATC list centres it too; a click on the
            // map only centres while the option is enabled.
            if (center)
            {
                CenterOnAtc(atc);
            }
        }
        else
        {
            ReplaceLayer(ref _selectedAtcLayer, IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), _palette ?? DefaultPalette));
        }

        RebuildRouteLayer();
        RebuildFlightPlanRoutesLayer();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        SelectPilot(null);
        if (SelectedAtc is not null)
        {
            SelectAtc(null);
        }

        SelectAirport(null);
    }

    public void SelectAirport(AirportRecord? airport)
    {
        if (ReferenceEquals(SelectedAirport, airport))
        {
            return;
        }

        if (SelectedPilot is { } previousPilot)
        {
            previousPilot.IsSelected = false;
            SelectedPilot = null;
        }

        if (SelectedAtc is { } previousAtc)
        {
            previousAtc.IsSelected = false;
            SelectedAtc = null;
            ReplaceLayer(ref _selectedAtcLayer, IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), _palette ?? DefaultPalette));
        }

        SelectedAirport = airport;
        _selectionTracks = [];
        _selectionTracksPending = false;
        RebuildRouteLayer();
        RebuildFlightPlanRoutesLayer();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hands the recorded track of the selected flight to the map. The route layer
    /// is redrawn straight away and again on every snapshot or theme change. While
    /// <paramref name="pending"/> is true the request is still running, so no line
    /// is drawn at all: the estimate of the filed route would be replaced by the
    /// recorded trail a moment later, and the line would visibly jump. Once IVAO
    /// has answered — with a track or without one — the map draws what it knows.
    /// A station has no trail, so nothing is drawn for one.
    /// </summary>
    public void SetSelectionTracks(IReadOnlyList<IvaoPilotTrack>? tracks, bool pending = false)
    {
        _selectionTracks = tracks ?? [];
        _selectionTracksPending = pending;
        if (SelectedPilot is null)
        {
            return;
        }

        RebuildRouteLayer();
    }

    public void SetSelectionFlightPlan(SessionFlightPlanResult result)
    {
        if (SelectedPilot is not { } pilot)
        {
            return;
        }

        pilot.ApplyFlightPlan(result);
        RebuildRouteLayer();
        RebuildFlightPlanRoutesLayer();
        RebuildHoverRouteLayer();
    }

    /// <summary>
    /// A click or tap on the map. A hit on an aircraft selects it; a further one
    /// brings the flight back into view, in case the map was panned away, unless
    /// the user switched map-click centring off. Anything else goes to
    /// <see cref="SelectAt"/>.
    /// </summary>
    public void TapAt(MPoint worldPosition, ScreenPosition screenPosition)
    {
        if (PilotMarkerAt(screenPosition) is not { } pilot)
        {
            SelectAt(worldPosition, screenPosition);
            return;
        }

        SetHoveredPilot(null);
        if (!ReferenceEquals(SelectedPilot, pilot))
        {
            SelectPilot(pilot, CenterOnMapClick);
        }
        else if (CenterOnMapClick)
        {
            CenterOnPilot(pilot);
        }
    }

    /// <summary>
    /// Picks the pilot, station badge or ATC sector under a click on the map.
    /// Centring follows <see cref="CenterOnMapClick"/> because the map is where
    /// the user is already looking.
    /// </summary>
    public void SelectAt(MPoint worldPosition, ScreenPosition screenPosition)
    {
        RefreshScreenPositions();
        screenPosition = OntoNearestCopy(screenPosition);
        if (HitTestPilot(screenPosition) is { } pilot)
        {
            if (ReferenceEquals(SelectedPilot, pilot) && HitTestAirport(screenPosition) is { } overlappingAirport)
            {
                SelectAirport(overlappingAirport);
                return;
            }

            SelectPilot(pilot, CenterOnMapClick);
            return;
        }

        var atc = _showAtc
            ? AtcStationHitTest.Find(_stationTargets, screenPosition.X, screenPosition.Y,
                SelectedAtc?.Id, _regionIndex?.FindAllAt(worldPosition))
            : null;
        if (atc is not null && _atcById.TryGetValue(atc.Id, out var item))
        {
            if (ReferenceEquals(SelectedAtc, item) &&
                HitTestAirport(screenPosition) is { } stationAirport &&
                string.Equals(item.AirportIcao, stationAirport.Icao, StringComparison.OrdinalIgnoreCase))
            {
                SelectAirport(stationAirport);
                return;
            }

            SelectAtc(item, CenterOnMapClick);
            return;
        }

        if (HitTestAirport(screenPosition) is { } airport)
        {
            SelectAirport(airport);
            return;
        }

        ClearSelection();
    }

    private AirportRecord? HitTestAirport(ScreenPosition position)
    {
        RefreshScreenPositions();
        position = OntoNearestCopy(position);
        AirportRecord? best = null;
        var bestDistanceSquared = 9d * 9d;

        foreach (var target in _trafficAirportTargets)
        {
            var dx = target.X - position.X;
            var dy = target.Y - position.Y;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = target.Airport;
            }
        }

        return best;
    }

    private ScreenPosition OntoNearestCopy(ScreenPosition position) =>
        Map is { } map ? WorldWrap.OntoNearestCopy(position, map.Navigator.Viewport) : position;

    public PilotItem? HitTestPilot(ScreenPosition position)
    {
        // Hidden markers must not swallow clicks, otherwise a parked aircraft
        // sitting on an airport blocks the tower and ground badges under it.
        if (!_showPilots)
        {
            return null;
        }

        RefreshScreenPositions();
        position = OntoNearestCopy(position);
        PilotItem? best = null;
        var bestDistanceSquared = 22d * 22d;

        foreach (var pilot in Pilots)
        {
            if (!pilot.IsVisible)
            {
                continue;
            }

            var dx = pilot.X + pilot.MarkerSize / 2d - position.X;
            var dy = pilot.Y + pilot.MarkerSize / 2d - position.Y;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = pilot;
            }
        }

        return best;
    }

    /// <summary>
    /// The aircraft whose pin covers a screen position, if any. The pins are drawn
    /// in list order, so the last one that covers the position is the one on top.
    /// </summary>
    public PilotItem? PilotMarkerAt(ScreenPosition position)
    {
        if (!_showPilots)
        {
            return null;
        }

        RefreshScreenPositions();
        position = OntoNearestCopy(position);
        for (var index = Pilots.Count - 1; index >= 0; index--)
        {
            var pilot = Pilots[index];
            if (!pilot.IsVisible)
            {
                continue;
            }

            var size = pilot.MarkerSize;
            if (position.X >= pilot.X && position.X <= pilot.X + size &&
                position.Y >= pilot.Y && position.Y <= pilot.Y + size)
            {
                return pilot;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the station badge drawn under a screen position, if any. A second
    /// click on the same airport steps on to the next stacked position, so the
    /// rings drawn on top of each other can all be reached.
    /// </summary>
    public AtcItem? HitTestStation(ScreenPosition position)
    {
        RefreshScreenPositions();
        position = OntoNearestCopy(position);
        if (!_showAtc
            || AtcStationHitTest.Find(_stationTargets, position.X, position.Y, SelectedAtc?.Id) is not { } station)
        {
            return null;
        }

        return _atcById.TryGetValue(station.Id, out var item) ? item : null;
    }

    /// <summary>
    /// Brings a flight into view: the aircraft plus the great-circle route drawn
    /// between its departure and arrival airports are framed together.
    /// </summary>
    public void CenterOnPilot(PilotItem pilot)
    {
        if (Map is not { } map || pilot.MapPosition is not { } position)
        {
            return;
        }

        // The nearest copy of the aircraft, so the map moves the short way round.
        var aircraft = NearestCopy(WorldMap.Project(position), map.Navigator.Viewport.CenterX);
        var flightPlan = pilot.Pilot.FlightPlan;
        var departure = flightPlan?.Departure?.Position;
        var arrival = flightPlan?.Arrival?.Position;

        if (departure is not { } startCoordinate || arrival is not { } endCoordinate)
        {
            map.Navigator.CenterOn(aircraft, 250);
            return;
        }

        // ZoomToBox divides by the viewport size, so it needs a laid out map.
        if (map.Navigator.Viewport is not { Width: > 0, Height: > 0 })
        {
            return;
        }

        // A route across the date line is framed in one piece, past the edge of the world.
        var start = NearestCopy(WorldMap.Project(startCoordinate), aircraft.X);
        var end = NearestCopy(WorldMap.Project(endCoordinate), aircraft.X);
        var minX = Math.Min(aircraft.X, Math.Min(start.X, end.X));
        var maxX = Math.Max(aircraft.X, Math.Max(start.X, end.X));
        var minY = Math.Min(aircraft.Y, Math.Min(start.Y, end.Y));
        var maxY = Math.Max(aircraft.Y, Math.Max(start.Y, end.Y));

        // Keep a little breathing room so the aircraft and both airports never
        // end up glued to the edge of the map.
        var paddingX = Math.Max((maxX - minX) * 0.1, MinimumFrameSize / 2);
        var paddingY = Math.Max((maxY - minY) * 0.1, MinimumFrameSize / 2);
        var frame = new MRect(minX - paddingX, minY - paddingY, maxX + paddingX, maxY + paddingY);

        // The navigator gives up on an animation that hits the zoom limit, so a
        // flight too long to frame would not move the map at all.
        var viewport = map.Navigator.Viewport;
        if (Math.Max(frame.Width / viewport.Width, frame.Height / viewport.Height) > WorldMap.MaxZoomOutResolution)
        {
            map.Navigator.CenterOn(aircraft, 250);
            return;
        }

        map.Navigator.ZoomToBox(frame, MBoxFit.Fit, 350);
    }

    /// <summary>Brings an ATC position into view without zooming out from a closer view.</summary>
    public void CenterOnAtc(AtcItem atc)
    {
        if (Map is null || atc.Atc.Position is not { } position)
        {
            return;
        }

        var viewport = Map.Navigator.Viewport;
        Map.Navigator.CenterOnAndZoomTo(
            NearestCopy(WorldMap.Project(position), viewport.CenterX),
            Math.Min(viewport.Resolution, 8000),
            300);
    }

    public void ZoomIn() => Map?.Navigator.ZoomIn(250);

    public void ZoomOut() => Map?.Navigator.ZoomOut(250);

    public void ResetView(GeoCoordinate center, double resolution) =>
        Map?.Navigator.CenterOnAndZoomTo(
            NearestCopy(WorldMap.Project(center), Map.Navigator.Viewport.CenterX),
            resolution,
            350);

    private static MPoint NearestCopy(MPoint point, double x) =>
        new(WorldWrap.NearestCopy(point.X, x), point.Y);

    public (GeoCoordinate Center, double Resolution) GetView()
    {
        if (Map is not { } map || map.Navigator.Viewport.Width <= 0)
        {
            return (new GeoCoordinate(45, 10), WorldMap.MaxZoomOutResolution);
        }

        var viewport = map.Navigator.Viewport;
        return (WorldMap.Unproject(viewport.CenterX, viewport.CenterY), viewport.Resolution);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pipeline.Dispose();
        if (Map is not null)
        {
            Map.Navigator.ViewportChanged -= OnViewportChanged;

            // The tile layer owns the client that downloads its tiles, and Map
            // does not dispose layers: taking the basemap off through the offline
            // style releases it before the map goes away.
            MapBasemap.Apply(Map, MapBasemap.Offline);
            Map.Dispose();
            Map = null;
        }
    }

    private void SyncPilots(IReadOnlyList<IvaoPilot> pilots)
    {
        var incoming = new HashSet<long>();
        foreach (var pilot in pilots)
        {
            if (pilot.LastTrack?.Position is null)
            {
                continue;
            }

            incoming.Add(pilot.Id);
            if (_pilotsById.TryGetValue(pilot.Id, out var existing))
            {
                existing.Update(pilot);
            }
            else
            {
                var item = new PilotItem(pilot, _icons, _images, _clock)
                {
                    GlyphSize = AircraftGlyphSize,
                    IconColor = AircraftIconColor
                };
                _pilotsById.Add(pilot.Id, item);
                Pilots.Add(item);
            }
        }

        foreach (var stale in Pilots.Where(item => !incoming.Contains(item.Id)).ToList())
        {
            if (ReferenceEquals(stale, _hoveredPilot))
            {
                SetHoveredPilot(null);
            }

            if (ReferenceEquals(stale, SelectedPilot))
            {
                SelectPilot(null);
            }

            Pilots.Remove(stale);
            _pilotsById.Remove(stale.Id);
        }
    }

    private void SyncAtc(IReadOnlyList<IvaoAtc> atc)
    {
        var incoming = new HashSet<long>();
        foreach (var station in atc)
        {
            if (!incoming.Add(station.Id))
            {
                continue;
            }

            if (_atcById.TryGetValue(station.Id, out var existing))
            {
                existing.Update(station);
            }
            else
            {
                var item = new AtcItem(station, _images);
                _atcById.Add(station.Id, item);
                Atc.Add(item);
            }
        }

        foreach (var stale in Atc.Where(item => !incoming.Contains(item.Id)).ToList())
        {
            if (ReferenceEquals(stale, SelectedAtc))
            {
                SelectAtc(null);
            }

            Atc.Remove(stale);
            _atcById.Remove(stale.Id);
        }
    }

    /// <summary>
    /// Moves the aircraft along their predicted paths. The shells call this from a
    /// fast timer; the map is only redrawn once a visible aircraft has moved far
    /// enough on screen to show it, and the next look at the aircraft waits for
    /// about when that will be, so the rate follows what the zoom can show.
    /// </summary>
    public void AdvanceAircraft()
    {
        if (_disposed || !_showPilots || Map is not { } map)
        {
            return;
        }

        var now = _clock.GetTimestamp();
        if (now < _nextMotionCheck)
        {
            return;
        }

        var viewport = map.Navigator.Viewport;
        var moved = MeasureMotion(viewport);
        if (moved >= MotionThreshold)
        {
            ApplyMotion(viewport);
            FollowMovedAircraft();
            map.RefreshGraphics();
        }

        ScheduleMotionCheck(now, moved);
    }

    /// <summary>
    /// Works out where every aircraft is now and returns how far, in pixels, the
    /// one that moved most on screen has got. The selected and the hovered aircraft
    /// count wherever they are, because their routes end at them. Nothing is
    /// published yet: most of the time the answer is that nothing needs to be.
    /// </summary>
    private double MeasureMotion(Viewport viewport)
    {
        var count = Pilots.Count;
        if (_motionBuffer.Length < count * 2)
        {
            _motionBuffer = new double[count * 2];
        }

        var resolution = viewport.Resolution;
        var reachX = viewport.Width / 2 * resolution;
        var reachY = viewport.Height / 2 * resolution;
        var largest = 0d;
        for (var index = 0; index < count; index++)
        {
            var item = Pilots[index];
            var marker = item.Marker;
            if (item.MapPosition is not { } position)
            {
                _motionBuffer[index * 2] = double.NaN;
                if (marker.HasPosition)
                {
                    largest = double.PositiveInfinity;
                }

                continue;
            }

            var (x, y) = WorldMap.ProjectToWorld(position);
            _motionBuffer[index * 2] = x;
            _motionBuffer[index * 2 + 1] = y;
            var followed = ReferenceEquals(item, _selectedPilot) || ReferenceEquals(item, _hoveredPilot);
            if (followed ||
                (Math.Abs(WorldWrap.Wrap(x - viewport.CenterX)) <= reachX && Math.Abs(y - viewport.CenterY) <= reachY))
            {
                largest = Math.Max(largest, Math.Max(Math.Abs(x - marker.WorldX), Math.Abs(y - marker.WorldY)));
            }
        }

        return largest / resolution;
    }

    /// <summary>Publishes the positions <see cref="MeasureMotion"/> worked out.</summary>
    private void ApplyMotion(Viewport viewport)
    {
        PublishAircraftList();
        for (var index = 0; index < Pilots.Count; index++)
        {
            var item = Pilots[index];
            var x = _motionBuffer[index * 2];
            item.Marker.HasPosition = !double.IsNaN(x);
            if (item.Marker.HasPosition)
            {
                item.Marker.WorldX = x;
                item.Marker.WorldY = _motionBuffer[index * 2 + 1];
            }

            PlacePin(item, viewport);
        }
    }

    /// <summary>Keeps the lines that end at a moving aircraft attached to it.</summary>
    private void FollowMovedAircraft()
    {
        if (SelectedPilot?.MapPosition != _selectedRoutePosition)
        {
            RebuildLiveTrailLayer();
            RebuildFlightPlanRoutesLayer();
        }

        if (_hoveredPilot is { } pilot && pilot.MapPosition != _hoverRoutePosition)
        {
            RebuildHoverRouteLayer();
        }
    }

    private void ScheduleMotionCheck(long now, double moved)
    {
        var frequency = (double)_clock.TimestampFrequency;
        var elapsed = (now - _lastMotion) / frequency;
        var speed = elapsed > 0 ? moved / elapsed : 0;
        var remaining = moved >= MotionThreshold ? MotionThreshold : MotionThreshold - moved;
        if (moved >= MotionThreshold)
        {
            _lastMotion = now;
        }

        var wait = now < _correctingUntil
            ? ShortestMotionWait.TotalSeconds
            : speed > 0 && double.IsFinite(speed)
                ? Math.Clamp(remaining / speed, ShortestMotionWait.TotalSeconds, LongestMotionWait.TotalSeconds)
                : LongestMotionWait.TotalSeconds;
        _nextMotionCheck = now + (long)(wait * frequency);
    }

    /// <summary>Tells the map something on the aircraft changed without them moving.</summary>
    public void RefreshAircraft() => Map?.RefreshGraphics();

    private void PublishAircraftList()
    {
        if (!_aircraftListChanged || _aircraftLayer is not { } layer)
        {
            return;
        }

        _aircraftListChanged = false;
        var markers = new AircraftMarker[Pilots.Count];
        for (var index = 0; index < markers.Length; index++)
        {
            markers[index] = Pilots[index].Marker;
        }

        layer.Markers = markers;
    }

    /// <summary>Brings every aircraft up to the moment, whether it moved far or not.</summary>
    private void UpdatePilotPositions()
    {
        if (Map is not { } map)
        {
            return;
        }

        PublishAircraftList();
        var viewport = map.Navigator.Viewport;
        foreach (var item in Pilots)
        {
            var marker = item.Marker;
            if (item.MapPosition is { } position)
            {
                (marker.WorldX, marker.WorldY) = WorldMap.ProjectToWorld(position);
                marker.HasPosition = true;
            }
            else
            {
                marker.HasPosition = false;
            }

            PlacePin(item, viewport);
        }
    }

    /// <summary>Works out where the box of a pin sits on the current viewport.</summary>
    private static void PlacePin(PilotItem item, Viewport viewport)
    {
        var marker = item.Marker;
        if (!marker.HasPosition || viewport.Width <= 0 || viewport.Height <= 0)
        {
            item.SetScreenPosition(0, 0, false);
            return;
        }

        var (x, y) = WorldWrap.ToScreen(viewport, marker.WorldX, marker.WorldY);
        var size = item.MarkerSize;
        var visible = x >= -size && y >= -size && x <= viewport.Width + size && y <= viewport.Height + size;
        item.SetScreenPosition(x - size / 2d, y - size / 2d, visible);
    }

    /// <summary>
    /// Mapsui advances its animations on a worker thread, so during an animated
    /// zoom this runs there, once per frame. All it does is note that the screen
    /// positions are out of date: the renderer places everything from the viewport
    /// of the frame it draws, and the pointer logic catches up when it next asks.
    /// </summary>
    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e)
    {
        _screenPositionsStale = true;

        // Whatever the pointer was resting on has just moved out from under it.
        if ((_hoveredPilot is not null || _hoveredAtc is not null || _mapTooltipText is not null || _isPointerOverAircraft) &&
            Interlocked.Exchange(ref _pointerResetQueued, 1) == 0)
        {
            UiThread.Post(ResetPointer);
        }
    }

    private void ResetPointer()
    {
        Volatile.Write(ref _pointerResetQueued, 0);
        if (!_disposed)
        {
            SetHoveredAtc(null);
        }
    }

    /// <summary>Re-projects what the pointer is tested against, if the map has moved.</summary>
    private void RefreshScreenPositions()
    {
        if (!_screenPositionsStale || Map is not { } map)
        {
            return;
        }

        _screenPositionsStale = false;
        var viewport = map.Navigator.Viewport;
        foreach (var item in Pilots)
        {
            PlacePin(item, viewport);
        }

        UpdateStationTargets();
        UpdateTrafficAirportTargets();
    }

    /// <summary>The scale the station badges are laid out with at the current zoom.</summary>
    private double StationZoom =>
        _map is { } map ? AtcMarkerStyles.ZoomScale(map.Navigator.Viewport.Resolution) : 1;

    /// <summary>
    /// Builds the station layer and keeps the placements used for hit testing in
    /// sync with the badges that are actually drawn. The badges of the members
    /// connected with supervisor rights carry a ring, so the layer is rebuilt with
    /// the current set on every change.
    /// </summary>
    private MemoryLayer CreateStationLayer(IEnumerable<IvaoAtc> atc)
    {
        var zoom = StationZoom;
        _stationPlacements = IvaoMapLayers.CreateAtcStationPlacements(atc, zoom);
        _screenPositionsStale = true;
        return IvaoMapLayers.CreateAtcStations(
            _stationPlacements,
            _palette ?? DefaultPalette,
            zoom,
            _supervisorUserIds);
    }

    /// <summary>
    /// Projects every badge onto the current viewport so a click can find it
    /// where it is drawn. The badges of one airport share the airport
    /// coordinate, so they are all tested at the same screen point.
    /// </summary>
    private void UpdateStationTargets()
    {
        if (Map is not { } map)
        {
            _stationTargets = [];
            return;
        }

        var viewport = map.Navigator.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            _stationTargets = [];
            return;
        }

        var targets = new List<AtcStationHitTarget>(_stationPlacements.Count);
        foreach (var placement in _stationPlacements)
        {
            var (x, y) = WorldWrap.ToScreen(viewport, placement.Point.X, placement.Point.Y);
            targets.Add(new AtcStationHitTarget(
                placement.Station,
                x,
                y,
                AtcMarkerStyles.HitRadius(AtcMarkerStyles.For(
                    placement.Station.Facility, placement.Station.Callsign,
                    AtcMarkerStyles.ZoomScale(viewport.Resolution))),
                AtcMarkerStyles.SortOrder(placement.Station.Facility, placement.Station.Callsign)));
        }

        _stationTargets = targets.ToArray();
    }

    /// <summary>
    /// Paints one marker and ICAO label per traffic airport. Runway detail and
    /// other airport labels are left to streamed basemaps.
    /// </summary>
    private void UpdateAirports()
    {
        if (Map is not { } map || _palette is null)
        {
            return;
        }

        var viewport = map.Navigator.Viewport;
        var trafficAirports = Pilots
            .SelectMany(pilot => new[]
            {
                AirportReference.Find(pilot.Pilot.FlightPlan?.DepartureIcao),
                AirportReference.Find(pilot.Pilot.FlightPlan?.ArrivalIcao)
            })
            .Where(airport => airport is not null)
            .Select(airport => airport!)
            .DistinctBy(airport => airport.Icao)
            .ToArray();
        IReadOnlyList<AirportRecord> visibleTrafficAirports = _showAirports ? trafficAirports : [];
        _trafficAirports = visibleTrafficAirports;
        _screenPositionsStale = true;
        if (SelectedAirport is { } selectedAirport &&
            !visibleTrafficAirports.Any(airport => airport.Icao == selectedAirport.Icao))
        {
            SelectAirport(null);
        }

        var key = $"{_showAirports}|{string.Join('|', visibleTrafficAirports.Select(airport => airport.Icao))}";
        if (key == _airportsKey)
        {
            return;
        }

        _airportsKey = key;
        ReplaceLayer(ref _airportLayer, IvaoMapLayers.CreateAirports(
            [], _palette, viewport.Resolution, showRunways: false, trafficAirports: visibleTrafficAirports));
    }

    private void UpdateTrafficAirportTargets()
    {
        _trafficAirportTargets.Clear();
        if (Map is not { } map)
        {
            return;
        }

        var viewport = map.Navigator.Viewport;
        foreach (var airport in _trafficAirports)
        {
            var point = WorldMap.Project(airport.Position);
            var (x, y) = WorldWrap.ToScreen(viewport, point.X, point.Y);
            _trafficAirportTargets.Add((airport, x, y));
        }
    }

    private void SetMapTooltip(string? text, ScreenPosition point, Viewport viewport)
    {
        MapTooltipText = text;
        MapTooltipX = Math.Clamp(point.X + 14, 0, Math.Max(0, viewport.Width - 190));
        MapTooltipY = Math.Clamp(point.Y + 14, 0, Math.Max(0, viewport.Height - 70));
    }

    private void RebuildHoverRouteLayer()
    {
        _hoverRoutePosition = _hoveredPilot?.MapPosition;
        ReplaceLayer(ref _hoverRouteLayer,
            IvaoMapLayers.CreateHoverRoute(
                _hoveredPilot?.Pilot,
                _palette ?? DefaultPalette,
                _hoverRoutePosition,
                showAirportLabels: false));
    }

    private void RebuildFlightPlanRoutesLayer()
    {
        ReplaceLayer(ref _flightPlanRoutesLayer,
            IvaoMapLayers.CreateFlightPlanRoutes(
                SelectedPilot is { } pilot ? [pilot.Pilot] : [],
                _palette ?? DefaultPalette,
                _navDataResolver?.Invoke(),
                SelectedPilot?.MapPosition));
    }

    private void RebuildLiveTrailLayer()
    {
        _selectedRoutePosition = SelectedPilot?.MapPosition;
        ReplaceLayer(ref _liveTrailLayer,
            IvaoMapLayers.CreateLiveTrail(SelectedPilot?.Pilot, _selectedRoutePosition));
    }

    /// <summary>
    /// Paints the trail of the selected aircraft, or an empty layer when nothing
    /// is selected. A station has no trail: the recorded track of a session is the
    /// path an aircraft flew, so selecting a controller only highlights its sector
    /// or badge. The estimated route is only drawn once the recorded track is known
    /// to be unavailable, so the line never jumps from a guess to the recorded
    /// trail.
    /// </summary>
    private void RebuildRouteLayer()
    {
        var layer = SelectedPilot is { } pilot
            ? IvaoMapLayers.CreateRoute(
                pilot.Pilot,
                _palette ?? DefaultPalette,
                _selectionTracks,
                estimateWhenMissing: !_selectionTracksPending,
                showAirportLabels: false)
            : EmptyRouteLayer();

        ReplaceLayer(ref _routeLayer, layer);
        RebuildLiveTrailLayer();
    }

    /// <summary>
    /// Gives a layer the content of a freshly built one. The layer itself stays:
    /// taking it out of the map and putting another in has the map recompute its
    /// resolutions and extent twice, and a renderer on another thread could draw a
    /// frame in between that lacks the layer. The layers of the map all outlive the
    /// content they show.
    /// </summary>
    private void ReplaceLayer(ref MemoryLayer? field, MemoryLayer replacement)
    {
        if (field is null)
        {
            field = replacement;
            return;
        }

        if (!ReferenceEquals(field.Style, replacement.Style))
        {
            field.Style = replacement.Style;
        }

        field.Features = replacement.Features;
        Map?.RefreshGraphics();
    }

    private static void SetLayerEnabled(ILayer? layer, bool enabled)
    {
        if (layer is not null)
        {
            layer.Enabled = enabled;
        }
    }

    private void ApplyAircraftColors(MapPalette palette)
    {
        if (_aircraftLayer is { } layer)
        {
            layer.LabelColor = WorldMap.ToArgb(palette.Text ?? palette.Label);
            layer.ShadowColor = WorldMap.ToArgb(palette.Shadow ?? DefaultAircraftShadow);
        }
    }

    private static MemoryLayer EmptyRouteLayer() => new(IvaoMapLayers.RouteLayerName)
    {
        Features = [],
        Style = null,
        SortFeatures = WorldMap.KeepOrder
    };

    private static IvaoAtc EmptyAtc() => new() { Id = -1, Callsign = string.Empty };

    private static readonly MapPalette DefaultPalette = new(
        "#0E2233", "#1B303F", "#2C4557", "#AEC4D4", "#4FB3FF", true);
}
