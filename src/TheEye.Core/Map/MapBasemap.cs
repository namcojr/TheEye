using BruTile;
using BruTile.Cache;
using BruTile.Web;
using Mapsui;
using Mapsui.Tiling.Layers;

namespace TheEye.Core.Maps;

/// <summary>
/// One basemap the map can be drawn with: the bundled offline country map, or a
/// raster style streamed from the tile service IVAO's WebEye uses.
/// </summary>
public sealed record MapStyle(string Id, string Label);

/// <summary>
/// Feeds the map its background. <see cref="Offline"/> is the default and paints
/// the bundled Natural Earth map without touching the network. Every other style
/// replaces that fill with raster tiles from IVAO's MapTiler proxy, the one WebEye
/// uses, so a theme can be compared against the map styles the network offers.
/// The country geometry stays in the map underneath and comes back as soon as the
/// style returns to <see cref="Offline"/>.
/// </summary>
public static class MapBasemap
{
    /// <summary>Style id that keeps the bundled map and contacts nothing.</summary>
    public const string Offline = "Offline";

    /// <summary>Name of the tile layer, so a style switch can find and replace it.</summary>
    public const string LayerName = "Basemap";

    /// <summary>Side of one tile in pixels. The proxy serves 512 px raster tiles.</summary>
    public const int TileSize = 512;

    /// <summary>
    /// Half the width of the EPSG:3857 world in metres: the same value the
    /// navigator pans within, so the tiles line up with the offline geometry.
    /// </summary>
    private const double HalfWorld = 20037508.34;

    /// <summary>
    /// The raster styles IVAO's proxy serves. The ids are the style names in that
    /// service's URL, the labels the ones WebEye's own map picker shows.
    /// </summary>
    public static IReadOnlyList<MapStyle> Styles { get; } =
    [
        new(Offline, "Offline"),
        new("basic", "Basic"),
        new("osm-bright", "Bright"),
        new("osm-liberty", "Liberty"),
        new("openstreetmap", "International"),
        new("positron", "Positron"),
        new("voyager", "Voyager"),
        new("dark-matter", "Dark Matter"),
        new("toner", "Toner"),
        new("satellite-hybrid", "Hybrid")
    ];

    /// <summary>The styles are MapTiler renders of OpenStreetMap data; both must be credited.</summary>
    private const string AttributionText = "© MapTiler © OpenStreetMap contributors";

    private const string AttributionUrl = "https://www.openstreetmap.org/copyright";

    /// <summary>
    /// Requests to the proxy have to look like WebEye's: without the referrer it
    /// answers with a redirect instead of a tile, and BruTile refuses to send a
    /// request that carries no application specific user agent.
    /// </summary>
    private const string Referer = "https://webeye.ivao.aero/";

    private const string UserAgent = "TheEye/1.0 (IVAO traffic map, theme preview)";

    /// <summary>Tiles older than this are fetched again, so a style does not drift.</summary>
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromDays(7);

    /// <summary>
    /// Upper bound for one style's tile cache. BruTile's file cache only expires
    /// by age, so without a cap a long-running map would keep every tile it saw.
    /// </summary>
    private const long CacheLimitBytes = 256L * 1024 * 1024;

