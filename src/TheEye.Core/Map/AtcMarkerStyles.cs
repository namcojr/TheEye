using System.Collections.Concurrent;
using System.Globalization;
using Mapsui.Styles;

namespace TheEye.Core.Maps;

/// <summary>The glyph drawn for an ATC position that has no sector polygon.</summary>
public enum AtcMarkerShape
{
    /// <summary>Four pointed star, used by ground positions.</summary>
    Star,

    /// <summary>
    /// The star turned by 45 degrees, used by delivery positions so a
    /// <c>_DEL</c> badge does not read like a <c>_GND</c> one.
    /// </summary>
    RotatedStar,

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
/// Ground gets a four pointed star, delivery the same star turned by 45
/// degrees, tower a red disc, observer a white disc and approach/departure/arrival
/// a wider blue disc, mirroring the legend of the IVAO web map. Because every
/// position of an airport is placed on the airport coordinate, the glyphs are
/// concentric and a click walks through them.
/// The live map draws screen-space vectors on each frame, with a fixed-width
/// contour. Inline SVG is also available for standalone glyph previews.
/// </summary>
public static class AtcMarkerStyles
{
    public const string GroundFill = "#F1C40F";
    public const string TowerFill = "#FF4D4F";
    public const string ApproachFill = "#3B82F6";
    public const string ControlFill = "#3498DB";
    public const string InformationFill = "#9B59B6";
    public const string ObserverFill = "#FFFFFF";
    public const string DefaultFill = "#95A5A6";

    // ----------------------------------------------------------------------
    // Glyph sizes. Diameters are device-independent screen pixels at the
    // reference zoom. Each facility retains its own size while the map scale
    // changes; the star and circle limits keep the glyphs within a useful
    // screen size at the extremes.
    // ----------------------------------------------------------------------

    /// <summary>Diameter of the delivery star (<c>_DEL</c>), the smallest glyph.</summary>
    public const double DeliveryDiameter = 27;

    /// <summary>Diameter of the ground star (<c>_GND</c>).</summary>
    public const double GroundDiameter = 27;

    /// <summary>Diameter of the tower circle (<c>_TWR</c>); wide enough to enclose the star.</summary>
    public const double TowerDiameter = 45;

    /// <summary>
    /// Diameter of the circle shared by approach, departure and arrival
    /// positions (<c>_APP</c>, <c>_DEP</c>, <c>_ARR</c>) — a little wider than
    /// the tower ring so it shows around it.
    /// </summary>
    public const double ApproachDiameter = 47;

    /// <summary>Diameter of the disc of a control position with no sector polygon.</summary>
    public const double ControlDiameter = 49;

    /// <summary>Diameter of the disc of an information position.</summary>
    public const double InformationDiameter = 43;

    /// <summary>Diameter of the disc drawn when the facility cannot be resolved.</summary>
    public const double DefaultDiameter = 41;

    /// <summary>Diameter of the disc of an observer position (<c>_OBS</c>).</summary>
    public const double ObserverDiameter = 46;

    /// <summary>Smallest allowed screen diameter for either airport-position star.</summary>
    public const double MinStarDiameter = 12;

    /// <summary>Largest allowed screen diameter for either airport-position star.</summary>
    public const double MaxStarDiameter = 550;

    /// <summary>Smallest allowed screen diameter for an airport-position circle.</summary>
    public const double MinCircleDiameter = 16;

    /// <summary>Largest allowed screen diameter for an airport-position circle.</summary>
    public const double MaxCircleDiameter = 580;

    /// <summary>Zoom level at which the facility diameters above are drawn at their configured size.</summary>
    public const int ReferenceZoomLevel = 6;

    /// <summary>
    /// Width in screen pixels of every glyph contour at every zoom.
    /// Only the fill of discs is see-through.
    /// </summary>
    public const double OutlineWidth = 1;

    /// <summary>
    /// Alpha of the disc fill, so the airport stays visible through the badge in
    /// the same way the translucent sector polygons keep the map readable.
    /// <c>255</c> draws a solid disc, <c>0</c> leaves only the contour.
    /// </summary>
    public const int CircleFillAlpha = 56;

