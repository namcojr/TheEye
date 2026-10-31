using System.Collections.Concurrent;
using Mapsui.Styles;

namespace TheEye.Core.Maps;

/// <summary>The glyph drawn for an ATC position that has no sector polygon.</summary>
public enum AtcMarkerShape
{
    /// <summary>Four pointed star, used by ground and delivery positions.</summary>
    Star,

    /// <summary>Disc, used by tower, approach, departure and control positions.</summary>
    Circle
}

/// <summary>
/// A single station glyph: the shape, its fill colour and the scale Mapsui uses
/// to turn the 32x32 vector artwork into screen pixels.
/// </summary>
public sealed record AtcMarkerStyle(AtcMarkerShape Shape, string Fill, double Scale, string SvgSource);

/// <summary>
/// Builds the fallback glyphs for ATC positions that report a coordinate but no
/// polygon (every ground position, most towers and the odd approach sector).
/// Ground gets a four pointed star, tower a red disc and approach/departure a
/// larger blue disc, mirroring the legend of the IVAO web map. The glyphs are
/// inline SVG images so Mapsui rasterises them at any zoom level without extra
/// asset files.
/// </summary>
public static class AtcMarkerStyles
{
    public const string Outline = "#FFFFFF";

    public const string GroundFill = "#F1C40F";
    public const string TowerFill = "#FF4D4F";
    public const string ApproachFill = "#3B82F6";
    public const string ControlFill = "#3498DB";
    public const string InformationFill = "#9B59B6";
    public const string DefaultFill = "#95A5A6";

    /// <summary>Resolution (metres per pixel) at which the badges use their base size.</summary>
    public const double ReferenceResolution = 25000;

    /// <summary>Smallest zoom multiplier, applied when the map is fully zoomed out.</summary>
    public const double MinZoomScale = 0.7;

    /// <summary>Largest zoom multiplier, applied on the deepest zoom level.</summary>
    public const double MaxZoomScale = 2.4;

    private const double ZoomStep = 0.18;
    private const double ZoomQuantisation = 20;
    private const double BaseLabelFontSize = 9;

    private const string SvgSourcePrefix = "svg-content://";
    private const string StarPath =
        "M16 0C17.5 9 23 14.5 32 16C23 17.5 17.5 23 16 32C14.5 23 9 17.5 0 16C9 14.5 14.5 9 16 0Z";

    private static readonly ConcurrentDictionary<string, Image> Images = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the glyph for a station. The facility reported by the API is
    /// preferred; when it is missing or generic the callsign suffix
    /// (<c>EDDB_GND</c>) is used instead. <paramref name="zoom"/> is the
    /// multiplier from <see cref="ZoomScale"/>, so the badges grow with the map.
    /// </summary>
    public static AtcMarkerStyle For(string? facility, string? callsign, double zoom = 1)
    {
        var shape = AtcMarkerShape.Circle;
        var fill = DefaultFill;
        var scale = 0.44;

        switch (ResolveFamily(facility, callsign))
        {
            case AtcMarkerFamily.Ground:
                shape = AtcMarkerShape.Star;
                fill = GroundFill;
                scale = 0.46;
                break;
            case AtcMarkerFamily.Tower:
                shape = AtcMarkerShape.Circle;
                fill = TowerFill;
                scale = 0.5;
                break;
            case AtcMarkerFamily.Approach:
                shape = AtcMarkerShape.Circle;
                fill = ApproachFill;
                scale = 0.62;
                break;
            case AtcMarkerFamily.Control:
                shape = AtcMarkerShape.Circle;
                fill = ControlFill;
                scale = 0.56;
                break;
            case AtcMarkerFamily.Information:
                shape = AtcMarkerShape.Circle;
                fill = InformationFill;
                scale = 0.5;
                break;
        }

        return new AtcMarkerStyle(shape, fill, Math.Round(scale * NormalizeZoom(zoom), 3), CreateSvg(shape, fill));
    }

