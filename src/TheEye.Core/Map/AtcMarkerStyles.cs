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
/// degrees, tower a red disc and approach/departure/arrival a wider blue disc,
/// mirroring the legend of the IVAO web map. Because every position of an
/// airport is placed on the airport coordinate, the glyphs are concentric: the
/// stars sit inside the tower disc and the tower disc inside the approach one,
/// so all of them stay visible at the same spot and a click walks through them.
/// The glyphs are inline SVG images, so Mapsui draws them as vectors at any
/// zoom level without extra asset files.
/// </summary>
public static class AtcMarkerStyles
{
    public const string GroundFill = "#F1C40F";
    public const string TowerFill = "#FF4D4F";
    public const string ApproachFill = "#3B82F6";
    public const string ControlFill = "#3498DB";
    public const string InformationFill = "#9B59B6";
    public const string DefaultFill = "#95A5A6";

    // ----------------------------------------------------------------------
    // Glyph sizes. Every diameter is in device independent pixels at zoom 1,
    // i.e. on the default continental view, and every glyph grows together
    // with its zoom multiplier. The badges of one airport are concentric: the
    // delivery and ground stars sit in the middle, the tower circle encloses
    // them and the approach circle encloses the tower one. Only these seven
    // numbers have to change to make a ring wider or narrower.
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

    /// <summary>
    /// Width in screen pixels of every glyph contour. One pixel at any zoom, like
    /// the outline of the sector polygons, and never transparent: only the fill
    /// of the discs is see-through.
    /// </summary>
    public const double OutlineWidth = 1;

    /// <summary>
    /// Alpha of the disc fill, so the airport stays visible through the badge in
    /// the same way the translucent sector polygons keep the map readable.
    /// <c>255</c> draws a solid disc, <c>0</c> leaves only the contour.
    /// </summary>
    public const int CircleFillAlpha = 56;

    /// <summary>
    /// Zoom level at which the badges start to grow, i.e. the level at which they
    /// show their smallest size. It is the zoomed-out limit of the map
    /// (<see cref="WorldMap.MinZoomLevel"/>), so the badges are smallest on the
    /// widest view the user can reach.
    /// </summary>
    public const double MinZoomLevel = WorldMap.MinZoomLevel;

    /// <summary>
    /// Zoom level at which the badges reach their largest size. It is the deepest
    /// level of the map, 17; every view that is zoomed in further keeps that size.
    /// </summary>
    public const double MaxZoomLevel = WorldMap.DeepestZoomLevel;

    /// <summary>
    /// Multiplier of every diameter at <see cref="MinZoomLevel"/> and on every
    /// level zoomed further out — the smallest size the badges can have. It scales
    /// all of the diameters above, so <c>1</c> shows them at their tuned size.
    /// </summary>
    public const double MinZoomScale = 1.0;

    /// <summary>
    /// Multiplier of every diameter at <see cref="MaxZoomLevel"/> and on every
    /// level zoomed in further — the largest size the badges can have.
    /// </summary>
    public const double MaxZoomScale = 5.0;

    /// <summary>
    /// Multiplier of the ring drawn around a selected badge, so it shows all
    /// around the glyph instead of cutting into it.
    /// </summary>
    public const double SelectionRingScale = 1.2;

    private const double ZoomQuantisation = 20;
    private const double BaseLabelFontSize = 9;
    private const double MaxLabelZoom = 1.6;

    /// <summary>Side of the inline SVG viewBox; a unit is one pixel of the artwork.</summary>
    private const double ArtworkSize = 32;

    /// <summary>
    /// Footprint of a glyph inside the viewBox. The two remaining units leave
    /// room for the contour of the widest badge, so nothing is clipped.
    /// </summary>
    private const double GlyphBox = 30;

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
        var (shape, fill, diameter) = ResolveGlyph(facility, callsign);

