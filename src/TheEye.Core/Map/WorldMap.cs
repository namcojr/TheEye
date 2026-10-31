using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using System.Text.Json;
using TheEye.Core.Models;

namespace TheEye.Core.Maps;

/// <summary>
/// Builds the fully offline world map from the Natural Earth country data that
/// ships as an embedded resource. No tile server is contacted at runtime.
/// </summary>
public static class WorldMap
{
    public const string CountriesLayerName = "Countries";
    public const string CountryLabelsLayerName = "Country labels";

    /// <summary>Resolution of the most zoomed out level, i.e. the whole world in one tile.</summary>
    public const double MaxResolution = 156543.034;

    /// <summary>
    /// Shallowest zoom level the map allows. Level 4 shows about a third of the
    /// world in a normal window; levels 0 to 3, which reach from a continent to the
    /// whole world, are not offered by the navigator at all. Set this to 3 to allow
    /// one more step out, or to 0 to allow the whole world again.
    /// </summary>
    public const int MinZoomLevel = 3;

    /// <summary>
    /// Resolution of <see cref="MinZoomLevel"/>: the most zoomed out the map can get,
    /// i.e. the upper limit of the navigator's zoom bounds.
    /// </summary>
    public const double MaxZoomOutResolution = MaxResolution / (1 << MinZoomLevel);

    /// <summary>
    /// Deepest zoom level. Level 17 puts one pixel at roughly 1.2 m, which is
    /// close enough to tell parked aircraft on an apron apart.
    /// </summary>
    public const int DeepestZoomLevel = 17;

    /// <summary>Resolution of <see cref="DeepestZoomLevel"/>.</summary>
    public const double MinResolution = MaxResolution / (1 << DeepestZoomLevel);

    private static readonly Lazy<(IFeature[] Borders, IFeature[] Labels)> Countries = new(LoadCountries);

    public static Map Create(MapPalette palette, GeoCoordinate center, double resolution)
    {
        var map = new Map { CRS = "EPSG:3857" };
        map.Widgets.Clear();
        map.Navigator.RotationLock = true;
        map.Navigator.OverrideResolutions = Enumerable.Range(MinZoomLevel, DeepestZoomLevel - MinZoomLevel + 1)
            .Select(level => MaxResolution / Math.Pow(2, level))
            .ToArray();
        map.Navigator.OverrideZoomBounds = new MMinMax(MinResolution, MaxZoomOutResolution);
        map.Navigator.OverridePanBounds = new MRect(-20037508.34, -20037508.34, 20037508.34, 20037508.34);
        map.Navigator.CenterOnAndZoomTo(
            Project(center),
            Math.Clamp(resolution, MinResolution, MaxZoomOutResolution));

        map.Layers.Add(new MemoryLayer(CountriesLayerName)
        {
            Features = Countries.Value.Borders
        });

        map.Layers.Add(new MemoryLayer(CountryLabelsLayerName)
        {
            Features = Countries.Value.Labels.Select(feature => new PointFeature((PointFeature)feature)).ToArray(),
            SortFeatures = features => features.OrderBy(feature => feature["LabelRank"] is int rank ? rank : 6)
        });

        ApplyTheme(map, palette);
        return map;
    }

    public static void ApplyTheme(Map map, MapPalette palette)
    {
        var water = Parse(palette.Water);
        var land = Parse(palette.Land);
        var border = Parse(palette.Border);
        var label = Parse(palette.Label);

        map.BackColor = water;

        if (map.Layers.FirstOrDefault(layer => layer.Name == CountriesLayerName) is MemoryLayer borderLayer)
        {
            borderLayer.Style = new VectorStyle
            {
                Fill = new Brush(land),
                Outline = new Pen(border, 0.8)
            };
        }

        if (map.Layers.FirstOrDefault(layer => layer.Name == CountryLabelsLayerName) is MemoryLayer labelLayer)
        {
            labelLayer.Style = null;
            foreach (var feature in labelLayer.Features)
            {
                var rank = feature["LabelRank"] is int labelRank ? labelRank : 6;
                feature.Styles.Clear();
                feature.Styles.Add(new LabelStyle
                {
                    LabelColumn = "Name",
                    Font = new Font { FontFamily = "Segoe UI", Size = 11 },
                    ForeColor = label,
                    BackColor = null,
                    Halo = new Pen(land, 2),
                    CollisionDetection = true,
                    MaxVisible = rank <= 2 ? 80000 : rank == 3 ? 24000 : rank == 4 ? 12000 : rank == 5 ? 6000 : 2500
                });
            }
        }

        map.Refresh();
    }

