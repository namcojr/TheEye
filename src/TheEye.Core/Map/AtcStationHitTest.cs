using TheEye.Core.Models;

namespace TheEye.Core.Maps;

/// <summary>
/// Screen position and clickable size of a station badge. The position is the
/// projected airport coordinate, where every badge of that airport is drawn, so
/// a hit test runs in map pixels exactly where the renderer draws the glyph.
/// </summary>
public readonly record struct AtcStationHitTarget(
    IvaoAtc Station,
    double X,
    double Y,
    double Radius,
    int Order);

/// <summary>
/// Picks the station badge under the pointer. The badges of one airport are
/// concentric rings around the same coordinate, so they cover each other: the
/// pointer picks the ring it sits deepest in, i.e. the small star in the middle
/// and the wide approach ring at its edge, and every further click on the same
/// airport walks outwards through the stack, which keeps all positions of the
/// airport reachable. Overlapping badges and control sectors join the same
/// cycle, with glyphs first because they are drawn on top of the sectors.
/// </summary>
public static class AtcStationHitTest
{
    /// <summary>Badge radii closer than this count as equally wide.</summary>
    private const double RadiusTolerance = 0.5;

    /// <summary>Pointer distances closer than this count as the same spot.</summary>
    private const double DistanceTolerance = 0.5;

    /// <summary>
    /// The badge a click would select when nothing is selected yet: the tightest
    /// ring the pointer is inside, the closer one for badges of another airport,
    /// and the smaller facility when two badges are equally wide and close.
    /// </summary>
    public static AtcStationHitTarget? Nearest(IReadOnlyList<AtcStationHitTarget> targets, double x, double y)
    {
        AtcStationHitTarget? best = null;
        var bestDistance = double.MaxValue;

        foreach (var target in targets)
        {
            var dx = target.X - x;
            var dy = target.Y - y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            if (distance > target.Radius)
            {
                continue;
            }

            if (best is not { } current || IsBetter(target, distance, current, bestDistance))
            {
                best = target;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Ordering of two badges the pointer is inside. The smallest ring wins, so a
    /// click in the middle of an airport picks the star and a click on the rim
    /// picks the wide approach ring around it; badges of two different airports
    /// fall back to the usual "closest wins".
    /// </summary>
    private static bool IsBetter(
        AtcStationHitTarget candidate,
        double candidateDistance,
        AtcStationHitTarget current,
        double currentDistance) =>
        candidate.Radius < current.Radius - RadiusTolerance
        || (Math.Abs(candidate.Radius - current.Radius) <= RadiusTolerance
            && (candidateDistance < currentDistance - DistanceTolerance
                || (Math.Abs(candidateDistance - currentDistance) <= DistanceTolerance
                    && candidate.Order < current.Order)));

    /// <summary>
    /// Picks the station a click selects. When <paramref name="selectedId"/> is
    /// one of the overlapping badges or sectors, the next station is returned
    /// instead, finally wrapping around to the first badge or smallest sector.
    /// </summary>
    public static IvaoAtc? Find(
        IReadOnlyList<AtcStationHitTarget> targets,
        double x,
        double y,
        long? selectedId = null,
        IReadOnlyList<IvaoAtc>? regions = null)
    {
        var hit = Nearest(targets, x, y);
        var stack = hit is { } badge
            ? Stack(targets, badge, x, y).Select(target => target.Station).ToList()
            : new List<IvaoAtc>();
        foreach (var region in regions ?? [])
        {
            if (!stack.Any(station => station.Id == region.Id))
            {
                stack.Add(region);
            }
        }

        var first = hit?.Station ?? stack.FirstOrDefault();
        if (selectedId is not { } id || stack.Count == 0)
        {
            return first;
        }

        var current = stack.FindIndex(station => station.Id == id);
        return current < 0 ? first : stack[(current + 1) % stack.Count];
    }

    /// <summary>
    /// The badges drawn on the hit airport plus other badges overlapping the
    /// pointer, in the order a click walks through them.
    /// </summary>
    private static List<AtcStationHitTarget> Stack(
        IReadOnlyList<AtcStationHitTarget> targets,
        AtcStationHitTarget hit,
        double x,
        double y)
    {
        var stack = new List<AtcStationHitTarget>();
        foreach (var target in targets)
        {
            var dx = target.X - x;
            var dy = target.Y - y;
            if ((Math.Abs(target.X - hit.X) < 0.5 && Math.Abs(target.Y - hit.Y) < 0.5)
                || dx * dx + dy * dy <= target.Radius * target.Radius)
            {
                stack.Add(target);
            }
        }

        stack.Sort((left, right) => left.Order != right.Order
            ? left.Order.CompareTo(right.Order)
            : string.Compare(left.Station.Callsign, right.Station.Callsign, StringComparison.OrdinalIgnoreCase));
        return stack;
    }
}
