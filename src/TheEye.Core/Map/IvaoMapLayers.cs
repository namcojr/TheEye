using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using TheEye.Core.Models;
using TheEye.Core.Reference;

namespace TheEye.Core.Maps;

public static class IvaoMapLayers
{
    public const string AtcRegionsLayerName = "IVAO sectors";
    public const string AtcLabelsLayerName = "IVAO sector labels";
    public const string AtcStationsLayerName = "IVAO ATC stations";
    public const string RouteLayerName = "Route";
    public const string SelectedAtcLayerName = "Selected ATC";
    public const string AirportsLayerName = "Airports";

    /// <summary>
    /// Zoom level at which the runway pavement and the ICAO codes of the airports
    /// come into view. Higher levels frame a whole region, where a few thousand
    /// metres of asphalt is a sub-pixel smudge; from here on a runway is wide
    /// enough to read and tells the user which airport sits under the aircraft.
    /// </summary>
    public const int RunwayZoomLevel = 12;

    /// <summary>Resolution of <see cref="RunwayZoomLevel"/>, i.e. when runways start being drawn.</summary>
    public static double RunwayResolution => WorldMap.MaxResolution / Math.Pow(2, RunwayZoomLevel);

    /// <summary>True when the zoom is close enough for the airport pavement to be drawn.</summary>
    public static bool ShowsRunways(double resolution) => resolution <= RunwayResolution;

