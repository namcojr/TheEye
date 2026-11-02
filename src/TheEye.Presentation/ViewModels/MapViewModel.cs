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

namespace TheEye.Presentation.ViewModels;

/// <summary>
/// Owns the Mapsui map, the theme palettes handed to it, and the live pilot and
/// ATC collections. Pilot markers are UI elements (an ItemsControl layered over
/// the map) so they get the full themed marker styling and a native tooltip,
/// while ATC sectors are rendered natively by Mapsui.
/// </summary>
public sealed class MapViewModel : ObservableObject, IDisposable
{
    /// <summary>Width of the world in EPSG:3857 metres, used to detect routes that wrap the antimeridian.</summary>
    private const double WorldWidth = 40075016.68;

    /// <summary>
    /// Smallest area framed around a selected flight, so a short hop does not
    /// zoom all the way down to a single apron.
    /// </summary>
    private const double MinimumFrameSize = 40000;

    private readonly IAircraftIconCache _icons;
    private readonly IImageCache _images;
    private readonly IImagePipeline _pipeline;
    private readonly TimeProvider _clock;
    private readonly string? _basemapCacheDirectory;
    private readonly Func<NavigraphRouteResolver?>? _navDataResolver;
    private GeoCoordinate? _hoverRoutePosition;
    private readonly Dictionary<long, PilotItem> _pilotsById = [];
    private readonly Dictionary<long, AtcItem> _atcById = [];
    private IReadOnlySet<long> _supervisorUserIds = new HashSet<long>();

