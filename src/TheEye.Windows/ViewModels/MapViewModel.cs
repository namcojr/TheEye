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
        _atcStationsLayer = IvaoMapLayers.CreateAtcStations([], palette, _stationZoom);
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
        ReplaceLayer(ref _atcStationsLayer, IvaoMapLayers.CreateAtcStations(Atc.Select(item => item.Atc), palette, _stationZoom));
        ReplaceLayer(ref _selectedAtcLayer, SelectedAtc is { } selected
            ? IvaoMapLayers.CreateSelectedAtc(selected.Atc, palette)
            : IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), palette));
        ReplaceLayer(ref _routeLayer, SelectedPilot is { } pilot
            ? IvaoMapLayers.CreateRoute(pilot.Pilot, palette)
            : new MemoryLayer(IvaoMapLayers.RouteLayerName) { Features = [], Style = null });

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
        ReplaceLayer(ref _atcStationsLayer, IvaoMapLayers.CreateAtcStations(atc, _palette, _stationZoom));
        SetLayerEnabled(_atcRegionsLayer, _showAtc);
        SetLayerEnabled(_atcLabelsLayer, _showAtc);
        SetLayerEnabled(_atcStationsLayer, _showAtc);

        _regionIndex = AtcRegionIndex.Build(atc);
        UpdatePilotPositions();
        Map.Refresh();
    }

    public void SelectPilot(PilotItem? pilot)
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
        if (pilot is not null)
        {
            pilot.IsSelected = true;
            ReplaceLayer(ref _routeLayer, _palette is null
                ? new MemoryLayer(IvaoMapLayers.RouteLayerName) { Features = [], Style = null }
                : IvaoMapLayers.CreateRoute(pilot.Pilot, _palette));
        }
        else
        {
            ReplaceLayer(ref _routeLayer, new MemoryLayer(IvaoMapLayers.RouteLayerName) { Features = [], Style = null });
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAtc(AtcItem? atc)
    {
        if (ReferenceEquals(SelectedAtc, atc))
        {
            return;
        }

        if (SelectedPilot is { } previousPilot)
        {
            previousPilot.IsSelected = false;
            SelectedPilot = null;
            ReplaceLayer(ref _routeLayer, new MemoryLayer(IvaoMapLayers.RouteLayerName) { Features = [], Style = null });
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
        }
        else
        {
            ReplaceLayer(ref _selectedAtcLayer, IvaoMapLayers.CreateSelectedAtc(EmptyAtc(), _palette ?? DefaultPalette));
        }

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

    /// <summary>Picks the pilot or ATC sector under a click on the map.</summary>
    public void SelectAt(MPoint worldPosition, ScreenPosition screenPosition)
    {
        if (HitTestPilot(screenPosition) is { } pilot)
        {
            SelectPilot(pilot);
            return;
        }

        var atc = _regionIndex?.FindAt(worldPosition);
        if (atc is not null && _atcById.TryGetValue(atc.Id, out var item))
        {
            SelectAtc(item);
            return;
        }

        ClearSelection();
    }

    public PilotItem? HitTestPilot(ScreenPosition position)
    {
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

    public void CenterOnPilot(PilotItem pilot)
    {
        if (Map is null || pilot.Pilot.LastTrack?.Position is not { } position)
        {
            return;
        }

        Map.Navigator.CenterOn(WorldMap.Project(position), 250);
    }

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
        UpdateStationZoom();
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
        ReplaceLayer(ref _atcStationsLayer, IvaoMapLayers.CreateAtcStations(_atc, _palette, _stationZoom));
        SetLayerEnabled(_atcStationsLayer, _showAtc);
        map.Refresh();
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
