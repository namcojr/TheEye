using System.Collections.ObjectModel;
using System.Windows;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Services;
using TheEye.Windows.Services;

namespace TheEye.Windows.ViewModels;

/// <summary>
/// Owns the Mapsui map, the theme palettes handed to it, and the live pilot and
/// ATC collections. Pilot markers are WPF elements (an ItemsControl layered over
/// the map) so they get the full themed <c>PilotMarkerButton</c> styling and a
/// native tooltip, while ATC sectors are rendered natively by Mapsui.
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

    private readonly AircraftIconCache _icons = new();
    private readonly Dictionary<long, PilotItem> _pilotsById = [];
    private readonly Dictionary<long, AtcItem> _atcById = [];

    private Map? _map;
    private MapPalette? _palette;
    private AtcRegionIndex? _regionIndex;
    private ILayer? _atcRegionsLayer;
    private ILayer? _atcLabelsLayer;
    private ILayer? _atcStationsLayer;
    private ILayer? _routeLayer;
    private ILayer? _selectedAtcLayer;
    private PilotItem? _selectedPilot;
    private AtcItem? _selectedAtc;
    private bool _showPilots = true;
    private bool _showAtc = true;
    private IReadOnlyList<IvaoAtc> _atc = [];
    private IReadOnlyList<IvaoPilotTrack> _selectionTracks = [];
    private IReadOnlyList<IvaoMapLayers.AtcStationPlacement> _stationPlacements = [];
    private readonly List<AtcStationHitTarget> _stationTargets = [];
    private double _stationZoom = 1;
    private bool _disposed;

    public MapViewModel()
    {
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

    public bool ShowPilots
    {
        get => _showPilots;
        set
        {
            if (SetProperty(ref _showPilots, value))
            {
                OnPropertyChanged(nameof(PilotsVisibility));
            }
        }
    }

    public Visibility PilotsVisibility => ShowPilots ? Visibility.Visible : Visibility.Collapsed;

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
        _selectedAtcLayer = IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), palette);

        map.Layers.Add(_atcRegionsLayer);
        map.Layers.Add(_atcLabelsLayer);
        map.Layers.Add(_atcStationsLayer);
        map.Layers.Add(_routeLayer);
        map.Layers.Add(_selectedAtcLayer);

        SetLayerEnabled(_atcRegionsLayer, _showAtc);
        SetLayerEnabled(_atcLabelsLayer, _showAtc);
        SetLayerEnabled(_atcStationsLayer, _showAtc);

        map.Navigator.ViewportChanged += OnViewportChanged;
        Map = map;
        UpdateStationTargets();
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
            ? IvaoMapLayers.CreateSelectedAtc(selected.Atc, palette)
            : IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), palette));
        ReplaceLayer(ref _routeLayer, SelectedPilot is { } pilot
            ? IvaoMapLayers.CreateRoute(pilot.Pilot, palette, _selectionTracks)
            : new MemoryLayer(IvaoMapLayers.RouteLayerName) { Features = [], Style = null });

        RebuildRouteLayer();

        SetLayerEnabled(_atcRegionsLayer, _showAtc);
        SetLayerEnabled(_atcLabelsLayer, _showAtc);
        SetLayerEnabled(_atcStationsLayer, _showAtc);
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

        var atc = snapshot.Atc;
        _atc = atc;
        ReplaceLayer(ref _atcRegionsLayer, IvaoMapLayers.CreateAtcRegions(atc, _palette));
        ReplaceLayer(ref _atcLabelsLayer, IvaoMapLayers.CreateAtcLabels(atc, _palette));
        ReplaceLayer(ref _atcStationsLayer, CreateStationLayer(atc));
        SetLayerEnabled(_atcRegionsLayer, _showAtc);
        SetLayerEnabled(_atcLabelsLayer, _showAtc);
        SetLayerEnabled(_atcStationsLayer, _showAtc);

        _regionIndex = AtcRegionIndex.Build(atc);
        UpdatePilotPositions();
        Map.Refresh();
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
            _selectionTracks = [];
        }

        if (SelectedAtc is { } previous)
        {
            previous.IsSelected = false;
        }

        SelectedAtc = atc;
        if (atc is not null)
        {
            atc.IsSelected = true;
            ReplaceLayer(ref _selectedAtcLayer, IvaoMapLayers.CreateSelectedAtc(atc.Atc, _palette ?? DefaultPalette));

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
    /// Hands the recorded track of the current selection to the map. The route
    /// layer is redrawn straight away and again on every snapshot or theme
    /// change, so a slow request never leaves the map without a route: until the
    /// track arrives the flight shows the part of its route it has already flown.
    /// </summary>
    public void SetSelectionTracks(IReadOnlyList<IvaoPilotTrack>? tracks)
    {
        _selectionTracks = tracks ?? [];
        if (SelectedPilot is null && SelectedAtc is null)
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

        // Tower, ground and delivery badges are drawn inside the much larger
        // control sector around their airport, so the glyphs win over the polygon.
        if (HitTestStation(screenPosition) is { } station)
        {
            SelectAtc(station, CenterOnMapClick);
            return;
        }

        var atc = _regionIndex?.FindAt(worldPosition);
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

            var dx = pilot.X + 13 - position.X;
            var dy = pilot.Y + 13 - position.Y;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared <= bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                best = pilot;
            }
        }

        return best;
    }

    /// <summary>Returns the station badge drawn under a screen position, if any.</summary>
    public AtcItem? HitTestStation(ScreenPosition position)
    {
        if (!_showAtc
            || AtcStationHitTest.Find(_stationTargets, position.X, position.Y) is not { } station)
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
        if (Map is not { } map || pilot.Pilot.LastTrack?.Position is not { } position)
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
            return (new GeoCoordinate(45, 10), 25000);
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
        if (Map is not null)
        {
            Map.Navigator.ViewportChanged -= OnViewportChanged;
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
                var item = new PilotItem(pilot, _icons);
                _pilotsById.Add(pilot.Id, item);
                Pilots.Add(item);
            }
        }

        foreach (var stale in Pilots.Where(item => !incoming.Contains(item.Id)).ToList())
        {
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
                var item = new AtcItem(station);
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
            if (item.Pilot.LastTrack?.Position is not { } position)
            {
                item.SetScreenPosition(0, 0, false);
                continue;
            }

            var screen = viewport.WorldToScreen(WorldMap.Project(position));
            var visible = screen.X >= -26 && screen.Y >= -26 && screen.X <= width + 26 && screen.Y <= height + 26;
            item.SetScreenPosition(screen.X - 13, screen.Y - 13, visible);
        }
    }

    private void OnViewportChanged(object? sender, ViewportChangedEventArgs e)
    {
        UpdatePilotPositions();
        UpdateStationTargets();
        UpdateStationZoom();
    }

    /// <summary>
    /// Builds the station layer and keeps the placements used for hit testing in
    /// sync with the badges that are actually drawn.
    /// </summary>
    private MemoryLayer CreateStationLayer(IEnumerable<IvaoAtc> atc)
    {
        _stationPlacements = IvaoMapLayers.CreateAtcStationPlacements(atc, _stationZoom);
        UpdateStationTargets();
        return IvaoMapLayers.CreateAtcStations(_stationPlacements, _palette ?? DefaultPalette, _stationZoom);
    }

    /// <summary>
    /// Projects every badge onto the current viewport so a click can find it
    /// where it is drawn, including the cluster fan-out offsets.
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
                screen.X + placement.OffsetX,
                screen.Y + placement.OffsetY,
                AtcMarkerStyles.HitRadius(placement.Marker),
                AtcMarkerStyles.SortOrder(placement.Station.Facility, placement.Station.Callsign)));
        }
    }

    /// <summary>
    /// Rebuilds the station layer when the zoom multiplier changes so the badges
    /// grow and shrink with the map instead of keeping one pixel size on every
    /// zoom level. <see cref="AtcMarkerStyles.ZoomScale"/> quantises the
    /// multiplier, so this only runs when the glyphs visibly change size.
    /// </summary>
    private void UpdateStationZoom()
    {
        if (Map is not { } map || _palette is null)
        {
            return;
        }

        var zoom = AtcMarkerStyles.ZoomScale(map.Navigator.Viewport.Resolution);
        if (Math.Abs(zoom - _stationZoom) < 0.001)
        {
            return;
        }

        _stationZoom = zoom;
        ReplaceLayer(ref _atcStationsLayer, CreateStationLayer(_atc));
        SetLayerEnabled(_atcStationsLayer, _showAtc);
        map.Refresh();
    }

    /// <summary>
    /// Paints the route of whatever is selected: the flown trail of an aircraft,
    /// the recorded track of a station, or an empty layer when nothing is
    /// selected.
    /// </summary>
    private void RebuildRouteLayer()
    {
        ILayer layer = SelectedPilot is { } pilot
            ? IvaoMapLayers.CreateRoute(pilot.Pilot, _palette ?? DefaultPalette, _selectionTracks)
            : SelectedAtc is { } atc
                ? IvaoMapLayers.CreateAtcRoute(atc.Atc, _selectionTracks)
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