    private Map? _map;
    private MapPalette? _palette;
    private AtcRegionIndex? _regionIndex;
    private ILayer? _atcRegionsLayer;
    private ILayer? _atcLabelsLayer;
    private ILayer? _atcStationsLayer;
    private ILayer? _routeLayer;
    private ILayer? _flightPlanRoutesLayer;
    private ILayer? _hoverRouteLayer;
    private PilotItem? _hoveredPilot;
    private ILayer? _selectedAtcLayer;
    private ILayer? _airportLayer;
    private PilotItem? _selectedPilot;
    private AtcItem? _selectedAtc;
    private AtcItem? _hoveredAtc;
    private double _atcHoverX;
    private double _atcHoverY;
    private string? _mapTooltipText;
    private double _mapTooltipX;
    private double _mapTooltipY;
    private bool _showPilots = true;
    private bool _showAtc = true;
    private IReadOnlyList<IvaoAtc> _atc = [];
    private IReadOnlyList<IvaoPilotTrack> _selectionTracks = [];
    private bool _selectionTracksPending;
    private IReadOnlyList<IvaoMapLayers.AtcStationPlacement> _stationPlacements = [];
    private readonly List<AtcStationHitTarget> _stationTargets = [];
    private readonly List<(AirportRecord Airport, double X, double Y)> _trafficAirportTargets = [];
    private double _stationZoom = 1;
    private string _airportsKey = string.Empty;
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
        }
    }

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
        }
    }

    /// <summary>The colour of the artwork for the basemap and theme currently drawn.</summary>
    private string AircraftIconColor => _palette is { } palette
        ? AircraftMarkerStyles.IconColor(palette, _mapStyle)
        : AircraftMarkerStyles.LightIcon;

    /// <summary>
    /// Repaints every pin after the basemap or the theme changed. The pins are
    /// UI elements, so the colour has to be handed to each of them; new traffic
    /// picks it up when it is created.
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
            if (SetProperty(ref _showPilots, value) && !value)
            {
                SetHoveredPilot(null);
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

    public void Initialize(MapPalette palette, GeoCoordinate center, double resolution)
    {
        _palette = palette;
        var map = WorldMap.Create(palette, center, resolution);
        _stationZoom = AtcMarkerStyles.ZoomScale(map.Navigator.Viewport.Resolution);

        _atcRegionsLayer = IvaoMapLayers.CreateAtcRegions([], palette);
        _atcLabelsLayer = IvaoMapLayers.CreateAtcLabels([], palette);
        _atcStationsLayer = CreateStationLayer([]);
        _routeLayer = new MemoryLayer(IvaoMapLayers.RouteLayerName) { Features = [], Style = null };
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
        map.Layers.Add(_flightPlanRoutesLayer);
        map.Layers.Add(_hoverRouteLayer);
        map.Layers.Add(_selectedAtcLayer);

        SetLayerEnabled(_atcRegionsLayer, _showAtc);
        SetLayerEnabled(_atcLabelsLayer, _showAtc);
        SetLayerEnabled(_atcStationsLayer, _showAtc);

        // A saved tile style is restored with the view, so a restart returns to
        // the basemap the user left instead of the offline map.
        MapBasemap.Apply(map, _mapStyle, _basemapCacheDirectory);

        map.Navigator.ViewportChanged += OnViewportChanged;
        Map = map;
        UpdateAircraftIconColors();
        UpdateStationTargets();
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
            ? IvaoMapLayers.CreateSelectedAtc(selected.Atc, palette, _stationZoom)
            : IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), palette, _stationZoom));

        // Repaints the route with the new palette colours; the recorded track of
        // the selection is kept, so a theme switch does not drop the trail.
        RebuildRouteLayer();
        RebuildFlightPlanRoutesLayer();
        RebuildHoverRouteLayer();

        // The airport pavement derives its colour from the palette, so it is
        // rebuilt even when the viewport is looking at the same airports.
        _airportsKey = string.Empty;
        UpdateAirports();

        SetLayerEnabled(_atcRegionsLayer, _showAtc);
        SetLayerEnabled(_atcLabelsLayer, _showAtc);
        SetLayerEnabled(_atcStationsLayer, _showAtc);

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
        ReplaceLayer(ref _atcRegionsLayer, IvaoMapLayers.CreateAtcRegions(atc, _palette));
        ReplaceLayer(ref _atcLabelsLayer, IvaoMapLayers.CreateAtcLabels(atc, _palette));
        ReplaceLayer(ref _atcStationsLayer, CreateStationLayer(atc));
        SetLayerEnabled(_atcRegionsLayer, _showAtc);
        SetLayerEnabled(_atcLabelsLayer, _showAtc);
        SetLayerEnabled(_atcStationsLayer, _showAtc);

        _regionIndex = AtcRegionIndex.Build(atc);
        if (HoveredAtc is { } hovered && !_atcById.ContainsKey(hovered.Id))
        {
            SetHoveredAtc(null);
        }
        UpdatePilotPositions();
        RebuildFlightPlanRoutesLayer();
        RebuildHoverRouteLayer();
        _airportsKey = string.Empty;
        UpdateAirports();
        Map.Refresh();
    }

    public void SetHoveredAtc(ScreenPosition? position)
    {
        if (position is not { } screen || Map is not { } map)
        {
            HoveredAtc = null;
            MapTooltipText = null;
            return;
        }

        var viewport = map.Navigator.Viewport;
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
            var dx = target.X - screen.X;
            var dy = target.Y - screen.Y;
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
        var station = AtcStationHitTest.Find(_stationTargets, screen.X, screen.Y,
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

        // The pin under the pointer grows by two pixels; the one it left returns
        // to its own size. Both markers redraw from their own property change.
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
        Map?.Refresh();
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

        SelectedPilot = pilot;
        _selectionTracks = [];
        _selectionTracksPending = pilot is not null;
        if (pilot is not null)
        {
            pilot.IsSelected = true;
        }

        RebuildRouteLayer();

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

        SelectedAtc = atc;
        _selectionTracks = [];

        // Only aircraft are traced, so a station never waits for a track.
        _selectionTracksPending = false;
        if (atc is not null)
        {
            atc.IsSelected = true;
            ReplaceLayer(ref _selectedAtcLayer, IvaoMapLayers.CreateSelectedAtc(atc.Atc, _palette ?? DefaultPalette, _stationZoom));

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
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        SelectPilot(null);
        if (SelectedAtc is not null)
        {
            SelectAtc(null);
        }
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
        Map?.Refresh();
    }

    /// <summary>
    /// Picks the pilot, station badge or ATC sector under a click on the map.
    /// Centring follows <see cref="CenterOnMapClick"/> because the map is where
    /// the user is already looking.
    /// </summary>
    public void SelectAt(MPoint worldPosition, ScreenPosition screenPosition)
    {
        if (HitTestPilot(screenPosition) is { } pilot)
        {
            SelectPilot(pilot, CenterOnMapClick);
            return;
        }

        var atc = _showAtc
            ? AtcStationHitTest.Find(_stationTargets, screenPosition.X, screenPosition.Y,
                SelectedAtc?.Id, _regionIndex?.FindAllAt(worldPosition))
            : null;
        if (atc is not null && _atcById.TryGetValue(atc.Id, out var item))
        {
            SelectAtc(item, CenterOnMapClick);
            return;
        }

        ClearSelection();
    }

    public PilotItem? HitTestPilot(ScreenPosition position)
    {
        // Hidden markers must not swallow clicks, otherwise a parked aircraft
        // sitting on an airport blocks the tower and ground badges under it.
        if (!_showPilots)
        {
            return null;
        }

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
    /// Returns the station badge drawn under a screen position, if any. A second
    /// click on the same airport steps on to the next stacked position, so the
    /// rings drawn on top of each other can all be reached.
    /// </summary>
    public AtcItem? HitTestStation(ScreenPosition position)
    {
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

        var aircraft = WorldMap.Project(position);
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

        var start = WorldMap.Project(startCoordinate);
        var end = WorldMap.Project(endCoordinate);
        var minX = Math.Min(aircraft.X, Math.Min(start.X, end.X));
        var maxX = Math.Max(aircraft.X, Math.Max(start.X, end.X));
        var minY = Math.Min(aircraft.Y, Math.Min(start.Y, end.Y));
        var maxY = Math.Max(aircraft.Y, Math.Max(start.Y, end.Y));

        // A route that crosses the antimeridian is drawn as two separate lines,
        // so its projected box would cover the whole world. Centring the aircraft
        // is far more useful than zooming out to a meaningless world view.
        if (maxX - minX > WorldWidth / 2)
        {
            map.Navigator.CenterOn(aircraft, 250);
            return;
        }

        // Keep a little breathing room so the aircraft and both airports never
        // end up glued to the edge of the map.
        var paddingX = Math.Max((maxX - minX) * 0.1, MinimumFrameSize / 2);
        var paddingY = Math.Max((maxY - minY) * 0.1, MinimumFrameSize / 2);

        map.Navigator.ZoomToBox(
            new MRect(minX - paddingX, minY - paddingY, maxX + paddingX, maxY + paddingY),
            MBoxFit.Fit,
            350);
    }

    /// <summary>Brings an ATC position into view without zooming out from a closer view.</summary>
    public void CenterOnAtc(AtcItem atc)
    {
        if (Map is null || atc.Atc.Position is not { } position)
        {
            return;
        }

        Map.Navigator.CenterOnAndZoomTo(WorldMap.Project(position), Math.Min(Map.Navigator.Viewport.Resolution, 8000), 300);
    }

    public void ZoomIn() => Map?.Navigator.ZoomIn(250);

    public void ZoomOut() => Map?.Navigator.ZoomOut(250);

    public void ResetView(GeoCoordinate center, double resolution) =>
        Map?.Navigator.CenterOnAndZoomTo(WorldMap.Project(center), resolution, 350);

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

    public void AdvanceAircraft()
    {
        if (_disposed || !_showPilots) return;
        UpdatePilotPositions();
        if (_hoveredPilot is { } pilot && pilot.MapPosition != _hoverRoutePosition)
        {
            RebuildHoverRouteLayer();
            Map?.Refresh();
        }
    }

    private void UpdatePilotPositions()
    {
        if (Map is not { } map)
        {
            return;
        }

        var viewport = map.Navigator.Viewport;
        var width = viewport.Width;
        var height = viewport.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        foreach (var item in Pilots)
        {
            if (item.MapPosition is not { } position)
            {
                item.SetScreenPosition(0, 0, false);
                continue;
            }

            var screen = viewport.WorldToScreen(WorldMap.Project(position));
            var size = item.MarkerSize;
            var visible = screen.X >= -size && screen.Y >= -size && screen.X <= width + size && screen.Y <= height + size;
            item.SetScreenPosition(screen.X - size / 2d, screen.Y - size / 2d, visible);
        }
    }

    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e)
    {
        SetHoveredAtc(null);
        UpdatePilotPositions();
        UpdateStationTargets();
        UpdateStationZoom();
        UpdateAirports();
    }

    /// <summary>
    /// Builds the station layer and keeps the placements used for hit testing in
    /// sync with the badges that are actually drawn. The badges of the members
    /// connected with supervisor rights carry a ring, so the layer is rebuilt with
    /// the current set on every change.
    /// </summary>
    private MemoryLayer CreateStationLayer(IEnumerable<IvaoAtc> atc)
    {
        _stationPlacements = IvaoMapLayers.CreateAtcStationPlacements(atc, _stationZoom);
        UpdateStationTargets();
        return IvaoMapLayers.CreateAtcStations(
            _stationPlacements,
            _palette ?? DefaultPalette,
            _stationZoom,
            _supervisorUserIds);
    }

    /// <summary>
    /// Projects every badge onto the current viewport so a click can find it
    /// where it is drawn. The badges of one airport share the airport
    /// coordinate, so they are all tested at the same screen point.
    /// </summary>
    private void UpdateStationTargets()
    {
        _stationTargets.Clear();
        if (Map is not { } map)
        {
            return;
        }

        var viewport = map.Navigator.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        foreach (var placement in _stationPlacements)
        {
            var screen = viewport.WorldToScreen(placement.Point);
            _stationTargets.Add(new AtcStationHitTarget(
                placement.Station,
                screen.X,
                screen.Y,
                AtcMarkerStyles.HitRadius(AtcMarkerStyles.For(
                    placement.Station.Facility, placement.Station.Callsign,
                    AtcMarkerStyles.ZoomScale(viewport.Resolution))),
                AtcMarkerStyles.SortOrder(placement.Station.Facility, placement.Station.Callsign)));
        }
    }

    /// <summary>
    /// Keeps the scale used to build placements in sync. Glyphs and label offsets
    /// resolve the current resolution at render time, without rebuilding layers.
    /// </summary>
    private void UpdateStationZoom()
    {
        if (Map is not { } map || _palette is null)
        {
            return;
        }

        var zoom = AtcMarkerStyles.ZoomScale(map.Navigator.Viewport.Resolution);
        if (Math.Abs(zoom - _stationZoom) < 0.000001)
        {
            return;
        }

        _stationZoom = zoom;
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
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

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
        UpdateTrafficAirportTargets(trafficAirports);
        var key = string.Join('|', trafficAirports.Select(airport => airport.Icao));
        if (key == _airportsKey)
        {
            return;
        }

        _airportsKey = key;
        ReplaceLayer(ref _airportLayer, IvaoMapLayers.CreateAirports(
            [], _palette, viewport.Resolution, showRunways: false, trafficAirports: trafficAirports));
        map.Refresh();
    }

    private void UpdateTrafficAirportTargets(IReadOnlyList<AirportRecord> airports)
    {
        _trafficAirportTargets.Clear();
        if (Map is not { } map)
        {
            return;
        }

        var viewport = map.Navigator.Viewport;
        foreach (var airport in airports)
        {
            var screen = viewport.WorldToScreen(WorldMap.Project(airport.Position));
            _trafficAirportTargets.Add((airport, screen.X, screen.Y));
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
                _navDataResolver?.Invoke(),
                showAirportLabels: false));
    }

    private void RebuildFlightPlanRoutesLayer()
    {
        ReplaceLayer(ref _flightPlanRoutesLayer,
            IvaoMapLayers.CreateFlightPlanRoutes(
                Pilots.Select(item => item.Pilot),
                _palette ?? DefaultPalette,
                _navDataResolver?.Invoke()));
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
        ILayer layer = SelectedPilot is { } pilot
            ? IvaoMapLayers.CreateRoute(
                pilot.Pilot,
                _palette ?? DefaultPalette,
                _selectionTracks,
                estimateWhenMissing: !_selectionTracksPending,
                showAirportLabels: false)
            : new MemoryLayer(IvaoMapLayers.RouteLayerName) { Features = [], Style = null };

        ReplaceLayer(ref _routeLayer, layer);
    }

    private void ReplaceLayer(ref ILayer? field, ILayer replacement)
    {
        if (Map is not { } map)
        {
            field = replacement;
            return;
        }

        if (field is not null)
        {
            var index = 0;
            var found = false;
            foreach (var layer in map.Layers)
            {
                if (ReferenceEquals(layer, field))
                {
                    found = true;
                    break;
                }

                index++;
            }

            if (found)
            {
                map.Layers.Remove(field);
                map.Layers.Insert(index, replacement);
            }
            else
            {
                map.Layers.Add(replacement);
            }
        }
        else
        {
            map.Layers.Add(replacement);
        }

        field = replacement;
    }

    private static void SetLayerEnabled(ILayer? layer, bool enabled)
    {
        if (layer is not null)
        {
            layer.Enabled = enabled;
        }
    }

    private static IvaoAtc EmptyAtc() => new() { Id = -1, Callsign = string.Empty };

    private static readonly MapPalette DefaultPalette = new(
        "#0E2233", "#1B303F", "#2C4557", "#AEC4D4", "#4FB3FF", true);
}
