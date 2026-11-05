using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Services;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

public sealed class HoverRouteTests
{
    private static readonly MapPalette Palette = new(
        "#16324F", "#1B2A35", "#2F4858", "#6FE3F5", "#4CC9F0", true);

    [Fact]
    public void Hover_LeavesSelectionAndSelectedRouteUntouchedAndClearsOnLeave()
    {
        using var model = CreateModel();
        model.SelectPilot(model.Pilots[0], center: false);
        model.SetSelectionTracks([]);
        var selectedRoute = Layer(model, IvaoMapLayers.RouteLayerName);

        model.SetHoveredPilot(model.Pilots[1]);
        Assert.Equal(4, Hover(model).Features.Count());
        Assert.Same(model.Pilots[0], model.SelectedPilot);
        Assert.Same(selectedRoute, Layer(model, IvaoMapLayers.RouteLayerName));

        model.SetHoveredPilot(null);
        Assert.Empty(Hover(model).Features);
        Assert.Same(selectedRoute, Layer(model, IvaoMapLayers.RouteLayerName));
    }

    [Fact]
    public void Hover_MarksAirportsWithTheSameDotsAndLabelsAsSelection()
    {
        var pilot = Pilot(1);
        var selected = IvaoMapLayers.CreateRoute(pilot, Palette).Features.OfType<PointFeature>().ToArray();
        var hovered = IvaoMapLayers.CreateHoverRoute(pilot, Palette).Features.OfType<PointFeature>().ToArray();

        Assert.Equal(2, hovered.Length);
        for (var index = 0; index < selected.Length; index++)
        {
            Assert.Equal(selected[index].Point, hovered[index].Point);
            var expectedSymbol = selected[index].Styles.OfType<SymbolStyle>().Single();
            var symbol = hovered[index].Styles.OfType<SymbolStyle>().Single();
            Assert.Equal(expectedSymbol.SymbolType, symbol.SymbolType);
            Assert.Equal(expectedSymbol.SymbolScale, symbol.SymbolScale);
            Assert.Equal(expectedSymbol.Fill!.Color, symbol.Fill!.Color);
            Assert.Equal(expectedSymbol.Outline!.Color, symbol.Outline!.Color);
            Assert.Equal(expectedSymbol.Outline.Width, symbol.Outline.Width);
            var expectedLabel = selected[index].Styles.OfType<LabelStyle>().Single();
            var label = hovered[index].Styles.OfType<LabelStyle>().Single();
            Assert.Equal(expectedLabel.GetLabelText(selected[index]), label.GetLabelText(hovered[index]));
            Assert.Equal(expectedLabel.ForeColor, label.ForeColor);
        }
    }