    /// <summary>
    /// Multiplier of a ring drawn around a badge: the selection ring, and the
    /// supervisor ring the station layer draws. The ring is a little wider than
    /// the glyph itself so it shows all around it instead of cutting into it.
    /// </summary>
    public const double RingScale = 1.0;

    private const double BaseLabelFontSize = 9;
    private const double ReferenceResolution = WorldMap.MaxResolution / (1 << ReferenceZoomLevel);

    /// <summary>Side of the inline SVG viewBox; a unit is one pixel of the artwork.</summary>
    private const double ArtworkSize = 32;

    /// <summary>
    /// Footprint of a glyph inside the viewBox. The two remaining units leave
    /// room for the contour of the widest badge, so nothing is clipped.
    /// </summary>
    private const double GlyphBox = 30;

    private const string SvgSourcePrefix = "svg-content://";
    /// <summary>The four-point star outline in the 32-unit artwork coordinate system.</summary>
    public const string StarPathData =
        "M16 0C17.5 9 23 14.5 32 16C23 17.5 17.5 23 16 32C14.5 23 9 17.5 0 16C9 14.5 14.5 9 16 0Z";

    private static readonly ConcurrentDictionary<string, Image> Images = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the glyph for a station. The facility reported by the API is
    /// preferred; when it is missing or generic the callsign suffix
    /// (<c>EDDB_GND</c>) is used instead. <paramref name="zoom"/> is the
    /// continuous scale factor from <see cref="ZoomScale"/>.
    /// </summary>
    public static AtcMarkerStyle For(string? facility, string? callsign, double zoom = 1)
    {
        var (shape, fill, diameter) = ResolveGlyph(facility, callsign);

        // The glyph fills GlyphBox units of the artwork, so this draws it at
        // exactly `diameter` screen pixels, contour included, at the reference zoom.
        var scale = diameter / GlyphBox * NormalizeZoom(zoom, shape);
        // Keep the SVG source stable as the map zooms, so image caching remains
        // bounded; the symbol scale carries the continuous zoom change.
        return new AtcMarkerStyle(shape, fill, scale, CreateSvg(shape, fill));
    }

    /// <summary>
    /// Raw multiplier that makes the badges follow the map's world scale. It
    /// doubles for every map zoom level; <see cref="For"/> clamps the resulting
    /// screen size per marker shape.
    /// </summary>
    public static double ZoomScale(double resolution)
    {
        if (!double.IsFinite(resolution) || resolution <= 0)
        {
            return 1;
        }

        return ReferenceResolution / resolution;
    }

    /// <summary>
    /// Callsign font size in screen pixels. Labels deliberately remain the same
    /// size as the map zooms, even though their badges follow the map scale.
    /// </summary>
    public static double LabelFontSize() => BaseLabelFontSize;

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
    public static Image ImageFor(AtcMarkerStyle marker) => ImageFor(marker.SvgSource);

    /// <summary>
    /// Style of the ring drawn around a badge. Two callers share it: the ring
    /// around the station a click selected, and the ring that marks the station
    /// of a member with supervisor rights. The ring is a little wider than the
    /// glyph itself and empty in the middle, so the airport and the glyph stay
    /// visible while the ring shows which position of the concentric stack a
    /// click picked, or that its member watches the network.
    /// </summary>
    public static ImageStyle CreateRingStyle(AtcMarkerStyle marker, string accent)
    {
        var scale = marker.Scale * RingScale;
        var source = CreateRingSvg(accent, 1);
        return new ImageStyle
        {
            Image = ImageFor(source),
            SymbolScale = scale,
            RotateWithMap = false
        };
    }

    private static Image ImageFor(string source) =>
        Images.GetOrAdd(source, key => new Image { Source = SvgSourcePrefix + key });

    /// <summary>
    /// Screen radius that still counts as a click on the badge. The artwork is
    /// 32 px wide and scaled by <see cref="AtcMarkerStyle.Scale"/>, plus a few
    /// pixels of slack so the small ground stars stay easy to hit.
    /// </summary>
    public static double HitRadius(AtcMarkerStyle marker) => Math.Max(10, (16 * marker.Scale) + 4);

