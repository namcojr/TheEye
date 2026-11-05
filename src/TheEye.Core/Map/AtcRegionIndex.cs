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

        return new AtcRegionIndex(regions.OrderBy(region =>
            region.Geometry is MultiPolygon wrapped
                ? wrapped.GetGeometryN(0).Area
                : region.Geometry.Area).ToList());
    }

    public IReadOnlyList<IvaoAtc> FindAllAt(MPoint point)
    {
        var ntsPoint = new Point(point.X, point.Y);
        var matches = new List<IvaoAtc>();
        foreach (var (atc, geometry) in _regions)
        {
            if (Covers(geometry, point) && geometry.Contains(ntsPoint))
            {
                matches.Add(atc);
            }
        }

        return matches;
    }

    public IvaoAtc? FindAt(MPoint point)
    {
        var ntsPoint = new Point(point.X, point.Y);
        foreach (var (atc, geometry) in _regions)
        {
            if (Covers(geometry, point) && geometry.Contains(ntsPoint))
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
    private static bool Covers(Geometry geometry, MPoint point) =>
        geometry.EnvelopeInternal.Contains(point.X, point.Y);

    public IvaoAtc? FindAt(GeoCoordinate coordinate)
    {
        if (!coordinate.IsValid)
        {
            return null;
        }

        return FindAt(WorldMap.Project(coordinate));
    }
}