    private static readonly string DefaultCacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TheEyeIVAO",
        "tilecache");

    /// <summary>
    /// The tile pyramid. A provider level holds 512 px tiles, so it covers exactly
    /// what the app calls one view level deeper: the navigator's resolution ladder
    /// is built for 256 px tiles, hence the extra step in the exponents. Zooming
    /// from view level <c>n</c> therefore requests <c>{z} = n - 1</c>.
    /// </summary>
    public static ITileSchema Schema { get; } = CreateSchema();

    /// <summary>
    /// The tile source of one style, including its on-disk cache. Public so the
    /// URL and headers of a request can be checked without a running map.
    /// </summary>
    public static HttpTileSource CreateTileSource(MapStyle style, string? cacheDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(style);

        var directory = Path.Combine(cacheDirectory ?? DefaultCacheDirectory, style.Id);
        PruneCache(directory);

        return new HttpTileSource(
            Schema,
            UrlFor(style),
            name: style.Label,
            persistentCache: new FileCache(directory, "webp", CacheExpiry),
            attribution: new Attribution(AttributionText, AttributionUrl),
            configureHttpRequestMessage: ConfigureRequest);
    }

    /// <summary>
    /// Draws the map with the given style, replacing any basemap set before. A tile
    /// style hides the bundled country polygons and labels; <see cref="Offline"/>
    /// removes the tiles and brings them back. Nothing the app draws on top moves.
    /// </summary>
    public static void Apply(Map map, string? styleId, string? cacheDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(map);

        foreach (var layer in map.Layers.Where(layer => layer.Name == LayerName).ToArray())
        {
            map.Layers.Remove(layer);
            if (layer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        var style = Find(styleId);
        var offline = string.Equals(style.Id, Offline, StringComparison.Ordinal);
        SetOfflineLayersEnabled(map, offline);

        if (!offline)
        {
            map.Layers.Insert(0, new TileLayer(CreateTileSource(style, cacheDirectory)) { Name = LayerName });
        }

        map.Refresh();
    }

    /// <summary>The style with this id, or the offline map for an id that is not known.</summary>
    public static MapStyle Find(string? styleId) =>
        Styles.FirstOrDefault(style =>
            string.Equals(style.Id, styleId?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? Styles[0];

    /// <summary>True when the id names a style; null, empty and unknown ids do not.</summary>
    public static bool IsKnown(string? styleId) =>
        !string.IsNullOrWhiteSpace(styleId) &&
        Styles.Any(style => string.Equals(style.Id, styleId.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The stored form of a style id: the exact id, or <see cref="Offline"/>.</summary>
    public static string Normalize(string? styleId) => Find(styleId).Id;

    private static TileSchema CreateSchema()
    {
        var schema = new TileSchema
        {
            Name = "TheEye basemap",
            Srs = "EPSG:3857",
            Format = "webp",
            YAxis = YAxis.OSM,
            Extent = new Extent(-HalfWorld, -HalfWorld, HalfWorld, HalfWorld),
            OriginX = -HalfWorld,
            OriginY = HalfWorld
        };

        // View level 3 is the shallowest the navigator offers and 15 the deepest,
        // so tile levels 2 to 14 are the ones actually requested. Levels 0 and 1
        // are kept so the pyramid starts where a tile service expects it to.
        for (var level = 0; level <= WorldMap.DeepestZoomLevel - 1; level++)
        {
            schema.Resolutions[level] = new Resolution(
                level,
                WorldMap.MaxResolution / Math.Pow(2, level + 1),
                tileWidth: TileSize,
                tileHeight: TileSize);
        }

        return schema;
    }

    private static string UrlFor(MapStyle style) =>
        $"https://maps-maptiler.ivao.aero/api/maps/{style.Id}/{{z}}/{{x}}/{{y}}.webp";

    private static void ConfigureRequest(HttpRequestMessage request)
    {
        request.Headers.Referrer = new Uri(Referer);
        request.Headers.UserAgent.ParseAdd(UserAgent);
    }

    private static void SetOfflineLayersEnabled(Map map, bool enabled)
    {
        foreach (var layer in map.Layers)
        {
            if (layer.Name is WorldMap.CountriesLayerName or WorldMap.CountryLabelsLayerName)
            {
                layer.Enabled = enabled;
            }
        }
    }

    /// <summary>
    /// Trims the oldest tiles of one style until the cache fits its allowance.
    /// A cache that cannot be read or trimmed simply keeps growing instead of
    /// keeping the map offline.
    /// </summary>
    private static void PruneCache(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var files = new DirectoryInfo(directory)
                .EnumerateFiles("*.webp", SearchOption.AllDirectories)
                .OrderBy(file => file.LastWriteTimeUtc)
                .ToList();

            var total = files.Sum(file => file.Length);
            foreach (var file in files)
            {
                if (total <= CacheLimitBytes)
                {
                    break;
                }

                try
                {
                    total -= file.Length;
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A tile another instance is reading is worth keeping.
                    total += file.Length;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Never let cache housekeeping keep the map offline.
        }
    }
}
