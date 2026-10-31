using TheEye.Core.Models;

namespace TheEye.Core.Maps;

/// <summary>
/// Screen position and clickable size of a station badge. The position is
/// already projected and includes the cluster fan-out, so a hit test runs in
/// map pixels exactly where the renderer draws the glyph.
/// </summary>
public readonly record struct AtcStationHitTarget(
    IvaoAtc Station,
    double X,
    double Y,
    double Radius,
    int Order);

/// <summary>
/// Picks the station badge under the pointer. Tower, ground and delivery
/// badges are drawn on top of the much larger control sector around their
/// airport, so the glyphs are tested before <see cref="AtcRegionIndex"/> and
/// the closest badge wins. When two badges overlap equally the smaller
/// facility (lower <see cref="AtcMarkerStyles.SortOrder"/>) is returned.
/// </summary>
public static class AtcStationHitTest
{
    private const double ToleranceSquared = 0.25;

    public static IvaoAtc? Find(IReadOnlyList<AtcStationHitTarget> targets, double x, double y)
    {
        AtcStationHitTarget? best = null;
        var bestDistance = double.MaxValue;

        foreach (var target in targets)
        {
            var dx = target.X - x;
            var dy = target.Y - y;
            var distance = (dx * dx) + (dy * dy);
            if (distance > target.Radius * target.Radius)
            {
                continue;
            }

            if (best is not { } current
                || distance < bestDistance - ToleranceSquared
                || (Math.Abs(distance - bestDistance) <= ToleranceSquared && target.Order < current.Order))
            {
                best = target;
                bestDistance = distance;
            }
        }

        return best?.Station;
    }
}