    /// <summary>
    /// Anchors a callsign below the badge stack of its airport. Every position of
    /// one airport shares the airport coordinate, so the glyphs are concentric
    /// and only the labels can be spread: they start below the widest glyph and
    /// then move down one line per facility, which keeps every callsign readable
    /// while the badges overlap. The returned point is the label anchor, see
    /// <see cref="LabelAlignment"/> for the side the text is anchored on.
    /// </summary>
    /// <param name="widestScale">Scale of the widest glyph of the stack.</param>
    /// <param name="line">Position of the station inside the stack, smallest facility first.</param>
    public static (double X, double Y) StackedLabelOffset(double widestScale, int line)
    {
        var reach = (16 * widestScale) + 4;
        var lineHeight = BaseLabelFontSize + 3;
        return (0d, Math.Round(reach + (line * lineHeight), 1));
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


    /// <summary>
    /// Sort key of a station inside its airport stack: smallest facility first,
    /// so the glyphs nest in the drawing order the map uses.
    /// </summary>
    public static int SortOrder(string? facility, string? callsign) =>
        ResolveFamily(facility, callsign) switch
        {
            AtcMarkerFamily.Ground => 0,
            AtcMarkerFamily.Tower => 1,
            AtcMarkerFamily.Observer => 2,
            AtcMarkerFamily.Approach => 3,
            AtcMarkerFamily.Control => 4,
            AtcMarkerFamily.Information => 5,
            _ => 6
        };

    /// <summary>
    /// Builds the inline SVG of a glyph. <paramref name="scale"/> is the
    /// multiplier Mapsui draws the artwork with, so the contour is
    /// <see cref="OutlineWidth"/> screen pixels at that scale.
    /// </summary>
    public static string CreateSvg(AtcMarkerShape shape, string fill, double scale = 1) =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 32 32\">{CreateBody(shape, fill, scale)}</svg>";

    private static string CreateBody(AtcMarkerShape shape, string fill, double scale)
    {
        // A user unit is one pixel of the 32 px artwork and the artwork is drawn
        // `scale` screen pixels wide per unit, so this contour shows as one pixel.
        var contour = OutlineWidth / Math.Max(scale, 0.01);

        // Half of the glyph footprint, minus half a contour, so the painted edge
        // lands exactly on the footprint and never leaves the viewBox.
        var reach = (GlyphBox / 2) - (contour / 2);

        if (shape is AtcMarkerShape.Star or AtcMarkerShape.RotatedStar)
        {
            // The star is drawn as a group so its tips can be pulled in by half a
            // contour; delivery positions add another 45 degrees on top.
            var inset = reach / (ArtworkSize / 2);
            var transform = $"translate(16 16) scale({Number(inset)}) translate(-16 -16)";
            if (shape == AtcMarkerShape.RotatedStar)
            {
                transform = $"rotate(45 16 16) {transform}";
            }

            // The group scale multiplies the stroke width too, so it is divided
            // by the inset to still come out as one screen pixel.
            return $"<g transform=\"{transform}\"><path d=\"{StarPathData}\" fill=\"{fill}\" stroke=\"{fill}\" "
                + $"fill-opacity=\"{Number(CircleFillAlpha / 255d)}\" "
                + $"stroke-width=\"{Number(contour / inset)}\" stroke-linejoin=\"round\"/></g>";
        }

        // Only the fill of a disc is see-through; the contour uses the full
        // facility colour so the ring stays readable on the map.
        return $"<circle cx=\"16\" cy=\"16\" r=\"{Number(reach)}\" fill=\"{fill}\" "
            + $"fill-opacity=\"{Number(CircleFillAlpha / 255d)}\" stroke=\"{fill}\" "
            + $"stroke-width=\"{Number(contour)}\"/>";
    }

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Empty ring of the selection marker: a two pixel contour at the supplied
    /// scale around the whole artwork, with nothing painted inside.
    /// </summary>
    private static string CreateRingSvg(string color, double scale)
    {
        var contour = (2 * OutlineWidth) / Math.Max(scale, 0.01);
        var reach = (ArtworkSize / 2) - (contour / 2);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 32 32\">"
            + $"<circle cx=\"16\" cy=\"16\" r=\"{Number(reach)}\" fill=\"none\" stroke=\"{color}\" "
            + $"stroke-width=\"{Number(contour)}\"/></svg>";
    }

    /// <summary>Clamps the continuous map scale to the limits for the marker shape.</summary>
    private static double NormalizeZoom(double zoom, AtcMarkerShape shape)
    {
        if (double.IsNaN(zoom))
        {
            zoom = 1;
        }

        var minimumBaseDiameter = shape is AtcMarkerShape.Star or AtcMarkerShape.RotatedStar
            ? Math.Min(DeliveryDiameter, GroundDiameter)
            : Math.Min(
                DefaultDiameter,
                Math.Min(ObserverDiameter, Math.Min(InformationDiameter, Math.Min(TowerDiameter, Math.Min(ApproachDiameter, ControlDiameter)))));
        var maximumBaseDiameter = shape is AtcMarkerShape.Star or AtcMarkerShape.RotatedStar
            ? Math.Max(DeliveryDiameter, GroundDiameter)
            : Math.Max(
                DefaultDiameter,
                Math.Max(ObserverDiameter, Math.Max(InformationDiameter, Math.Max(TowerDiameter, Math.Max(ApproachDiameter, ControlDiameter)))));
        var minimumDiameter = shape is AtcMarkerShape.Star or AtcMarkerShape.RotatedStar
            ? MinStarDiameter
            : MinCircleDiameter;
        var maximumDiameter = shape is AtcMarkerShape.Star or AtcMarkerShape.RotatedStar
            ? MaxStarDiameter
            : MaxCircleDiameter;

        return Math.Clamp(zoom, minimumDiameter / minimumBaseDiameter, maximumDiameter / maximumBaseDiameter);
    }

    private static (AtcMarkerShape Shape, string Fill, double Diameter) ResolveGlyph(string? facility, string? callsign)
    {
        var token = ResolveToken(facility, callsign);
        return FamilyOf(token) switch
        {
            AtcMarkerFamily.Ground when token == "DEL" =>
                (AtcMarkerShape.RotatedStar, GroundFill, DeliveryDiameter),
            AtcMarkerFamily.Ground => (AtcMarkerShape.Star, GroundFill, GroundDiameter),
            AtcMarkerFamily.Tower => (AtcMarkerShape.Circle, TowerFill, TowerDiameter),
            AtcMarkerFamily.Approach => (AtcMarkerShape.Circle, ApproachFill, ApproachDiameter),
            AtcMarkerFamily.Control => (AtcMarkerShape.Circle, ControlFill, ControlDiameter),
            AtcMarkerFamily.Information => (AtcMarkerShape.Circle, InformationFill, InformationDiameter),
            AtcMarkerFamily.Observer => (AtcMarkerShape.Circle, ObserverFill, ObserverDiameter),
            _ => (AtcMarkerShape.Circle, DefaultFill, DefaultDiameter)
        };
    }

    private static AtcMarkerFamily ResolveFamily(string? facility, string? callsign) =>
        FamilyOf(ResolveToken(facility, callsign)) ?? AtcMarkerFamily.Unknown;

    /// <summary>
    /// The facility token of a station: the reported facility when it names a
    /// known facility, otherwise the last segment of the callsign that does,
    /// because callsigns follow <c>ICAO_[middle]_FACILITY</c>.
    /// </summary>
    private static string? ResolveToken(string? facility, string? callsign)
    {
        if (FamilyOf(facility) is not null)
        {
            return Normalize(facility);
        }

        if (!string.IsNullOrWhiteSpace(callsign))
        {
            var segments = callsign.Split(
                '_',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var index = segments.Length - 1; index >= 0; index--)
            {
                if (FamilyOf(segments[index]) is not null)
                {
                    return Normalize(segments[index]);
                }
            }
        }

        return null;
    }

    private static string? Normalize(string? value) => value?.Trim().ToUpperInvariant();

    private static AtcMarkerFamily? FamilyOf(string? value) => Normalize(value) switch
    {
        "GND" or "DEL" => AtcMarkerFamily.Ground,
        "TWR" => AtcMarkerFamily.Tower,
        "APP" or "DEP" or "ARR" => AtcMarkerFamily.Approach,
        "CTR" or "FSS" or "CEN" => AtcMarkerFamily.Control,
        "INFO" => AtcMarkerFamily.Information,
        "OBS" => AtcMarkerFamily.Observer,
        _ => null
    };

    private enum AtcMarkerFamily
    {
        Unknown,
        Ground,
        Tower,
        Approach,
        Control,
        Information,
        Observer
    }
}
