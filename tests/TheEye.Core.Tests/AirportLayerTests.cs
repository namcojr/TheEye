using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Reference;

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

    private static AirportRecord Heathrow() => new(
        "EGLL",
        new GeoCoordinate(51.4706, -0.4619),
        "London Heathrow Airport",
        [
            new AirportRunway(new GeoCoordinate(51.4775, -0.4614), new GeoCoordinate(51.4646, -0.4337), 45),
            new AirportRunway(new GeoCoordinate(51.4775, -0.4864), new GeoCoordinate(51.4646, -0.4586), 45)
        ]);
}
