using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class FlightTrailTests
{
    private static readonly GeoCoordinate Schiphol = new(52.3105, 4.7683);
    private static readonly GeoCoordinate Heathrow = new(51.4706, -0.4619);

    [Fact]
    public void FromTracks_OrdersByTimestampAndDropsUnusableEntries()
    {
        var tracks = new[]
        {
            Track(52.4, 4.9, 15000, At(2)),
            new IvaoPilotTrack { Latitude = null, Longitude = 4.7683, Altitude = 10 },
            Track(52.3105, 4.7683, 20, At(0))
        };

        var points = FlightTrail.FromTracks(tracks);

        Assert.Equal(2, points.Count);
        Assert.Equal(20, points[0].Altitude);
        Assert.Equal(Schiphol, points[0].Position);
        Assert.Equal(15000, points[1].Altitude);
    }

    [Fact]
    public void FromTracks_SkipsRepeatedPositions()
    {
        var tracks = new[]
        {
            Track(52.2, 4.6, 20000, At(0)),
            Track(52.2, 4.6, 20000, At(1)),
            Track(52.3, 4.7, 21000, At(2))
        };

        var points = FlightTrail.FromTracks(tracks);

        Assert.Equal(2, points.Count);
    }

    [Fact]
    public void FromTracks_ReturnsEmptyForNoTracks()
    {
        Assert.Empty(FlightTrail.FromTracks(null));
        Assert.Empty(FlightTrail.FromTracks([]));
    }

    [Fact]
    public void BuildSegments_ColoursEverySegmentByThePointItEndsOn()
    {
        var points = new[]
        {
            new TrailPoint(Schiphol, 0, OnGround: true),
            new TrailPoint(new GeoCoordinate(52.1, 4.1), 12000, OnGround: false),
            new TrailPoint(new GeoCoordinate(52.2, 4.2), 35000, OnGround: false)
        };

        var segments = FlightTrail.BuildSegments(points);

        Assert.Equal(2, segments.Count);
        Assert.Equal(AltitudeBand.Below15000, segments[0].Band);
        Assert.Equal(AltitudeBand.Below40000, segments[1].Band);
        Assert.Equal(points[1].Position, segments[0].End);
        Assert.Equal(points[1].Position, segments[1].Start);
    }

    [Fact]
    public void BuildSegments_SkipsZeroLengthAndInvalidSegments()
    {
        var points = new[]
        {
            new TrailPoint(Schiphol, 1000, OnGround: false),
            new TrailPoint(Schiphol, 1000, OnGround: false),
            new TrailPoint(new GeoCoordinate(double.NaN, 4.7), 1000, OnGround: false)
        };

        Assert.Empty(FlightTrail.BuildSegments(points));
    }

    [Fact]
    public void BuildSegments_SplitsSegmentsCrossingTheAntimeridian()
    {
        var points = new[]
        {
            new TrailPoint(new GeoCoordinate(0, 179.5), 30000, OnGround: false),
            new TrailPoint(new GeoCoordinate(0, -179.5), 31000, OnGround: false)
        };

        var segments = FlightTrail.BuildSegments(points);

        Assert.Equal(2, segments.Count);
        Assert.Equal(180, segments[0].End.Longitude, 6);
        Assert.Equal(-180, segments[1].Start.Longitude, 6);
        Assert.Equal(-179.5, segments[1].End.Longitude, 6);
    }

    [Fact]
    public void BuildPilotTrail_EndsOnTheLivePositionOfTheSummaryFeed()
    {
        var pilot = Pilot(
            Schiphol,
            Heathrow,
            new GeoCoordinate(52.4, 4.9),
            liveAltitude: 3000);
        var tracks = new[]
        {
            Track(52.2, 4.6, 20000, At(0)),
            Track(52.3, 4.7, 15000, At(1))
        };

        var segments = FlightTrail.BuildPilotTrail(pilot, tracks);

        Assert.Equal(2, segments.Count);
        Assert.Equal(52.2, segments[0].Start.Latitude, 4);
        Assert.Equal(pilot.LastTrack!.Position, segments[^1].End);
        Assert.Equal(AltitudeBand.Below5000, segments[^1].Band);
    }

    [Fact]
    public void BuildPilotTrail_UsesTheTrackInsteadOfTheFiledRoute()
    {
        var pilot = Pilot(Schiphol, Heathrow, new GeoCoordinate(52.4, 4.9));
        var tracks = new[]
        {
            Track(52.2, 4.6, 20000, At(0)),
            Track(52.3, 4.7, 20000, At(1))
        };

        var segments = FlightTrail.BuildPilotTrail(pilot, tracks);

        // A recorded track that wanders away from the filed route still wins.
        Assert.Equal(new GeoCoordinate(52.2, 4.6), segments[0].Start);
        Assert.Equal(3, segments.Count);
        Assert.DoesNotContain(segments, segment => segment.Start == Schiphol || segment.End == Heathrow);
    }

    [Fact]
    public void BuildPilotTrail_FallsBackToTheFlownPartOfTheRoute()
    {
        var aircraft = new GeoCoordinate(52.03, 3.03);
        var pilot = Pilot(
            Schiphol,
            Heathrow,
            aircraft,
            liveAltitude: 20000,
            departureDistance: 70,
            arrivalDistance: 140);

        var segments = FlightTrail.BuildPilotTrail(pilot, tracks: null);

        Assert.NotEmpty(segments);
        Assert.Equal(Schiphol.Latitude, segments[0].Start.Latitude, 3);
        Assert.Equal(Schiphol.Longitude, segments[0].Start.Longitude, 3);
        Assert.Equal(aircraft, segments[^1].End);
        Assert.All(segments, segment => Assert.Equal(AltitudeBand.Below30000, segment.Band));

        // Only the first third of the route is behind the aircraft, so the trail
        // must not reach the arrival airport.
        var total = RouteBuilder.DistanceNm(Schiphol, Heathrow);
        var covered = segments
            .Select(segment => RouteBuilder.DistanceNm(Schiphol, segment.End))
            .Max();
        Assert.InRange(covered, total / 6, total / 2);
    }

    [Fact]
    public void BuildPilotTrail_ReturnsEmptyWhenThereIsNothingToDraw()
    {
        Assert.Empty(FlightTrail.BuildPilotTrail(null, null));

        var withoutFlightPlan = new IvaoPilot
        {
            Id = 7,
            LastTrack = new IvaoLastTrack { Latitude = 52, Longitude = 4, Altitude = 30000 }
        };
        Assert.Empty(FlightTrail.BuildPilotTrail(withoutFlightPlan, null));

        var withoutPosition = new IvaoPilot
        {
            Id = 8,
            FlightPlan = new IvaoFlightPlan
            {
                Departure = new IvaoAirport { Icao = "EHAM", Latitude = 52.3105, Longitude = 4.7683 },
                Arrival = new IvaoAirport { Icao = "EGLL", Latitude = 51.4706, Longitude = -0.4619 }
            }
        };
        Assert.Empty(FlightTrail.BuildPilotTrail(withoutPosition, null));
    }

    [Fact]
    public void BuildLines_TracesTheRecordedTrackOfAStation()
    {
        var tracks = new[]
        {
            Track(52.0, 4.0, 0, At(0)),
            Track(52.1, 4.1, 0, At(1)),
            Track(52.2, 4.2, 0, At(2))
        };

        var lines = FlightTrail.BuildLines(tracks);

        Assert.Single(lines);
        Assert.Equal(3, lines[0].Count);
        Assert.Equal(new GeoCoordinate(52.2, 4.2), lines[0][^1]);
    }

    [Fact]
    public void BuildLines_ReturnsEmptyForNoTracks() => Assert.Empty(FlightTrail.BuildLines(null));

    private static DateTimeOffset At(int second) => new(2026, 10, 31, 10, 0, second, TimeSpan.Zero);

    private static IvaoPilotTrack Track(double latitude, double longitude, double altitude, DateTimeOffset timestamp) =>
        new()
        {
            Latitude = latitude,
            Longitude = longitude,
            Altitude = altitude,
            Timestamp = timestamp
        };

    private static IvaoPilot Pilot(
        GeoCoordinate departure,
        GeoCoordinate arrival,
        GeoCoordinate live,
        int liveAltitude = 20000,
        double? departureDistance = null,
        double? arrivalDistance = null) =>
        new()
        {
            Id = 1,
            Callsign = "TEST123",
            LastTrack = new IvaoLastTrack
            {
                Latitude = live.Latitude,
                Longitude = live.Longitude,
                Altitude = liveAltitude,
                DepartureDistance = departureDistance,
                ArrivalDistance = arrivalDistance
            },
            FlightPlan = new IvaoFlightPlan
            {
                Id = 2,
                Departure = new IvaoAirport
                {
                    Icao = "EHAM",
                    Latitude = departure.Latitude,
                    Longitude = departure.Longitude
                },
                Arrival = new IvaoAirport
                {
                    Icao = "EGLL",
                    Latitude = arrival.Latitude,
                    Longitude = arrival.Longitude
                }
            }
        };
}
