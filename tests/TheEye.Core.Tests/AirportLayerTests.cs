using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Mapsui.Manipulations;
using Mapsui.Extensions;
using NetTopologySuite.Geometries;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Reference;
using TheEye.Core.Services;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

public sealed class AirportLayerTests
{
    private static readonly MapPalette Dark = new(
        Water: "#16324F",
        Land: "#1B2A35",
        Border: "#2F4858",
        Label: "#6FE3F5",
        Accent: "#4CC9F0",
        IsDark: true);

    private static readonly MapPalette Light = Dark with { Land = "#F2F0EA", Label = "#2B2B2B", IsDark = false };

    [Fact]
    public void ShowsRunways_WaitsForTheRunwayZoom()
    {
        Assert.True(IvaoMapLayers.ShowsRunways(IvaoMapLayers.RunwayResolution));
        Assert.True(IvaoMapLayers.ShowsRunways(IvaoMapLayers.RunwayResolution / 2));
        Assert.False(IvaoMapLayers.ShowsRunways(IvaoMapLayers.RunwayResolution * 2));
        Assert.False(IvaoMapLayers.ShowsRunways(WorldMap.MaxResolution));
    }

    [Fact]
    public void CreateAirports_DrawsAStripPerRunwayAndOneCodePerAirport()
    {
        var layer = IvaoMapLayers.CreateAirports([Heathrow()], Dark, resolution: 5);

        var strips = layer.Features.OfType<GeometryFeature>().ToList();
        var code = Assert.Single(layer.Features.OfType<PointFeature>());

        Assert.Equal(IvaoMapLayers.AirportsLayerName, layer.Name);
        Assert.Equal(2, strips.Count);
        Assert.All(strips, strip =>
        {
            Assert.IsType<Polygon>(strip.Geometry);
            Assert.Equal("EGLL", strip["Icao"]);
            Assert.Equal(
                WorldMap.Mix(WorldMap.Parse(Dark.Land), WorldMap.Parse(Dark.Label), 0.5),
                FillColor(strip));
            Assert.Equal(WorldMap.Parse(Dark.Border), OutlineColor(strip));
        });

        var label = code.Styles.OfType<LabelStyle>().Single();
        Assert.Equal("Icao", label.LabelColumn);
        Assert.Equal(WorldMap.Parse(Dark.Label), label.ForeColor);
        Assert.Equal("EGLL", code["Icao"]);
    }

    [Fact]
    public void CreateAirports_WidensANarrowRunwaySoItStaysVisible()
    {
        // At a resolution of 100 m per pixel the smallest drawn runway is 160 m,
        // so a 45 m strip is drawn four times wider than it is published.
        var narrow = new AirportRecord(
            "TEST",
            new GeoCoordinate(0, 0.005),
            "Test Field",
            [new AirportRunway(new GeoCoordinate(0, 0), new GeoCoordinate(0, 0.01), 45)]);

        var strip = Assert.Single(
            IvaoMapLayers.CreateAirports([narrow], Dark, resolution: 100).Features.OfType<GeometryFeature>());

        var corners = ((Polygon)strip.Geometry!).ExteriorRing.Coordinates;
        Assert.Equal(160, corners[0].Distance(corners[3]), 3);
    }

    [Fact]
    public void CreateAirports_KeepsThePublishedWidthWhereItIsAlreadyVisible()
    {
        var wide = new AirportRecord(
            "TEST",
            new GeoCoordinate(0, 0.005),
            "Test Field",
            [new AirportRunway(new GeoCoordinate(0, 0), new GeoCoordinate(0, 0.01), 60)]);

        var strip = Assert.Single(
            IvaoMapLayers.CreateAirports([wide], Dark, resolution: 5).Features.OfType<GeometryFeature>());

        var corners = ((Polygon)strip.Geometry!).ExteriorRing.Coordinates;
        Assert.Equal(60, corners[0].Distance(corners[3]), 3);
    }

    [Fact]
    public void CreateAirports_BlendsThePavementTowardsTheLabelColourOnEveryTheme()
    {
        var dark = IvaoMapLayers.CreateAirports([Heathrow()], Dark, resolution: 5)
            .Features.OfType<GeometryFeature>().First();
        var light = IvaoMapLayers.CreateAirports([Heathrow()], Light, resolution: 5)
            .Features.OfType<GeometryFeature>().First();

        Assert.NotEqual(FillColor(dark), FillColor(light));
        Assert.Equal(
            WorldMap.Mix(WorldMap.Parse(Light.Land), WorldMap.Parse(Light.Label), 0.55),
            FillColor(light));
    }