    public static MPoint Project(GeoCoordinate coordinate) =>
        Project(coordinate.Latitude, coordinate.Longitude);

    public static MPoint Project(double latitude, double longitude)
    {
        var point = SphericalMercator.FromLonLat(longitude, Math.Clamp(latitude, -85.05112878, 85.05112878));
        return new MPoint(point.x, point.y);
    }

    public static GeoCoordinate Unproject(double x, double y)
    {
        var lonLat = SphericalMercator.ToLonLat(x, y);
        return new GeoCoordinate(lonLat.lat, lonLat.lon);
    }

    public static Color Parse(string? hex, string fallback = "#808080")
    {
        try
        {
            return Color.FromString(string.IsNullOrWhiteSpace(hex) ? fallback : hex);
        }
        catch
        {
            return Color.FromString(fallback);
        }
    }

    public static Color WithAlpha(Color color, int alpha) =>
        new(color.R, color.G, color.B, Math.Clamp(alpha, 0, 255));

    /// <summary>
    /// Blends two colours: 0 keeps <paramref name="from"/>, 1 gives
    /// <paramref name="to"/>. The airport surfaces derive from the theme this way,
    /// so every palette gets a readable runway without a colour of its own.
    /// </summary>
    public static Color Mix(Color from, Color to, double amount)
    {
        var ratio = Math.Clamp(amount, 0, 1);
        return new Color(
            (int)Math.Round(from.R + (to.R - from.R) * ratio),
            (int)Math.Round(from.G + (to.G - from.G) * ratio),
            (int)Math.Round(from.B + (to.B - from.B) * ratio));
    }

    private static (IFeature[] Borders, IFeature[] Labels) LoadCountries()
    {
        using var stream = typeof(WorldMap).Assembly.GetManifestResourceStream("TheEye.Core.Resources.Maps.countries.geojson")
            ?? throw new InvalidOperationException("The bundled country map is missing.");
        using var document = JsonDocument.Parse(stream);

        var borders = new List<IFeature>();
        var labels = new List<IFeature>();

        foreach (var country in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var geometry = country.GetProperty("geometry");
            var coordinates = geometry.GetProperty("coordinates");
            Geometry shape = geometry.GetProperty("type").GetString() switch
            {
                "Polygon" => ReadPolygon(coordinates),
                "MultiPolygon" => new MultiPolygon(coordinates.EnumerateArray().Select(ReadPolygon).ToArray()),
                _ => throw new InvalidOperationException("Unsupported country geometry.")
            };

            borders.Add(new GeometryFeature { Geometry = shape });

            var properties = country.GetProperty("properties");
            var label = new PointFeature(Project(
                properties.GetProperty("LABEL_Y").GetDouble(),
                properties.GetProperty("LABEL_X").GetDouble()));
            label["Name"] = properties.GetProperty("NAME_EN").GetString();
            label["LabelRank"] = properties.GetProperty("LABELRANK").GetInt32();
            labels.Add(label);
        }

        return (borders.ToArray(), labels.ToArray());
    }

    private static Polygon ReadPolygon(JsonElement coordinates)
    {
        var rings = coordinates.EnumerateArray().Select(ring => new LinearRing(ring.EnumerateArray().Select(coordinate =>
        {
            var point = Project(coordinate[1].GetDouble(), coordinate[0].GetDouble());
            return new Coordinate(point.X, point.Y);
        }).ToArray())).ToArray();

        return new Polygon(rings[0], rings.Skip(1).ToArray());
    }
}
