using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using TheEye.Core.Models;

namespace TheEye.Core.Maps;

public static class IvaoMapLayers
{
    public const string AtcRegionsLayerName = "IVAO sectors";
    public const string AtcLabelsLayerName = "IVAO sector labels";
    public const string AtcStationsLayerName = "IVAO ATC stations";
    public const string RouteLayerName = "Route";
    public const string SelectedAtcLayerName = "Selected ATC";

    public static MemoryLayer CreateAtcRegions(IEnumerable<IvaoAtc> atc, MapPalette palette)
    {
        var features = new List<IFeature>();
        foreach (var station in atc)
        {
            var geometry = CreatePolygon(station);
            if (geometry is null)
            {
                continue;
            }

            var color = WorldMap.Parse(FacilityPalette.ForFacility(station.Facility));
            var feature = new GeometryFeature { Geometry = geometry };
            feature["Callsign"] = station.Callsign;
            feature.Styles.Add(new VectorStyle
            {
                Fill = new Brush(WorldMap.WithAlpha(color, palette.IsDark ? 52 : 40)),
                Outline = new Pen(color, 1.1)
            });
            features.Add(feature);
        }

        return new MemoryLayer(AtcRegionsLayerName)
        {
            Features = features,
            Style = null
        };
    }

    public static MemoryLayer CreateAtcLabels(IEnumerable<IvaoAtc> atc, MapPalette palette)
    {
        var features = new List<IFeature>();
        foreach (var station in atc)
        {
            var points = GetGeoPoints(station);
            if (points.Count < 3)
            {
                continue;
            }

            var centroid = GeoPolygon.Centroid(points);
            if (centroid is not { } coordinate || !coordinate.IsValid)
            {
                continue;
            }

            var color = WorldMap.Parse(FacilityPalette.ForFacility(station.Facility));
            var feature = new PointFeature(WorldMap.Project(coordinate));
            feature["Callsign"] = station.Callsign;
            feature.Styles.Add(new LabelStyle
            {
                LabelColumn = "Callsign",
                Font = new Font { FontFamily = "Segoe UI", Size = 10, Bold = true },
                ForeColor = color,
                BackColor = null,
                Halo = new Pen(WorldMap.Parse(palette.Land), 2.5),
                CollisionDetection = true,
                MaxVisible = 14000
            });
            features.Add(feature);
        }

        return new MemoryLayer(AtcLabelsLayerName)
        {
            Features = features,
            Style = null
        };
    }