    [Fact]
    public void CreateAirports_ReturnsAnEmptyLayerWithoutAirports()
    {
        var layer = IvaoMapLayers.CreateAirports([], Dark, IvaoMapLayers.RunwayResolution);

        Assert.Empty(layer.Features);
        Assert.Equal(IvaoMapLayers.AirportsLayerName, layer.Name);
    }

    [Fact]
    public void CreateAirports_CanHideRunwaysAndDrawFixedScaleTrafficMarkers()
    {
        var airport = Heathrow();
        var layer = IvaoMapLayers.CreateAirports(
            [airport], Dark, resolution: 5, showRunways: false, trafficAirports: [airport]);

        Assert.Empty(layer.Features.OfType<GeometryFeature>());
        var trafficMarker = Assert.Single(layer.Features.OfType<PointFeature>(),
            feature => feature.Styles.OfType<SymbolStyle>().Any());
        var markerStyle = trafficMarker.Styles.OfType<SymbolStyle>().Single();

        Assert.Equal(0.3, markerStyle.SymbolScale);
        Assert.Equal(WorldMap.WithAlpha(WorldMap.Parse(Dark.Accent), 128), markerStyle.Outline?.Color);
        Assert.Equal("EGLL", trafficMarker["Icao"]);
        Assert.Single(trafficMarker.Styles.OfType<LabelStyle>());
    }

    [Fact]
    public void OnlineMap_ShowsTrafficAirportTooltipWithoutRunwayPolygons()
    {
        var airport = AirportReference.Find("EGLL")!;
        var pilot = new IvaoPilot
        {
            Id = 1,
            Callsign = "TEST1",
            LastTrack = new IvaoLastTrack { Latitude = 51.4706, Longitude = -0.45 },
            FlightPlan = new IvaoFlightPlan { Departure = new IvaoAirport { Icao = airport.Icao } }
        };
        using var model = new MapViewModel(new Images()) { MapStyle = "positron" };
        model.Initialize(Dark, airport.Position, resolution: 5);
        model.Map!.Navigator.SetSize(800, 600);
        model.UpdateTraffic(new TrackerSnapshot([pilot], [], [], new IvaoConnectionStats(),
            DateTimeOffset.UtcNow, []));

        var layer = Assert.IsType<MemoryLayer>(model.Map.Layers.Single(layer => layer.Name == IvaoMapLayers.AirportsLayerName));
        Assert.Empty(layer.Features.OfType<GeometryFeature>());

        var screen = model.Map.Navigator.Viewport.WorldToScreen(WorldMap.Project(airport.Position));
        model.SetHoveredAtc(new ScreenPosition(screen.X, screen.Y));

        Assert.True(model.HasMapTooltip);
        Assert.Contains("EGLL", model.MapTooltipText);
        Assert.Null(model.HoveredAtc);
    }

    [Fact]
    public void CreateAirports_SkipsAnAirportWithoutPavement()
    {
        var noRunways = new AirportRecord("EMPT", new GeoCoordinate(51.5, -0.4), "Nowhere", []);

        var layer = IvaoMapLayers.CreateAirports([noRunways], Dark, resolution: 5);

        // The code still marks the field, but there is no strip to draw.
        Assert.Empty(layer.Features.OfType<GeometryFeature>());
        Assert.Single(layer.Features.OfType<PointFeature>());
    }

    private static Color? FillColor(GeometryFeature feature) =>
        feature.Styles.OfType<VectorStyle>().First().Fill?.Color;

    private static Color? OutlineColor(GeometryFeature feature) =>
        feature.Styles.OfType<VectorStyle>().First().Outline?.Color;

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

    private static AirportRecord Heathrow() => new(
        "EGLL",
        new GeoCoordinate(51.4706, -0.4619),
        "London Heathrow Airport",
        [
            new AirportRunway(new GeoCoordinate(51.4775, -0.4614), new GeoCoordinate(51.4646, -0.4337), 45),
            new AirportRunway(new GeoCoordinate(51.4775, -0.4864), new GeoCoordinate(51.4646, -0.4586), 45)
        ]);
}
