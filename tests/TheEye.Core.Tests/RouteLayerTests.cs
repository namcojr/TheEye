using System.Text.Json;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using TheEye.Core.Api;
using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class RouteLayerTests
{
    private static readonly MapPalette Palette = new(
        Water: "#16324F",
        Land: "#1B2A35",
        Border: "#2F4858",
        Label: "#6FE3F5",
        Accent: "#4CC9F0",
        IsDark: true);

    [Fact]
    public void CreateRoute_MergesSegmentsOfTheSameAltitudeBandIntoOneLine()
    {
        var pilot = Pilot();
        var tracks = new[]
        {
            Track(52.0, 4.0, 0, onGround: true),
            Track(52.1, 4.1, 0, onGround: true),
            Track(52.2, 4.2, 5000, onGround: false),
            Track(52.3, 4.3, 5000, onGround: false),
            Track(52.4, 4.4, 30000, onGround: false),
            Track(52.5, 4.5, 30000, onGround: false)
        };

        var lines = TrailLines(IvaoMapLayers.CreateRoute(pilot, Palette, tracks));

        Assert.Equal(3, lines.Count);
        Assert.Equal([2, 3, 3], lines.Select(line => line.Geometry!.NumPoints));
        Assert.Equal(
            [
                WorldMap.Parse(AltitudePalette.Ground),
                WorldMap.Parse(AltitudePalette.Below7000),
                WorldMap.Parse(AltitudePalette.Below40000)
            ],
            lines.Select(PenColor));
        Assert.All(lines, line => Assert.Equal(IvaoMapLayers.TrailWidth, PenWidth(line)));
    }

    [Fact]
    public void CreateRoute_SplitsTheTrailAtTheAntimeridian()
    {
        var tracks = new[]
        {
            Track(0, 179.5, 30000, onGround: false),
            Track(0, -179.5, 30000, onGround: false)
        };

        var pilot = Pilot();
        pilot.LastTrack!.Latitude = 0;
        pilot.LastTrack.Longitude = -179.5;

        var lines = TrailLines(IvaoMapLayers.CreateRoute(pilot, Palette, tracks));

        Assert.Equal(2, lines.Count);
        Assert.All(lines, line => Assert.Equal(2, line.Geometry!.NumPoints));
    }

    [Fact]
    public void CreateRoute_PaintsEverySegmentOfTheRecordedPayloadWithItsAltitude()
    {
        // The real shape of v2/tracker/sessions/{id}/tracks: one object per
        // recorded position, climbing from the apron to the cruise level.
        const string payload = """
            [
              { "altitude": 19, "latitude": 34.868385, "longitude": 33.60998, "onGround": true,
                "timestamp": "2026-10-31T12:47:39.000Z", "time": 1 },
              { "altitude": 22, "latitude": 34.871, "longitude": 33.613, "onGround": true,
                "timestamp": "2026-10-31T12:48:39.000Z", "time": 61 },
              { "altitude": 2400, "latitude": 34.905, "longitude": 33.645, "onGround": false,
                "timestamp": "2026-10-31T12:49:39.000Z", "time": 121 },
              { "altitude": 12800, "latitude": 35.012, "longitude": 33.712, "onGround": false,
                "timestamp": "2026-10-31T12:54:39.000Z", "time": 421 },
              { "altitude": 26500, "latitude": 35.204, "longitude": 33.804, "onGround": false,
                "timestamp": "2026-10-31T13:02:39.000Z", "time": 901 },
              { "altitude": 35500, "latitude": 35.402, "longitude": 33.918, "onGround": false,
                "timestamp": "2026-10-31T13:12:39.000Z", "time": 1501 },
              { "altitude": 41000, "latitude": 35.611, "longitude": 34.044, "onGround": false,
                "timestamp": "2026-10-31T13:24:39.000Z", "time": 2221 }
            ]
            """;

        var tracks = JsonSerializer.Deserialize<List<IvaoPilotTrack>>(payload, IvaoJson.Options);
        var pilot = Pilot();
        pilot.LastTrack!.Latitude = 35.611;
        pilot.LastTrack.Longitude = 34.044;
        pilot.LastTrack.Altitude = 41000;

        var lines = TrailLines(IvaoMapLayers.CreateRoute(pilot, Palette, tracks));

        Assert.Equal(
            [
                WorldMap.Parse(AltitudePalette.Ground),
                WorldMap.Parse(AltitudePalette.Below3000),
                WorldMap.Parse(AltitudePalette.Below15000),
                WorldMap.Parse(AltitudePalette.Below30000),
                WorldMap.Parse(AltitudePalette.Below40000),
                WorldMap.Parse(AltitudePalette.Above40000)
            ],
            lines.Select(PenColor));
        Assert.All(lines, line => Assert.Equal(IvaoMapLayers.TrailWidth, PenWidth(line)));
        Assert.All(lines, line => Assert.Equal(255, PenColor(line)!.Value.A));
    }

    [Fact]
    public void CreateRoute_DrawsTheFlownPartOfTheRouteAsAGhostLineWithoutATrack()
    {
        var line = Assert.Single(TrailLines(IvaoMapLayers.CreateRoute(Pilot(), Palette, tracks: null)));

        Assert.Equal(IvaoMapLayers.TrailWidth, PenWidth(line));
        Assert.Equal(IvaoMapLayers.EstimatedTrailAlpha, PenColor(line)!.Value.A);
    }

    [Fact]
    public void CreateRoute_FallsBackToTheFlownRouteAndMarksTheAirports()
    {
        var layer = IvaoMapLayers.CreateRoute(Pilot(), Palette, tracks: null);
        var features = layer.Features.ToList();

        var line = Assert.Single(TrailLines(layer));
        Assert.Equal(
            WorldMap.WithAlpha(WorldMap.Parse(AltitudePalette.Below40000), IvaoMapLayers.EstimatedTrailAlpha),
            PenColor(line));
        Assert.Equal(2, features.OfType<PointFeature>().Count());
    }

    [Fact]
    public void CreateRoute_WaitsForTheTrackInsteadOfGuessingTheRoute()
    {
        var layer = IvaoMapLayers.CreateRoute(Pilot(), Palette, tracks: null, estimateWhenMissing: false);

        // No line while the recorded track is still being fetched; the airports
        // come from the flight plan, so they are known up front and stay.
        Assert.Empty(TrailLines(layer));
        Assert.Equal(2, layer.Features.OfType<PointFeature>().Count());
    }

    [Fact]
    public void CreateRoute_DrawsNothingForAPilotWithoutAFlightPlanOrPosition()
    {
        var layer = IvaoMapLayers.CreateRoute(new IvaoPilot { Id = 5 }, Palette);

        Assert.Empty(layer.Features);
    }

    [Fact]
    public void CreateAtcRoute_TracesTheSessionInTheFacilityColour()
    {
        var station = new IvaoAtc
        {
            Id = 9,
            Callsign = "EDDB_CTR",
            AtcSession = new IvaoAtcSession { Position = "CTR", Frequency = 132.6 }
        };
        var tracks = new[]
        {
            Track(52.0, 4.0, 0, onGround: false),
            Track(52.1, 4.1, 0, onGround: false),
            Track(52.2, 4.2, 0, onGround: false)
        };

        var lines = TrailLines(IvaoMapLayers.CreateAtcRoute(station, tracks));

        var line = Assert.Single(lines);
        Assert.Equal(3, line.Geometry!.NumPoints);
        Assert.Equal(WorldMap.Parse(FacilityPalette.Control), PenColor(line));
    }

    [Fact]
    public void CreateAtcRoute_ReturnsAnEmptyLayerWithoutTracks()
    {
        var station = new IvaoAtc { Id = 9, Callsign = "EDDB_TWR" };

        Assert.Empty(IvaoMapLayers.CreateAtcRoute(station).Features);
        Assert.Empty(IvaoMapLayers.CreateAtcRoute(station, [Track(52.0, 4.0, 0, onGround: false)]).Features);
    }

    private static List<GeometryFeature> TrailLines(MemoryLayer layer) =>
        layer.Features
            .OfType<GeometryFeature>()
            .Where(feature => feature.Geometry is LineString)
            .ToList();

    private static Color? PenColor(GeometryFeature feature) =>
        feature.Styles.OfType<VectorStyle>().First().Line?.Color;

    private static double? PenWidth(GeometryFeature feature) =>
        feature.Styles.OfType<VectorStyle>().First().Line?.Width;

    private static IvaoPilotTrack Track(double latitude, double longitude, double altitude, bool onGround) =>
        new()
        {
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            OnGround = onGround,
            Timestamp = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero)
        };

    private static IvaoPilot Pilot() => new()
    {
        Id = 1,
        Callsign = "TEST123",
        LastTrack = new IvaoLastTrack
        {
            Latitude = 52.5,
            Longitude = 4.5,
            Altitude = 30000,
            OnGround = false,
            DepartureDistance = 60,
            ArrivalDistance = 120
        },
        FlightPlan = new IvaoFlightPlan
        {
            Id = 2,
            Departure = new IvaoAirport { Icao = "EHAM", Latitude = 52.3105, Longitude = 4.7683 },
            Arrival = new IvaoAirport { Icao = "EGLL", Latitude = 51.4706, Longitude = -0.4619 }
        }
    };
}
