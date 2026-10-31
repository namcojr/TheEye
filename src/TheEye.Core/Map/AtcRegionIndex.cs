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

    public IvaoAtc? FindAt(MPoint point)
    {
        var ntsPoint = new Point(point.X, point.Y);
        foreach (var (atc, geometry) in _regions)
        {
            if (geometry.Contains(ntsPoint))
            {
                return atc;
            }
        }

        return null;
    }

    public IvaoAtc? FindAt(GeoCoordinate coordinate)
    {
        if (!coordinate.IsValid)
        {
            return null;
        }

        return FindAt(WorldMap.Project(coordinate));
    }
}
