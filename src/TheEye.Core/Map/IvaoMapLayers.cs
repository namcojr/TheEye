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

    /// <summary>
    /// One station badge and where it is drawn: the projected coordinate, the
    /// cluster fan-out in screen pixels and the glyph itself.
    /// </summary>
    public sealed record AtcStationPlacement(
        IvaoAtc Station,
        MPoint Point,
        double OffsetX,
        double OffsetY,
        AtcMarkerStyle Marker);

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

    public static MemoryLayer CreateAtcStations(IEnumerable<IvaoAtc> atc, MapPalette palette, double zoom = 1) =>
        CreateAtcStations(CreateAtcStationPlacements(atc, zoom), palette, zoom);

    public static MemoryLayer CreateAtcStations(
        IEnumerable<AtcStationPlacement> placements,
        MapPalette palette,
        double zoom = 1)
    {
        var features = new List<IFeature>();
        foreach (var placement in placements)
        {
            var offset = (placement.OffsetX, placement.OffsetY);
            var labelOffset = AtcMarkerStyles.LabelOffset(placement.Marker, offset);
            var labelAlignment = AtcMarkerStyles.LabelAlignment(labelOffset);

            var feature = new PointFeature(placement.Point);
            feature["Callsign"] = placement.Station.Callsign;
            feature["Frequency"] = placement.Station.FrequencyText;
            feature.Styles.Add(AtcMarkerStyles.CreateImageStyle(placement.Marker, offset));
            feature.Styles.Add(new LabelStyle
            {
                LabelColumn = "Callsign",
                Font = new Font
                {
                    FontFamily = "Segoe UI",
                    Size = AtcMarkerStyles.LabelFontSize(zoom),
                    Bold = true
                },
                ForeColor = WorldMap.Parse(FacilityPalette.ForFacility(placement.Station.Facility)),
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

        return new MemoryLayer(AtcStationsLayerName)
        {
            Features = features,
            Style = null
        };
    }

    /// <summary>
    /// Works out where every station badge is drawn: the projected coordinate
    /// plus the screen pixel offset that fans clustered positions apart. The
    /// station layer and the click handler share these numbers, so a badge is
    /// always hit exactly where it is rendered.
    /// </summary>
    public static IReadOnlyList<AtcStationPlacement> CreateAtcStationPlacements(
        IEnumerable<IvaoAtc> atc,
        double zoom = 1)
    {
        // Only positions without a sector polygon need a glyph; sector shapes are
        // drawn (and labelled at the centroid) by the sector layers instead.
        var stations = atc
            .Where(station => station.Position is { } position && position.IsValid && GetGeoPoints(station).Count < 3)
            .Select(station => new { Station = station, Point = WorldMap.Project(station.Position!.Value) })
            .ToList();

        var placements = new List<AtcStationPlacement>(stations.Count);
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
                placements.Add(new AtcStationPlacement(
                    station,
                    members[index].Point,
                    offsets[index].X,
                    offsets[index].Y,
                    AtcMarkerStyles.For(station.Facility, station.Callsign, zoom)));
            }
        }

        return placements;
    }

    /// <summary>
    /// Width of the trail, in device independent pixels. One pixel keeps a long
    /// recorded track readable while the altitude bands stay distinct.
    /// </summary>
    public const double TrailWidth = 1.0;

    /// <summary>
    /// Alpha of the estimated trail, i.e. the flown part of the filed route that
    /// is drawn while no recorded track is available. The ghosted line reads as
    /// "not recorded", which distinguishes it from the altitude-coloured trail of
    /// a session whose tracks the API returned.
    /// </summary>
    public const int EstimatedTrailAlpha = 140;

    /// <summary>
    /// Draws the path the aircraft has flown, coloured by the altitude of every
    /// point, and marks the airports of the flight plan. The trail comes from the
    /// recorded tracks of the session when <paramref name="tracks"/> is given and
    /// from the flown part of the filed route otherwise — unless
    /// <paramref name="estimateWhenMissing"/> is false, which is what the map uses
    /// while the recorded track is still being fetched: nothing is drawn until
    /// the answer is known, so the line cannot jump from the estimate to the
    /// recorded trail. The airports come from the flight plan, so they are always
    /// drawn.
    /// </summary>
    public static MemoryLayer CreateRoute(
        IvaoPilot pilot,
        MapPalette palette,
        IReadOnlyList<IvaoPilotTrack>? tracks = null,
        bool estimateWhenMissing = true)
    {
        var features = new List<IFeature>();
        AddTrail(
            features,
            FlightTrail.BuildPilotTrail(pilot, tracks, estimateWhenMissing),
            FlightTrail.HasRecordedTrack(tracks));

        var flightPlan = pilot.FlightPlan;
        var color = WorldMap.Parse(palette.Accent);
        if (flightPlan?.Departure?.Position is { } departure)
        {
            features.Add(CreateAirportFeature(departure, flightPlan.DepartureIcao, color, palette));
        }

        if (flightPlan?.Arrival?.Position is { } arrival)
        {
            features.Add(CreateAirportFeature(arrival, flightPlan.ArrivalIcao, color, palette));
        }

        return new MemoryLayer(RouteLayerName)
        {
            Features = features,
            Style = null
        };
    }

    /// <summary>
    /// Traces the recorded track of a station, in the colour of its facility.
    /// A controller session has no altitude, so the whole line shares one colour.
    /// </summary>
    public static MemoryLayer CreateAtcRoute(IvaoAtc atc, IReadOnlyList<IvaoPilotTrack>? tracks = null)
    {
        var features = new List<IFeature>();
        var color = WorldMap.Parse(FacilityPalette.ForFacility(atc.Facility));

        foreach (var line in FlightTrail.BuildLines(tracks))
        {
            var coordinates = line.Select(ToCoordinate).ToList();
            if (coordinates.Count < 2)
            {
                continue;
            }

            features.Add(CreateTrailFeature(coordinates, color));
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

    /// <summary>
    /// Adds one line per run of segments that share an altitude band. Consecutive
    /// segments share their endpoints, so a run becomes a single line instead of
    /// one feature per recorded point.
    /// </summary>
    private static void AddTrail(
        List<IFeature> features,
        IReadOnlyList<TrailSegment> segments,
        bool recorded)
    {
        if (segments.Count == 0)
        {
            return;
        }

        var coordinates = new List<Coordinate>
        {
            ToCoordinate(segments[0].Start),
            ToCoordinate(segments[0].End)
        };
        var band = segments[0].Band;

        for (var index = 1; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment.Band == band && segment.Start == segments[index - 1].End)
            {
                coordinates.Add(ToCoordinate(segment.End));
                continue;
            }

            features.Add(CreateTrailFeature(coordinates, TrailColor(band, recorded)));
            coordinates = [ToCoordinate(segment.Start), ToCoordinate(segment.End)];
            band = segment.Band;
        }

        features.Add(CreateTrailFeature(coordinates, TrailColor(band, recorded)));
    }

    /// <summary>
    /// Altitude colour of a band. The estimated trail keeps the band colour of
    /// the current altitude but is drawn translucent, because there is no
    /// recorded altitude history behind it.
    /// </summary>
    private static Color TrailColor(AltitudeBand band, bool recorded)
    {
        var color = WorldMap.Parse(AltitudePalette.For(band));
        return recorded ? color : WorldMap.WithAlpha(color, EstimatedTrailAlpha);
    }

    private static IFeature CreateTrailFeature(IReadOnlyList<Coordinate> coordinates, Color color)
    {
        var feature = new GeometryFeature { Geometry = new LineString(coordinates.ToArray()) };
        feature.Styles.Add(new VectorStyle
        {
            Line = new Pen(color, TrailWidth)
        });
        return feature;
    }

    private static Coordinate ToCoordinate(GeoCoordinate point)
    {
        var projected = WorldMap.Project(point);
        return new Coordinate(projected.X, projected.Y);
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
