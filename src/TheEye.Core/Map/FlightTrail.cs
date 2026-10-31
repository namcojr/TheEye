using TheEye.Core.Models;

namespace TheEye.Core.Maps;

/// <summary>One recorded position together with the flight state at that moment.</summary>
public readonly record struct TrailPoint(GeoCoordinate Position, double? Altitude, bool OnGround);

/// <summary>
/// One straight piece of a flown trail. The band is the one of the point the
/// segment ends on, so the line is coloured with the altitude the aircraft had
/// when it reached that point.
/// </summary>
public readonly record struct TrailSegment(GeoCoordinate Start, GeoCoordinate End, AltitudeBand Band);

/// <summary>
/// Turns the recorded tracks of a session into the path an aircraft has
/// actually flown. The IVAO web map traces exactly this: the history of the
/// flight from the departure airport up to the aircraft's current position,
/// coloured by altitude. When the track feed is not available the trail falls
/// back to the great circle between the airports, trimmed at the fraction of
/// the route the aircraft has already covered.
/// </summary>
public static class FlightTrail
{
    /// <summary>
    /// Reads the position of every track, oldest first, dropping entries without
    /// a usable coordinate and runs of identical consecutive positions.
    /// </summary>
    public static IReadOnlyList<TrailPoint> FromTracks(IEnumerable<IvaoPilotTrack>? tracks)
    {
        if (tracks is null)
        {
            return [];
        }

        // The feed is chronological already, but ordering by timestamp keeps the
        // line sane when the API returns the pages in a different order.
        var ordered = tracks
            .Where(track => track.Position is not null)
            .OrderBy(track => track.Timestamp is null)
            .ThenBy(track => track.Timestamp)
            .ToList();

        var points = new List<TrailPoint>(ordered.Count);
        foreach (var track in ordered)
        {
            var point = new TrailPoint(track.Position!.Value, track.Altitude, track.OnGround == true);
            if (points.Count > 0 && points[^1].Position == point.Position)
            {
                continue;
            }

            points.Add(point);
        }

        return points;
    }

    /// <summary>
    /// True when <paramref name="tracks"/> holds enough recorded positions to
    /// trace the flight. Without them the trail is the flown part of the filed
    /// route, which carries no altitude history and therefore no colours.
    /// </summary>
    public static bool HasRecordedTrack(IReadOnlyList<IvaoPilotTrack>? tracks) =>
        FromTracks(tracks).Count >= 2;

    /// <summary>
    /// Builds the coloured trail of a flight. The recorded track wins; without
    /// one the flown part of the great-circle route is drawn instead so the
    /// aircraft still shows where it came from. Pass
    /// <paramref name="estimateWhenMissing"/> = false while the track request is
    /// still running: an estimate that is about to be replaced by the real trail
    /// would make the line jump across the map.
    /// </summary>
    public static IReadOnlyList<TrailSegment> BuildPilotTrail(
        IvaoPilot? pilot,
        IReadOnlyList<IvaoPilotTrack>? tracks,
        bool estimateWhenMissing = true)
    {
        if (pilot is null)
        {
            return [];
        }

        var points = FromTracks(tracks);
        if (points.Count < 2)
        {
            return estimateWhenMissing ? BuildFallback(pilot) : [];
        }

        return BuildSegments(AppendLivePoint(points, pilot.LastTrack));
    }

    /// <summary>
    /// Builds the plain lines of a session that is not flown, i.e. an ATC
    /// position, split at the antimeridian.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<GeoCoordinate>> BuildLines(IReadOnlyList<IvaoPilotTrack>? tracks)
    {
        var points = FromTracks(tracks);
        return points.Count < 2
            ? []
            : RouteBuilder.SplitAtAntimeridian(points.Select(point => point.Position).ToList());
    }