    public static MemoryLayer CreateAtcStations(IEnumerable<IvaoAtc> atc, MapPalette palette, double zoom = 1)
    {
        // Only positions without a sector polygon need a glyph; sector shapes are
        // drawn (and labelled at the centroid) by the sector layers instead.
        var stations = atc
            .Where(station => station.Position is { } position && position.IsValid && GetGeoPoints(station).Count < 3)
            .Select(station => new { Station = station, Point = WorldMap.Project(station.Position!.Value) })
            .ToList();

        var features = new List<IFeature>(stations.Count);
        foreach (var cluster in stations.GroupBy(
            station => (X: Math.Round(station.Point.X), Y: Math.Round(station.Point.Y))))
        {
            var members = cluster
                .OrderBy(member => AtcMarkerStyles.SortOrder(member.Station.Facility, member.Station.Callsign))
                .ThenBy(member => member.Station.Callsign, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var offsets = AtcMarkerStyles.ClusterOffsets(members.Count, zoom);

            for (var index = 0; index < members.Count; index++)
            {
                var station = members[index].Station;
                var marker = AtcMarkerStyles.For(station.Facility, station.Callsign, zoom);
                var offset = offsets[index];
                var labelOffset = AtcMarkerStyles.LabelOffset(marker, offset);
                var labelAlignment = AtcMarkerStyles.LabelAlignment(labelOffset);

                var feature = new PointFeature(members[index].Point);
                feature["Callsign"] = station.Callsign;
                feature["Frequency"] = station.FrequencyText;
                feature.Styles.Add(AtcMarkerStyles.CreateImageStyle(marker, offset));
                feature.Styles.Add(new LabelStyle
                {
                    LabelColumn = "Callsign",
                    Font = new Font
                    {
                        FontFamily = "Segoe UI",
                        Size = AtcMarkerStyles.LabelFontSize(zoom),
                        Bold = true
                    },
                    ForeColor = WorldMap.Parse(FacilityPalette.ForFacility(station.Facility)),
                    BackColor = null,
                    Halo = new Pen(WorldMap.Parse(palette.Land), 2.5),
                    Offset = new Offset(labelOffset.X, labelOffset.Y),
                    HorizontalAlignment = labelAlignment.Horizontal,
                    VerticalAlignment = labelAlignment.Vertical,
                    CollisionDetection = true,
                    MaxVisible = 3200
                });
                features.Add(feature);
            }
        }

        return new MemoryLayer(AtcStationsLayerName)
        {
            Features = features,
            Style = null
        };
    }

    public static MemoryLayer CreateRoute(IvaoPilot pilot, MapPalette palette)
    {
        var features = new List<IFeature>();
        var flightPlan = pilot.FlightPlan;
        var departure = flightPlan?.Departure?.Position;
        var arrival = flightPlan?.Arrival?.Position;

        if (departure is { } start && arrival is { } end)
        {
            var color = WorldMap.Parse(palette.Accent);
            foreach (var line in RouteBuilder.BuildLines(start, end))
            {
                var coordinates = line
                    .Select(point => WorldMap.Project(point))
                    .Select(point => new Coordinate(point.X, point.Y))
                    .ToArray();

                if (coordinates.Length < 2)
                {
                    continue;
                }

                var feature = new GeometryFeature { Geometry = new LineString(coordinates) };
                feature.Styles.Add(new VectorStyle
                {
                    Line = new Pen(color, 2.2) { PenStyle = PenStyle.Dash }
                });
                features.Add(feature);
            }

            features.Add(CreateAirportFeature(start, flightPlan!.DepartureIcao, color, palette));
            features.Add(CreateAirportFeature(end, flightPlan.ArrivalIcao, color, palette));
        }

        return new MemoryLayer(RouteLayerName)
        {
            Features = features,
            Style = null
        };
    }

    public static MemoryLayer CreateSelectedAtc(IvaoAtc atc, MapPalette palette)
    {
        var features = new List<IFeature>();
        if (CreatePolygon(atc) is { } geometry)
        {
            var accent = WorldMap.Parse(palette.Accent);
            var feature = new GeometryFeature { Geometry = geometry };
            feature.Styles.Add(new VectorStyle
            {
                Fill = new Brush(new Color(0, 0, 0, 0)),
                Outline = new Pen(accent, 3.2)
            });
            features.Add(feature);
        }

        return new MemoryLayer(SelectedAtcLayerName)
        {
            Features = features,
            Style = null
        };
    }

    public static IReadOnlyList<GeoCoordinate> GetGeoPoints(IvaoAtc atc) =>
        GeoPolygon.DistinctConsecutive(atc.PolygonPoints.Select(point => point.ToGeoCoordinate()));

    public static Polygon? CreatePolygon(IvaoAtc atc)
    {
        var points = GetGeoPoints(atc);
        if (points.Count < 3)
        {
            return null;
        }

        var coordinates = points
            .Select(point => WorldMap.Project(point))
            .Select(point => new Coordinate(point.X, point.Y))
            .ToList();

        if (coordinates[0] != coordinates[^1])
        {
            coordinates.Add(coordinates[0]);
        }

        if (coordinates.Count < 4)
        {
            return null;
        }

        return new Polygon(new LinearRing(coordinates.ToArray()));
    }

    private static PointFeature CreateAirportFeature(GeoCoordinate coordinate, string name, Color color, MapPalette palette)
    {
        var feature = new PointFeature(WorldMap.Project(coordinate));
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = 0.55,
            Fill = new Brush(color),
            Outline = new Pen(WorldMap.Parse(palette.Land), 1.5)
        });
        feature.Styles.Add(new LabelStyle
        {
            Text = name,
            Font = new Font { FontFamily = "Segoe UI", Size = 10, Bold = true },
            ForeColor = color,
            BackColor = null,
            Halo = new Pen(WorldMap.Parse(palette.Land), 2.5),
            Offset = new Offset(0, -14),
            CollisionDetection = true
        });
        return feature;
    }
}