    /// <summary>
    /// Multiplies the badge and cluster geometry for a viewport resolution. The
    /// badges use their base size on the default continental view, shrink a
    /// little when the whole world is visible and grow up to
    /// <see cref="MaxZoomScale"/> when the user dives into an airport. The
    /// result is quantised so callers can rebuild the station layer only when the
    /// glyphs visibly change size.
    /// </summary>
    public static double ZoomScale(double resolution)
    {
        if (double.IsNaN(resolution) || resolution <= 0)
        {
            return 1;
        }

        var scale = 1 + (ZoomStep * Math.Log2(ReferenceResolution / resolution));
        scale = Math.Clamp(scale, MinZoomScale, MaxZoomScale);
        return Math.Round(scale * ZoomQuantisation) / ZoomQuantisation;
    }

    /// <summary>Callsign font size that keeps the labels in proportion to the badges.</summary>
    public static double LabelFontSize(double zoom) =>
        Math.Round(BaseLabelFontSize * Math.Clamp(NormalizeZoom(zoom), 1, 1.6), 1);

    /// <summary>
    /// Creates the Mapsui style for a marker, sharing the decoded image. The
    /// <paramref name="offset"/> is given in screen pixels (positive Y is down,
    /// like <see cref="LabelStyle"/>), while <see cref="BasePointStyle.Offset"/>
    /// is applied before the symbol is scaled and points upwards, so it is
    /// converted here.
    /// </summary>
    public static ImageStyle CreateImageStyle(AtcMarkerStyle marker, (double X, double Y) offset = default) =>
        new()
        {
            Image = ImageFor(marker),
            SymbolScale = marker.Scale,
            Offset = new Offset(offset.X / marker.Scale, -offset.Y / marker.Scale),
            RotateWithMap = false
        };

    /// <summary>Returns the cached image so repeated refreshes reuse one drawable.</summary>
    public static Image ImageFor(AtcMarkerStyle marker) =>
        Images.GetOrAdd(marker.SvgSource, source => new Image { Source = SvgSourcePrefix + source });

    /// <summary>
    /// Screen radius that still counts as a click on the badge. The artwork is
    /// 32 px wide and scaled by <see cref="AtcMarkerStyle.Scale"/>, plus a few
    /// pixels of slack so the small ground stars stay easy to hit.
    /// </summary>
    public static double HitRadius(AtcMarkerStyle marker) => Math.Max(10, (16 * marker.Scale) + 4);

    /// <summary>
    /// Tower, ground and approach positions of one airport share the airport
    /// coordinate, so their glyphs are fanned out around a small circle to keep
    /// every badge visible. A lone station keeps the exact coordinate. The ring
    /// grows with <paramref name="zoom"/> so the larger badges keep their gap.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> ClusterOffsets(int count, double zoom = 1)
    {
        if (count <= 1)
        {
            return [(0d, 0d)];
        }

        var radius = count switch
        {
            2 => 12d,
            3 => 15d,
            4 => 16d,
            _ => 16d + (4d * (count - 4))
        };

        radius *= NormalizeZoom(zoom);

        var offsets = new (double X, double Y)[count];
        for (var index = 0; index < count; index++)
        {
            var angle = (-Math.PI / 2) + (2 * Math.PI * index / count);
            offsets[index] = (Math.Round(radius * Math.Cos(angle), 1), Math.Round(radius * Math.Sin(angle), 1));
        }

        return offsets;
    }

