using Mapsui;
using NetTopologySuite.Geometries;
using TheEye.Core.Models;

namespace TheEye.Core.Maps;

/// <summary>
/// Spatial index for clicking ATC sectors. Smaller sectors are tested first so
/// a tower polygon wins over the larger approach/control polygon it sits in.
/// </summary>
public sealed class AtcRegionIndex
{
    private readonly List<(IvaoAtc Atc, Geometry Geometry)> _regions;

    private AtcRegionIndex(List<(IvaoAtc Atc, Geometry Geometry)> regions)
    {
        _regions = regions;
    }

    public int Count => _regions.Count;

    public static AtcRegionIndex Build(IEnumerable<IvaoAtc> atc)
    {
        var regions = new List<(IvaoAtc Atc, Geometry Geometry)>();
        foreach (var station in atc)
        {
            if (IvaoMapLayers.CreatePolygon(station) is { } geometry)
            {
                regions.Add((station, geometry));
            }
        }

        return new AtcRegionIndex(regions.OrderBy(region => region.Geometry.Area).ToList());
    }

    public IReadOnlyList<IvaoAtc> FindAllAt(MPoint point)
    {
        var x = WorldWrap.Wrap(point.X);
        var matches = new List<IvaoAtc>();
        foreach (var (atc, geometry) in _regions)
        {
            if (Contains(geometry, x, point.Y))
            {
                matches.Add(atc);
            }
        }

        return matches;
    }

    public IvaoAtc? FindAt(MPoint point)
    {
        var x = WorldWrap.Wrap(point.X);
        foreach (var (atc, geometry) in _regions)
        {
            if (Contains(geometry, x, point.Y))
            {
                return atc;
            }
        }

        return null;
    }

    /// <summary>
    /// The bounding box answers for almost every sector, and the pointer asks on
    /// every move, so the exact test only runs for the few that are left.
    /// </summary>
    /// <summary>
    /// A sector across the date line runs on past the edge of the world, so a point
    /// in the main world may lie on it as seen from the copy next door.
    /// </summary>
    private static bool Contains(Geometry geometry, double x, double y) =>
        ContainsExactly(geometry, x, y) ||
        ContainsExactly(geometry, x + WorldWrap.WorldWidth, y) ||
        ContainsExactly(geometry, x - WorldWrap.WorldWidth, y);

    private static bool ContainsExactly(Geometry geometry, double x, double y) =>
        geometry.EnvelopeInternal.Contains(x, y) && geometry.Contains(new Point(x, y));

    public IvaoAtc? FindAt(GeoCoordinate coordinate)
    {
        if (!coordinate.IsValid)
        {
            return null;
        }

        return FindAt(WorldMap.Project(coordinate));
    }
}