    [Fact]
    public void MapViewModel_DrawsOnlySelectedRouteAndClearsItOnDeselection()
    {
        var pilot = Pilot(1);
        pilot.LastTrack!.Latitude = 52;
        pilot.LastTrack.Longitude = 5.5;
        pilot.FlightPlan!.Departure = new IvaoAirport { Icao = "EHAM", Latitude = 52, Longitude = 4.7 };
        pilot.FlightPlan.Arrival = new IvaoAirport { Icao = "EGLL", Latitude = 52, Longitude = 8 };
        pilot.FlightPlan.Route = "FIXA Q41 FIXB DCT FIXC";
        var resolver = new NavigraphRouteResolver(
            [
                ("FIXA", new GeoCoordinate(52, 5)),
                ("FIXB", new GeoCoordinate(52, 6)),
                ("FIXC", new GeoCoordinate(52, 7))
            ],
            [
                ("FIXA", "Q41", 10, new GeoCoordinate(52, 5)),
                ("HIDDEN", "Q41", 20, new GeoCoordinate(52, 5.5)),
                ("FIXB", "Q41", 30, new GeoCoordinate(52, 6))
            ]);

        var secondPilot = Pilot(2);
        secondPilot.LastTrack!.Latitude = 52;
        secondPilot.LastTrack.Longitude = 6.5;
        secondPilot.FlightPlan!.Departure = pilot.FlightPlan.Departure;
        secondPilot.FlightPlan.Arrival = pilot.FlightPlan.Arrival;
        secondPilot.FlightPlan.Route = pilot.FlightPlan.Route;

        using var model = new MapViewModel(new Images(), navDataResolver: () => resolver);
        model.Initialize(Palette, new GeoCoordinate(52, 5), resolution: 10000);
        model.UpdateTraffic(Snapshot([pilot, secondPilot]));

        Assert.Empty(Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features);

        model.SelectPilot(model.Pilots[0], center: false);
        var selectedLayer = Assert.IsType<MemoryLayer>(
            model.Map!.Layers.Single(mapLayer => mapLayer.Name == IvaoMapLayers.FlightPlanRoutesLayerName));
        Assert.Single(selectedLayer.Features.OfType<GeometryFeature>());
        Assert.Equal(3, selectedLayer.Features.OfType<PointFeature>()
            .Count(feature => feature["Waypoint"] is not null));
        model.SelectPilot(model.Pilots[1], center: false);
        Assert.Single(Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features.OfType<GeometryFeature>());
        model.ClearSelection();
        Assert.Empty(Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features);
        model.UpdateTraffic(Snapshot([pilot, secondPilot]));
        model.ApplyTheme(Palette);
        model.AdvanceAircraft();
        Assert.Empty(Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features);

        model.SelectPilot(model.Pilots[0], center: false);
        model.SelectAtc(new AtcItem(new IvaoAtc { Id = 3 }, new Images()), center: false);
        Assert.Empty(Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features);

        model.SelectPilot(model.Pilots[0], center: false);
        model.UpdateTraffic(Snapshot([]));
        Assert.Empty(Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Selection_FlightPlanArrivalDrawsWaypointsAndSurvivesSummaryRefresh(bool hasAirportCoordinates)
    {
        var resolver = new NavigraphRouteResolver(
            [
                ("FIXA", new GeoCoordinate(52, 5)),
                ("FIXB", new GeoCoordinate(52, 6)),
                ("FIXC", new GeoCoordinate(52, 7))
            ], []);
        var pilot = Summary();
        using var model = new MapViewModel(new Images(), new Clock(), navDataResolver: () => resolver);
        model.Initialize(Palette, new GeoCoordinate(52, 5), resolution: 10000);
        model.UpdateTraffic(Snapshot([pilot]));
        model.SelectPilot(model.Pilots[0], center: false);
        Assert.Empty(Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features);
        var selectedTrail = Layer(model, IvaoMapLayers.RouteLayerName).Features;

        model.SetSelectionFlightPlan(new SessionFlightPlanResult(
            new IvaoFlightPlan { SessionId = pilot.Id, Route = "FIXA DCT FIXB DCT FIXC" }, null));

        Assert.NotSame(selectedTrail, Layer(model, IvaoMapLayers.RouteLayerName).Features);
        AssertRoute();
        model.UpdateTraffic(Snapshot([Summary(longitude: 5.75)]));
        AssertRoute();
        Assert.Equal("FIXA DCT FIXB DCT FIXC", model.SelectedPilot!.Route);

        model.ApplyTheme(Palette with { Accent = "#FF8800" });
        AssertRoute();
        model.ClearSelection();
        Assert.Empty(Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features);
        model.SelectPilot(model.Pilots[0], center: false);
        AssertRoute();

        model.SetSelectionFlightPlan(new SessionFlightPlanResult(
            new IvaoFlightPlan { SessionId = pilot.Id, Route = "FIXB DCT FIXC" }, null));
        Assert.Equal(["FIXB", "FIXC"], Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName).Features
            .OfType<PointFeature>().Select(feature => (string)feature["Waypoint"]!));

        IvaoPilot Summary(double longitude = 5.5) => new()
        {
            Id = 1,
            Callsign = "TEST1",
            LastTrack = new IvaoLastTrack { Latitude = 52, Longitude = longitude },
            FlightPlan = new IvaoFlightPlan
            {
                DepartureId = "EHAM",
                ArrivalId = "EGLL",
                Departure = hasAirportCoordinates
                    ? new IvaoAirport { Icao = "EHAM", Latitude = 52, Longitude = 4.7 }
                    : null,
                Arrival = hasAirportCoordinates
                    ? new IvaoAirport { Icao = "EGLL", Latitude = 52, Longitude = 8 }
                    : null
            }
        };

        void AssertRoute()
        {
            var layer = Layer(model, IvaoMapLayers.FlightPlanRoutesLayerName);
            Assert.Equal(["FIXA", "FIXB", "FIXC"], layer.Features
                .OfType<PointFeature>().Select(feature => (string)feature["Waypoint"]!));
            var line = Assert.Single(layer.Features.OfType<GeometryFeature>());
            var pen = line.Styles.OfType<VectorStyle>().Single().Line!;
            Assert.Equal(PenStyle.Dash, pen.PenStyle);
            Assert.Equal(IvaoMapLayers.ForwardRouteWidth, pen.Width);
            var live = WorldMap.Project(model.Pilots[0].MapPosition!.Value);
            Assert.Equal(live.X, line.Geometry!.Coordinates[0].X, 5);
            Assert.Equal(live.Y, line.Geometry.Coordinates[0].Y, 5);
        }
    }

    [Fact]
    public void Selection_FlightPlanPreservesKnownAirportsAndFillsMissingCoordinates()
    {
        using var model = CreateModel();
        model.SelectPilot(model.Pilots[0], center: false);
        var departure = model.Pilots[0].Pilot.FlightPlan!.Departure;
        var arrival = model.Pilots[0].Pilot.FlightPlan!.Arrival;
        model.SetSelectionFlightPlan(new SessionFlightPlanResult(new IvaoFlightPlan
        {
            Route = "DCT",
            Departure = new IvaoAirport { Icao = "EHAM" },
            Arrival = new IvaoAirport { Icao = "EGLL" }
        }, null));

        Assert.Same(departure, model.Pilots[0].Pilot.FlightPlan!.Departure);
        Assert.Same(arrival, model.Pilots[0].Pilot.FlightPlan!.Arrival);

        model.Pilots[0].Pilot.FlightPlan = null;
        model.SetSelectionFlightPlan(new SessionFlightPlanResult(new IvaoFlightPlan
        {
            Departure = departure,
            Arrival = arrival
        }, null));

        Assert.Equal(2, Layer(model, IvaoMapLayers.RouteLayerName).Features.OfType<PointFeature>().Count());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Hover_OnlyDrawsKnownAirports(bool hasDeparture, bool hasArrival)
    {
        var pilot = Pilot(1);
        if (!hasDeparture)
        {
            pilot.FlightPlan!.Departure = null;
        }
        if (!hasArrival)
        {
            pilot.FlightPlan!.Arrival = null;
        }

        var features = IvaoMapLayers.CreateHoverRoute(pilot, Palette).Features.ToArray();
        var airportCount = (hasDeparture ? 1 : 0) + (hasArrival ? 1 : 0);
        Assert.Equal(airportCount * 2, features.Length);
        Assert.Equal(airportCount, features.OfType<PointFeature>().Count());
    }

    [Fact]
    public void Hover_WithoutAnAircraftPositionOrFlightPlanDrawsNothing()
    {
        Assert.Empty(IvaoMapLayers.CreateHoverRoute(null, Palette).Features);
        var pilot = Pilot(1);
        pilot.FlightPlan = null;
        Assert.Empty(IvaoMapLayers.CreateHoverRoute(pilot, Palette).Features);
        pilot = Pilot(1);
        pilot.LastTrack = null;
        Assert.Empty(IvaoMapLayers.CreateHoverRoute(pilot, Palette).Features);
    }

    [Fact]
    public void Selection_ClearsPreviewAndSuppressesHoverOfSelectedAircraft()
    {
        using var model = CreateModel();
        var pilot = model.Pilots[0];
        model.SetHoveredPilot(pilot);
        model.SelectPilot(pilot, center: false);
        Assert.Empty(Hover(model).Features);
        model.SetHoveredPilot(pilot);
        Assert.Empty(Hover(model).Features);
        Assert.NotEmpty(Layer(model, IvaoMapLayers.RouteLayerName).Features);
    }

    [Fact]
    public void HidingAircraft_ClearsAndDisablesHover()
    {
        using var model = CreateModel();
        model.SetHoveredPilot(model.Pilots[0]);
        model.ShowPilots = false;
        Assert.Empty(Hover(model).Features);
        model.SetHoveredPilot(model.Pilots[0]);
        Assert.Empty(Hover(model).Features);
        model.ShowPilots = true;
        Assert.Empty(Hover(model).Features);
    }

    [Fact]
    public void Hover_FollowsTrafficAndThemeChangesAndClearsWhenPilotDisconnects()
    {
        var clock = new Clock();
        using var model = CreateModel(clock);
        model.SetHoveredPilot(model.Pilots[0]);
        var initial = Hover(model).Features;
        model.UpdateTraffic(Snapshot([Pilot(1, longitude: 5)]));
        clock.Advance();
        model.AdvanceAircraft();
        Assert.NotSame(initial, Hover(model).Features);
        var updated = Hover(model).Features.First().Extent!;
        var position = WorldMap.Project(new GeoCoordinate(52.5, 5));
        // The plan folds each leg into one extent, so the projected coordinate can
        // land a floating point step outside it.
        Assert.InRange(position.X, updated.MinX - 1e-6, updated.MaxX + 1e-6);

        var theme = Palette with { Accent = "#FF8800" };
        model.ApplyTheme(theme);
        Assert.All(Hover(model).Features.OfType<Mapsui.Nts.GeometryFeature>(), feature =>
            Assert.Equal(WorldMap.Parse(theme.Accent),
                feature.Styles.OfType<VectorStyle>().Single().Line!.Color));
        Assert.All(Hover(model).Features.OfType<PointFeature>(), feature =>
            Assert.Equal(WorldMap.WithAlpha(WorldMap.Parse(theme.Accent), IvaoMapLayers.RouteAirportAlpha),
                feature.Styles.OfType<SymbolStyle>().Single().Fill!.Color));

        model.UpdateTraffic(Snapshot([]));
        Assert.Empty(Hover(model).Features);
    }

    private static MapViewModel CreateModel(TimeProvider? clock = null)
    {
        var model = new MapViewModel(new Images(), clock);
        model.Initialize(Palette, new GeoCoordinate(50, 0), 10000);
        model.UpdateTraffic(Snapshot([Pilot(1), Pilot(2)]));
        return model;
    }

    private static MemoryLayer Hover(MapViewModel model) => Layer(model, IvaoMapLayers.HoverRouteLayerName);

    private static MemoryLayer Layer(MapViewModel model, string name) =>
        Assert.IsType<MemoryLayer>(model.Map!.Layers.Single(layer => layer.Name == name));

    private static TrackerSnapshot Snapshot(IReadOnlyList<IvaoPilot> pilots) =>
        new(pilots, [], [], new IvaoConnectionStats(), DateTimeOffset.UtcNow, []);

    private static IvaoPilot Pilot(long id, double longitude = 4.5) => new()
    {
        Id = id,
        Callsign = $"TEST{id}",
        LastTrack = new IvaoLastTrack { Latitude = 52.5, Longitude = longitude },
        FlightPlan = new IvaoFlightPlan
        {
            Departure = new IvaoAirport { Icao = "EHAM", Latitude = 52.3105, Longitude = 4.7683 },
            Arrival = new IvaoAirport { Icao = "EGLL", Latitude = 51.4706, Longitude = -0.4619 }
        }
    };

    private sealed class Images : IImagePipeline, IAircraftIconCache, IImageCache
    {
        public IAircraftIconCache AircraftIcons => this;
        public IImageCache SessionImages => this;
        public object Get(AircraftGlyph glyph) => glyph;
        public object? TryGet(string? url) => null;
        public Task<object?> GetAsync(string? url, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);
        public void Dispose() { }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance() => _ticks += TimeSpan.TicksPerSecond;
    }
}