    /// <summary>
    /// One station badge and where it is drawn: the projected airport
    /// coordinate, the offset of the callsign below the badge stack and the
    /// glyph itself. Every position of an airport shares the coordinate, so the
    /// badges are drawn as concentric rings around the airport and only the
    /// labels are stacked underneath.
    /// </summary>
    public sealed record AtcStationPlacement(
        IvaoAtc Station,
        MPoint Point,
        double LabelOffsetY,
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
            // The badges are concentric, so the labels only move down; the
            // stacked line of the station decides how far.
            var labelOffsetY = placement.LabelOffsetY;
            var labelAlignment = AtcMarkerStyles.LabelAlignment((0d, labelOffsetY));

            var feature = new PointFeature(placement.Point);
            feature["Callsign"] = placement.Station.Callsign;
            feature["Frequency"] = placement.Station.FrequencyText;
            feature.Styles.Add(AtcMarkerStyles.CreateImageStyle(placement.Marker));
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
                Offset = new Offset(0, labelOffsetY),
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
    /// Works out where every station badge is drawn: the projected airport
    /// coordinate plus the callsign line that keeps the labels of one airport
    /// apart. The station layer and the click handler share these numbers, so a
    /// badge is always hit exactly where it is rendered.
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
        foreach (var stack in stations.GroupBy(
            station => (X: Math.Round(station.Point.X), Y: Math.Round(station.Point.Y))))
        {
            // Smallest facility first: the ground star is drawn inside the tower
            // ring, so it has to be laid out — and clicked — before the wider
            // facilities around it.
            var members = stack
                .Select(member => new
                {
                    member.Station,
                    member.Point,
                    Marker = AtcMarkerStyles.For(member.Station.Facility, member.Station.Callsign, zoom)
                })
                .OrderBy(member => AtcMarkerStyles.SortOrder(member.Station.Facility, member.Station.Callsign))
                .ThenBy(member => member.Station.Callsign, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // The widest glyph of the stack decides where the labels start, so
            // none of them lands on a ring of the airport.
            var widestScale = members.Max(member => member.Marker.Scale);

            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                placements.Add(new AtcStationPlacement(
                    member.Station,
                    member.Point,
                    AtcMarkerStyles.StackedLabelOffset(widestScale, index, zoom).Y,
                    member.Marker));
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
    /// Draws the pavement of every runway of the given airports, plus the ICAO
    /// code of each of them. The strip is a real rectangle built from the two
    /// thresholds and the published width, so it points the right way and scales
    /// with the map. <paramref name="resolution"/> is the resolution of the map
    /// being drawn: the narrowest runway is widened to a little over a pixel, so a
    /// small airfield stays visible at the zoom where runways appear at all.
    /// </summary>
    public static MemoryLayer CreateAirports(
        IEnumerable<AirportRecord> airports,
        MapPalette palette,
        double resolution)
    {
        var features = new List<IFeature>();
        var land = WorldMap.Parse(palette.Land);
        var label = WorldMap.Parse(palette.Label);

        // No palette member for the pavement: blending the land towards the label
        // colour gives a strip that reads as asphalt on every theme, dark or light.
        var surface = WorldMap.Mix(land, label, palette.IsDark ? 0.5 : 0.55);
        var pavement = new Pen(WorldMap.Parse(palette.Border), 0.8);
        var minimumWidth = Math.Max(resolution * 1.6, 25);

        foreach (var airport in airports)
        {
            foreach (var runway in airport.Runways)
            {
                var geometry = CreateRunway(runway, Math.Max(runway.WidthMetres, minimumWidth));
                if (geometry is null)
                {
                    continue;
                }

                var feature = new GeometryFeature { Geometry = geometry };
                feature["Icao"] = airport.Icao;
                feature.Styles.Add(new VectorStyle
                {
                    Fill = new Brush(surface),
                    Outline = pavement
                });
                features.Add(feature);
            }

            var code = new PointFeature(WorldMap.Project(airport.Position));
            code["Icao"] = airport.Icao;
            code.Styles.Add(new LabelStyle
            {
                LabelColumn = "Icao",
                Font = new Font { FontFamily = "Segoe UI", Size = 10, Bold = true },
                ForeColor = label,
                BackColor = null,
                Halo = new Pen(land, 2.5),
                Offset = new Offset(0, -16),
                CollisionDetection = true
            });
            features.Add(code);
        }

        return new MemoryLayer(AirportsLayerName)
        {
            Features = features,
            Style = null
        };
    }

    /// <summary>
    /// Turns a runway into a rectangle in map coordinates: both thresholds are
    /// projected and offset by half the width along the perpendicular. Web Mercator
    /// stretches a metre by 1/cos(latitude), so the width is scaled the same way to
    /// keep the strip as wide on the ground as it is published.
    /// </summary>
    private static Polygon? CreateRunway(AirportRunway runway, double widthMetres)
    {
        var start = WorldMap.Project(runway.Threshold);
        var end = WorldMap.Project(runway.End);
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length < 1)
        {
            return null;
        }

        var latitude = runway.Threshold.Latitude * Math.PI / 180;
        var stretch = 1 / Math.Max(Math.Cos(latitude), 0.2);
        var halfWidth = widthMetres * stretch / 2;
        var offsetX = -deltaY / length * halfWidth;
        var offsetY = deltaX / length * halfWidth;

        return new Polygon(new LinearRing(
        [
            new Coordinate(start.X + offsetX, start.Y + offsetY),
            new Coordinate(end.X + offsetX, end.Y + offsetY),
            new Coordinate(end.X - offsetX, end.Y - offsetY),
            new Coordinate(start.X - offsetX, start.Y - offsetY),
            new Coordinate(start.X + offsetX, start.Y + offsetY)
        ]));
    }

    /// <summary>
    /// Highlights the selected position: the sector outline for a control
    /// position that reports a polygon, or an accent ring around the badge of a
    /// station that is drawn as a glyph. The ring is what tells the user which
    /// position of a stack of concentric badges a click picked.
    /// </summary>
    public static MemoryLayer CreateSelectedAtc(IvaoAtc atc, MapPalette palette, double zoom = 1)
    {
        var accent = WorldMap.Parse(palette.Accent);
        var features = new List<IFeature>();
        if (CreatePolygon(atc) is { } geometry)
        {
            var feature = new GeometryFeature { Geometry = geometry };
            feature.Styles.Add(new VectorStyle
            {
                Fill = new Brush(new Color(0, 0, 0, 0)),
                Outline = new Pen(accent, 3.2)
            });
            features.Add(feature);
        }
        else if (atc.Position is { } position && position.IsValid)
        {
            var feature = new PointFeature(WorldMap.Project(position));
            feature.Styles.Add(AtcMarkerStyles.CreateSelectionRingStyle(
                AtcMarkerStyles.For(atc.Facility, atc.Callsign, zoom),
                palette.Accent));
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