        // The glyph fills GlyphBox units of the artwork, so this draws it at
        // exactly `diameter` screen pixels, contour included, at zoom 1.
        var scale = Math.Round(diameter / GlyphBox * NormalizeZoom(zoom), 3);
        return new AtcMarkerStyle(shape, fill, scale, CreateSvg(shape, fill, scale));
    }

    /// <summary>
    /// Multiplies the badge geometry for a viewport resolution. The multiplier
    /// follows the <em>zoom level</em> of the map, not the raw resolution: it stays
    /// at <see cref="MinZoomScale"/> from the whole-world view down to
    /// <see cref="MinZoomLevel"/>, rises evenly on every level in between and
    /// stays at <see cref="MaxZoomScale"/> from <see cref="MaxZoomLevel"/> inwards.
    /// The badges therefore shrink while the map is zoomed out and grow while it is
    /// zoomed in, and they never change direction. The result is quantised so
    /// callers rebuild the station layer only when the glyphs visibly change size.
    /// </summary>
    public static double ZoomScale(double resolution)
    {
        if (double.IsNaN(resolution) || resolution <= 0)
        {
            return Quantise(MinZoomScale);
        }

        // Mapsui zooms in powers of two, so the viewport resolution maps one to one
        // onto the zoom level of the map: 0 puts the whole world in the window and
        // DeepestZoomLevel puts one pixel on the apron.
        var level = Math.Log2(WorldMap.MaxResolution / resolution);
        var span = Math.Max(MaxZoomLevel - MinZoomLevel, 0.001);
        var progress = Math.Clamp((level - MinZoomLevel) / span, 0, 1);

        return Quantise(MinZoomScale + ((MaxZoomScale - MinZoomScale) * progress));
    }

    /// <summary>Rounds a multiplier to the step the station layer reacts to.</summary>
    private static double Quantise(double scale) =>
        Math.Round(scale * ZoomQuantisation) / ZoomQuantisation;

    /// <summary>
    /// Callsign font size that keeps the labels in proportion to the badges. The
    /// text grows much slower than the glyphs do, otherwise one airport would
    /// print its callsigns across half the map.
    /// </summary>
    public static double LabelFontSize(double zoom) =>
        Math.Round(BaseLabelFontSize * Math.Clamp(NormalizeZoom(zoom), 1, MaxLabelZoom), 1);

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
    /// Style of the ring drawn around a selected badge. The ring is a little
    /// wider than the glyph itself and empty in the middle, so the airport and
    /// the glyph stay visible while the accent ring shows which position of the
    /// concentric stack a click picked.
    /// </summary>
    public static ImageStyle CreateSelectionRingStyle(AtcMarkerStyle marker, string accent)
    {
        var scale = Math.Round(marker.Scale * SelectionRingScale, 3);
        var source = CreateRingSvg(accent, scale);
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
    /// <param name="zoom">Zoom multiplier the labels are drawn with.</param>
    public static (double X, double Y) StackedLabelOffset(double widestScale, int line, double zoom = 1)
    {
        var reach = (16 * widestScale) + 4;
        var lineHeight = LabelFontSize(zoom) + 3;
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
            AtcMarkerFamily.Approach => 2,
            AtcMarkerFamily.Control => 3,
            AtcMarkerFamily.Information => 4,
            _ => 5
        };

    /// <summary>
    /// Builds the inline SVG of a glyph. <paramref name="scale"/> is the
    /// multiplier Mapsui draws the artwork with, so the contour can be kept at
    /// <see cref="OutlineWidth"/> screen pixels however large the badge gets.
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
            return $"<g transform=\"{transform}\"><path d=\"{StarPath}\" fill=\"{fill}\" stroke=\"{fill}\" "
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
    /// Empty ring of the selection marker: a two pixel contour around the whole
    /// artwork, i.e. just outside the widest glyph, with nothing painted inside.
    /// </summary>
    private static string CreateRingSvg(string color, double scale)
    {
        var contour = (2 * OutlineWidth) / Math.Max(scale, 0.01);
        var reach = (ArtworkSize / 2) - (contour / 2);
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\" viewBox=\"0 0 32 32\">"
            + $"<circle cx=\"16\" cy=\"16\" r=\"{Number(reach)}\" fill=\"none\" stroke=\"{color}\" "
            + $"stroke-width=\"{Number(contour)}\"/></svg>";
    }

    /// <summary>Keeps a caller supplied multiplier inside the supported zoom range.</summary>
    private static double NormalizeZoom(double zoom) =>
        double.IsNaN(zoom) ? 1 : Math.Clamp(zoom, MinZoomScale, MaxZoomScale);

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
