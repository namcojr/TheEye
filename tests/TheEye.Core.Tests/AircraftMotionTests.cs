using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Nts;
using Mapsui.Styles;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Services;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

public sealed class AircraftMotionTests
{
    [Theory]
    [InlineData(0, 0, 90)]
    [InlineData(0, 0, 0)]
    [InlineData(80, 20, 90)]
    [InlineData(-80, -20, 270)]
    [InlineData(89.999, 0, 0)]
    [InlineData(0, 179.99, 90)]
    public void MovesExactlyGroundSpeedTimesElapsedAlongAGreatCircle(double latitude, double longitude, int heading)
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        var track = Track(clock, latitude, longitude, heading: heading);
        motion.Update(track);
        clock.Advance(30);

        var position = motion.Position!.Value;
        Assert.True(position.IsValid);
        Assert.Equal(3, DistanceNm(track.Position!.Value, position), 7); // 360 kt for 30 seconds.
        Assert.Equal(new GeoCoordinate(latitude, longitude), track.Position);
        if (longitude == 179.99) Assert.True(position.Longitude < -179);
        if (latitude == 0 && heading == 90) Assert.Equal(0, position.Latitude, 8);
        if (latitude == 0 && heading == 0) Assert.Equal(0, position.Longitude, 8);
    }

    [Theory]
    [InlineData(null, 90)]
    [InlineData(0, 90)]
    [InlineData(-10, 90)]
    [InlineData(360, null)]
    public void MissingOrNonPositiveMotionDataStaysStationary(int? speed, int? heading)
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        var track = Track(clock, speed: speed, heading: heading);
        motion.Update(track);
        clock.Advance(30);
        Assert.Equal(track.Position, motion.Position);
    }

    [Fact]
    public void PredictionStopsAtExactlySixtySecondsAndDuplicatesDoNotRestartIt()
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        var track = Track(clock);
        motion.Update(track);
        clock.Advance(59);
        Assert.Equal(5.9, DistanceNm(track.Position!.Value, motion.Position!.Value), 7);
        var duplicate = Track(clock);
        duplicate.Timestamp = track.Timestamp;
        motion.Update(duplicate);
        clock.Advance(1);
        var frozen = motion.Position;
        Assert.Equal(6, DistanceNm(track.Position!.Value, frozen!.Value), 7);
        clock.Advance(120);
        motion.Update(track);
        Assert.Equal(frozen, motion.Position);
    }

    [Fact]
    public void TimestampAgeCountsTowardLimitAndFutureTimestampIsClamped()
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        var track = Track(clock);
        track.Timestamp = clock.GetUtcNow().AddSeconds(-45);
        motion.Update(track);
        Assert.Equal(4.5, DistanceNm(track.Position!.Value, motion.Position!.Value), 7);
        clock.Advance(15);
        var frozen = motion.Position;
        clock.Advance(15);
        Assert.Equal(frozen, motion.Position);

        var future = new AircraftMotion(clock);
        track.Timestamp = clock.GetUtcNow().AddMinutes(5);
        future.Update(track);
        Assert.Equal(track.Position, future.Position);
        clock.Advance(10);
        Assert.Equal(1, DistanceNm(track.Position!.Value, future.Position!.Value), 7);
    }

    [Fact]
    public void TimestampAbsentUsesReceiptAndIdenticalTracksDoNotResetAge()
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        var track = Track(clock);
        track.Timestamp = null;
        motion.Update(track);
        clock.Advance(30);
        motion.Update(track);
        clock.Advance(30);
        var frozen = motion.Position;
        clock.Advance(100);
        Assert.Equal(frozen, motion.Position);
        Assert.Equal(6, DistanceNm(track.Position!.Value, frozen!.Value), 7);
    }

    [Fact]
    public void NewSampleCorrectsContinuouslyAndReachesMovingTargetAfterOneSecond()
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        motion.Update(Track(clock));
        clock.Advance(30);
        var before = motion.Position!.Value;
        var next = Track(clock, longitude: 0.1);
        motion.Update(next);
        Assert.Equal(before.Latitude, motion.Position!.Value.Latitude, 8);
        Assert.Equal(before.Longitude, motion.Position!.Value.Longitude, 8);
        clock.Advance(0.5);
        Assert.InRange(motion.Position!.Value.Longitude, before.Longitude, next.Longitude!.Value);
        clock.Advance(0.5);
        Assert.Equal(0.1, DistanceNm(next.Position!.Value, motion.Position!.Value), 7);
    }

    [Fact]
    public void CorrectionsTakeShortestPathAcrossDateLineAndCanBeInterrupted()
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        motion.Update(Track(clock, longitude: 179.99, speed: 0));
        clock.Advance(30);
        motion.Update(Track(clock, longitude: -179.99, speed: 0));
        clock.Advance(0.5);
        Assert.InRange(Math.Abs(motion.Position!.Value.Longitude), 179.99, 180);
        var halfway = motion.Position!.Value;
        motion.Update(Track(clock, longitude: -179.98, speed: 0));
        Assert.Equal(halfway.Longitude, motion.Position!.Value.Longitude, 8);
        clock.Advance(1);
        Assert.Equal(-179.98, motion.Position!.Value.Longitude, 8);
    }

    [Fact]
    public void OlderTracksDoNotChangePredictionAndMissingPositionClearsIt()
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        var track = Track(clock);
        motion.Update(track);
        clock.Advance(20);
        var before = motion.Position;
        var older = Track(clock, longitude: 5);
        older.Timestamp = track.Timestamp!.Value.AddSeconds(-1);
        motion.Update(older);
        Assert.Equal(before, motion.Position);
        motion.Update(new IvaoLastTrack { Latitude = double.NaN, Longitude = 0 });
        Assert.Null(motion.Position);
    }

    [Fact]
    public void MonotonicElapsedTimeIgnoresWallClockChanges()
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        var track = Track(clock);
        motion.Update(track);
        clock.UtcNow = clock.UtcNow.AddHours(-1);
        clock.Advance(30);
        Assert.Equal(3, DistanceNm(track.Position!.Value, motion.Position!.Value), 7);
    }

    [Fact]
    public void GroundAircraftMoveAndStopSmoothlyWhenNewSpeedIsZero()
    {
        var clock = new Clock();
        var motion = new AircraftMotion(clock);
        var track = Track(clock, speed: 20);
        track.OnGround = true;
        motion.Update(track);
        clock.Advance(30);
        Assert.Equal(20.0 / 120, DistanceNm(track.Position!.Value, motion.Position!.Value), 7);
        var stopped = Track(clock, speed: 0);
        stopped.OnGround = true;
        motion.Update(stopped);
        clock.Advance(1);
        var position = motion.Position;
        clock.Advance(10);
        Assert.Equal(stopped.Position, position);
        Assert.Equal(position, motion.Position);
    }

    [Fact]
    public void MapAnimationMovesMarkerHitTargetAndHoverButNotReportedTelemetry()
    {
        var clock = new Clock();
        using var model = new MapViewModel(new Images(), clock);
        var palette = new MapPalette("#16324F", "#1B2A35", "#2F4858", "#6FE3F5", "#4CC9F0", true);
        model.Initialize(palette, new GeoCoordinate(0, 0), 100);
        model.Map!.Navigator.SetSize(800, 600);
        var pilot = new IvaoPilot
        {
            Id = 1,
            LastTrack = Track(clock),
            FlightPlan = new IvaoFlightPlan
            {
                Departure = new IvaoAirport { Latitude = 0, Longitude = -1 },
                Arrival = new IvaoAirport { Latitude = 0, Longitude = 1 }
            }
        };
        model.UpdateTraffic(new TrackerSnapshot([pilot], [], [], new IvaoConnectionStats(), clock.GetUtcNow(), []));
        var item = model.Pilots.Single();
        var reportedText = item.PositionText;
        model.SetHoveredPilot(item);
        var hover = model.Map.Layers.Single(layer => layer.Name == IvaoMapLayers.HoverRouteLayerName);
        var oldX = item.X;
        clock.Advance(10);
        model.AdvanceAircraft();
        Assert.True(item.X > oldX + 10);
        var screen = model.Map.Navigator.Viewport.WorldToScreen(WorldMap.Project(item.MapPosition!.Value));
        Assert.Equal(screen.X, item.X + item.MarkerSize / 2d, 6);
        Assert.Same(item, model.HitTestPilot(new ScreenPosition(screen.X, screen.Y)));
        Assert.NotSame(hover, model.Map.Layers.Single(layer => layer.Name == IvaoMapLayers.HoverRouteLayerName));
        Assert.Equal(reportedText, item.PositionText);
        Assert.Equal(0, pilot.LastTrack.Longitude);
        model.UpdateTraffic(new TrackerSnapshot([], [], [], new IvaoConnectionStats(), clock.GetUtcNow(), []));
        model.AdvanceAircraft();
        Assert.Empty(model.Pilots);
        Assert.Empty(Assert.IsType<MemoryLayer>(
            model.Map.Layers.Single(layer => layer.Name == IvaoMapLayers.HoverRouteLayerName)).Features);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(179.99)]
    public void SelectedTrailFollowsPredictedAircraftWithoutRebuildingHistory(double longitude)
    {
        var clock = new Clock();
        var resolver = new NavigraphRouteResolver(
            [("FIXA", new GeoCoordinate(0, longitude)), ("FIXB", new GeoCoordinate(1, longitude))], []);
        using var model = new MapViewModel(new Images(), clock, navDataResolver: () => resolver);
        model.Initialize(new MapPalette("#16324F", "#1B2A35", "#2F4858", "#6FE3F5", "#4CC9F0", true),
            new GeoCoordinate(0, longitude), 100);
        var pilot = new IvaoPilot
        {
            Id = 1,
            LastTrack = Track(clock, longitude: longitude),
            FlightPlan = new IvaoFlightPlan { Route = "FIXA DCT FIXB" }
        };
        model.UpdateTraffic(Snapshot());
        model.SelectPilot(model.Pilots[0], center: false);
        model.SetSelectionTracks(
            [
                new IvaoPilotTrack { Latitude = -0.01, Longitude = longitude, Altitude = 30000 },
                new IvaoPilotTrack { Latitude = 0, Longitude = longitude, Altitude = 30000 }
            ]);
        var history = Layer(IvaoMapLayers.RouteLayerName);
        Assert.Empty(Layer(IvaoMapLayers.LiveTrailLayerName).Features);
        clock.Advance(30);
        model.AdvanceAircraft();

        Assert.Same(history, Layer(IvaoMapLayers.RouteLayerName));
        AssertEndpoints();
        Assert.Equal(longitude, pilot.LastTrack.Longitude);
        var connector = Layer(IvaoMapLayers.LiveTrailLayerName);
        var lines = connector.Features.OfType<GeometryFeature>().ToArray();
        Assert.Equal(longitude == 0 ? 1 : 2, lines.Length);
        Assert.All(lines, line =>
        {
            var pen = line.Styles.OfType<VectorStyle>().Single().Line!;
            Assert.Equal(IvaoMapLayers.TrailWidth, pen.Width);
            Assert.Equal(IvaoMapLayers.EstimatedTrailAlpha, pen.Color.A);
        });

        clock.Advance(31);
        model.AdvanceAircraft();
        var frozen = Layer(IvaoMapLayers.LiveTrailLayerName);
        clock.Advance(30);
        model.AdvanceAircraft();
        Assert.Same(frozen, Layer(IvaoMapLayers.LiveTrailLayerName));

        pilot.LastTrack = Track(clock, longitude: longitude == 0 ? 0.2 : -179.8);
        model.UpdateTraffic(Snapshot());
        clock.Advance(1);
        model.AdvanceAircraft();
        AssertEndpoints();

        model.ClearSelection();
        Assert.Empty(Layer(IvaoMapLayers.LiveTrailLayerName).Features);
        Assert.Empty(Layer(IvaoMapLayers.FlightPlanRoutesLayerName).Features);
        model.AdvanceAircraft();
        Assert.Empty(Layer(IvaoMapLayers.LiveTrailLayerName).Features);

        TrackerSnapshot Snapshot() => new([pilot], [], [], new IvaoConnectionStats(), clock.GetUtcNow(), []);
        MemoryLayer Layer(string name) =>
            Assert.IsType<MemoryLayer>(model.Map!.Layers.Single(layer => layer.Name == name));
        void AssertEndpoints()
        {
            var predicted = WorldMap.Project(model.Pilots[0].MapPosition!.Value);
            var connectorLines = Layer(IvaoMapLayers.LiveTrailLayerName).Features.OfType<GeometryFeature>().ToArray();
            var end = connectorLines[^1].Geometry!.Coordinates[^1];
            Assert.Equal(predicted.X, end.X, 5);
            Assert.Equal(predicted.Y, end.Y, 5);
            var start = connectorLines[0].Geometry!.Coordinates[0];
            var reported = WorldMap.Project(pilot.LastTrack!.Position!.Value);
            Assert.Equal(reported.X, start.X, 5);
            Assert.Equal(reported.Y, start.Y, 5);
            var future = Layer(IvaoMapLayers.FlightPlanRoutesLayerName).Features.OfType<GeometryFeature>().First();
            Assert.Equal(predicted.X, future.Geometry!.Coordinates[0].X, 5);
            Assert.Equal(predicted.Y, future.Geometry.Coordinates[0].Y, 5);
        }
    }

    private static IvaoLastTrack Track(Clock clock, double latitude = 0, double longitude = 0,
        int? speed = 360, int? heading = 90) => new()
    {
        Latitude = latitude, Longitude = longitude, GroundSpeed = speed, Heading = heading,
        Timestamp = clock.GetUtcNow()
    };

    private static double DistanceNm(GeoCoordinate start, GeoCoordinate end)
    {
        var lat1 = start.Latitude * Math.PI / 180;
        var lat2 = end.Latitude * Math.PI / 180;
        var dlat = lat2 - lat1;
        var dlon = (end.Longitude - start.Longitude) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(dlat / 2), 2) +
            Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dlon / 2), 2);
        return 3440.065 * 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        private long _ticks;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(double seconds)
        {
            UtcNow = UtcNow.AddSeconds(seconds);
            _ticks += TimeSpan.FromSeconds(seconds).Ticks;
        }
    }

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
}