    /// <summary>
    /// Places the callsign just outside its badge, continuing the cluster
    /// direction so stacked stations keep their labels apart. The returned point
    /// is the label anchor, see <see cref="LabelAlignment"/> for the side the
    /// text is anchored on.
    /// </summary>
    public static (double X, double Y) LabelOffset(AtcMarkerStyle marker, (double X, double Y) offset)
    {
        var distance = Math.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y));
        var reach = (16 * marker.Scale) + 4;
        if (distance < 0.5)
        {
            return (0d, reach);
        }

        var factor = (distance + reach) / distance;
        return (Math.Round(offset.X * factor, 1), Math.Round(offset.Y * factor, 1));
    }

    /// <summary>
    /// Anchors the callsign box on the side facing away from its badge, so the
    /// text grows into the free space instead of back over the glyph.
    /// </summary>
    public static (LabelStyle.HorizontalAlignmentEnum Horizontal, LabelStyle.VerticalAlignmentEnum Vertical) LabelAlignment(
        (double X, double Y) offset)
    {
        const double tolerance = 0.5;
        var horizontal = offset.X > tolerance
            ? LabelStyle.HorizontalAlignmentEnum.Left
            : offset.X < -tolerance
                ? LabelStyle.HorizontalAlignmentEnum.Right
                : LabelStyle.HorizontalAlignmentEnum.Center;
        var vertical = offset.Y > tolerance
            ? LabelStyle.VerticalAlignmentEnum.Top
            : offset.Y < -tolerance
                ? LabelStyle.VerticalAlignmentEnum.Bottom
                : LabelStyle.VerticalAlignmentEnum.Center;
        return (horizontal, vertical);
    }


    /// <summary>Sort key so the same facility always takes the same cluster slot.</summary>
    public static int SortOrder(string? facility, string? callsign) =>
        ResolveFamily(facility, callsign) switch
        {
            AtcMarkerFamily.Ground => 0,
            AtcMarkerFamily.Tower => 1,
            AtcMarkerFamily.Approach => 2,
            AtcMarkerFamily.Control => 3,
            AtcMarkerFamily.Information => 4,
            _ => 5
        };

    public static string CreateSvg(AtcMarkerShape shape, string fill) =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 32 32\">{CreateBody(shape, fill)}</svg>";

    private static string CreateBody(AtcMarkerShape shape, string fill) => shape switch
    {
        AtcMarkerShape.Star =>
            $"<path d=\"{StarPath}\" fill=\"{fill}\" stroke=\"{Outline}\" stroke-width=\"1.6\" stroke-linejoin=\"round\"/>",
        _ =>
            $"<circle cx=\"16\" cy=\"16\" r=\"12.5\" fill=\"{fill}\" stroke=\"{Outline}\" stroke-width=\"2.5\"/>"
    };

    /// <summary>Keeps a caller supplied multiplier inside the supported zoom range.</summary>
    private static double NormalizeZoom(double zoom) =>
        double.IsNaN(zoom) ? 1 : Math.Clamp(zoom, MinZoomScale, MaxZoomScale);

    private static AtcMarkerFamily ResolveFamily(string? facility, string? callsign)
    {
        var family = FamilyOf(facility);
        if (family is not null)
        {
            return family.Value;
        }

        // Callsigns follow <ICAO>_<optional middle>_<facility>, so the last
        // segment that names a known facility wins.
        if (!string.IsNullOrWhiteSpace(callsign))
        {
            var segments = callsign.Split(
                '_',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var index = segments.Length - 1; index >= 0; index--)
            {
                family = FamilyOf(segments[index]);
                if (family is not null)
                {
                    return family.Value;
                }
            }
        }

        return AtcMarkerFamily.Unknown;
    }

    private static AtcMarkerFamily? FamilyOf(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "GND" or "DEL" => AtcMarkerFamily.Ground,
        "TWR" => AtcMarkerFamily.Tower,
        "APP" or "DEP" or "ARR" => AtcMarkerFamily.Approach,
        "CTR" or "FSS" or "CEN" => AtcMarkerFamily.Control,
        "INFO" => AtcMarkerFamily.Information,
        _ => null
    };

    private enum AtcMarkerFamily
    {
        Unknown,
        Ground,
        Tower,
        Approach,
        Control,
        Information
    }
}