    /// <summary>
    /// Turns consecutive points into segments, splitting the pairs that cross
    /// the antimeridian so no segment spans the whole world.
    /// </summary>
    public static IReadOnlyList<TrailSegment> BuildSegments(IReadOnlyList<TrailPoint> points)
    {
        var segments = new List<TrailSegment>(Math.Max(points.Count - 1, 0));
        for (var index = 1; index < points.Count; index++)
        {
            AddSegment(segments, points[index - 1], points[index]);
        }

        return segments;
    }

    private static void AddSegment(List<TrailSegment> segments, TrailPoint from, TrailPoint to)
    {
        if (!from.Position.IsValid || !to.Position.IsValid || from.Position == to.Position)
        {
            return;
        }

        var band = AltitudePalette.Band(to.Altitude, to.OnGround);
        if (Math.Abs(to.Position.Longitude - from.Position.Longitude) > 180)
        {
            var eastern = from.Position.Longitude > 0;
            segments.Add(new TrailSegment(
                from.Position,
                new GeoCoordinate(from.Position.Latitude, eastern ? 180 : -180),
                band));
            segments.Add(new TrailSegment(
                new GeoCoordinate(to.Position.Latitude, eastern ? -180 : 180),
                to.Position,
                band));
            return;
        }

        segments.Add(new TrailSegment(from.Position, to.Position, band));
    }

    /// <summary>
    /// Ends the trail on the live position of the summary feed, which usually
    /// runs a little ahead of the recorded tracks.
    /// </summary>
    private static IReadOnlyList<TrailPoint> AppendLivePoint(IReadOnlyList<TrailPoint> points, IvaoLastTrack? live)
    {
        if (live?.Position is not { } position)
        {
            return points;
        }

        var livePoint = new TrailPoint(position, live.Altitude, live.OnGround == true);
        var extended = points.ToList();
        if (extended[^1].Position == position)
        {
            // Same spot, fresher state: keep the altitude of the live feed so the
            // colour of the closing segment matches the aircraft.
            extended[^1] = livePoint;
            return extended;
        }

        extended.Add(livePoint);
        return extended;
    }

    /// <summary>
    /// Draws the part of the filed route that has already been flown: the great
    /// circle between departure and arrival, cut where the aircraft currently
    /// is. Every segment inherits the current altitude, because the summary feed
    /// carries no history.
    /// </summary>
    private static IReadOnlyList<TrailSegment> BuildFallback(IvaoPilot pilot)
    {
        var live = pilot.LastTrack;
        if (live?.Position is not { } aircraft)
        {
            return [];
        }

        var flightPlan = pilot.FlightPlan;
        if (flightPlan?.Departure?.Position is not { } departure ||
            flightPlan.Arrival?.Position is not { } arrival)
        {
            return [];
        }

        var fraction = TravelledFraction(live, departure, aircraft, arrival);
        var points = RouteBuilder
            .TrimToFraction(RouteBuilder.Build(departure, arrival), fraction)
            .Select(point => new TrailPoint(point, live.Altitude, live.OnGround == true))
            .ToList();

        points.Add(new TrailPoint(aircraft, live.Altitude, live.OnGround == true));
        return BuildSegments(points);
    }

    /// <summary>
    /// How much of the route is behind the aircraft, from the distances reported
    /// by the summary feed, with a geometric estimate as a fallback.
    /// </summary>
    private static double TravelledFraction(
        IvaoLastTrack live,
        GeoCoordinate departure,
        GeoCoordinate aircraft,
        GeoCoordinate arrival)
    {
        if (live.DepartureDistance is { } flown &&
            live.ArrivalDistance is { } remaining &&
            flown + remaining > 0)
        {
            return Math.Clamp(flown / (flown + remaining), 0, 1);
        }

        var total = RouteBuilder.DistanceNm(departure, arrival);
        return total > 0
            ? Math.Clamp(RouteBuilder.DistanceNm(departure, aircraft) / total, 0, 1)
            : 1;
    }
}
