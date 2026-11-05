using System.Runtime.CompilerServices;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.Extensions;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using SkiaSharp;
using TheEye.Core.Maps;

namespace TheEye.Rendering;

/// <summary>
/// Draws polygons from a path built once in world coordinates, moved onto the
/// screen by the canvas matrix. Mapsui's own renderer clips every vertex in
/// managed code and caches the outcome per viewport, so a map that moves pays for
/// the whole country table again on every frame and leaves tens of megabytes of
/// paths behind it. Only the matrix changes here, and the vertices a zoom level
/// cannot show are dropped from the path that level uses.
/// </summary>
/// <remarks>
/// Skia's vertices and matrices are single precision. At the deepest zooms the
/// outline of a continent lies millions of pixels away, where that is a visible
/// fraction of a pixel, so from <see cref="FinestResolution"/> down the stock
/// renderer takes over: by then few polygons are in view and double precision
/// clipping is what the zoom needs. Everything else the stock renderer handles (lines, points, patterned fills,
/// dashed contours) goes to it as well.
/// </remarks>
public sealed class CachedPolygonRenderer : ISkiaStyleRenderer, IFeatureSize
{
    /// <summary>Metres per pixel below which single precision is no longer exact enough.</summary>
    private const double FinestResolution = 40;

    /// <summary>One path per doubling of the resolution, up to the most zoomed out view.</summary>
    private const int DetailLevels = 10;

    /// <summary>How far, in pixels, a dropped vertex may lie from the kept outline.</summary>
    private const double Tolerance = 0.3;

    /// <summary>
    /// How far, in pixels at the sharpest zoom a path serves, a country cut at the
    /// date line is filled past it. Land is opaque, so the overlap does not show.
    /// </summary>
    private const double DateLineOverlap = 2;

    private static readonly ConditionalWeakTable<IFeature, PolygonPaths> Paths = new();

    [ThreadStatic]
    private static SKPaint? _fill;

    [ThreadStatic]
    private static SKPaint? _outline;

    private readonly VectorStyleRenderer _stock = new();

    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature,
        IStyle style, RenderService renderService, long iteration)
    {
        var vector = (VectorStyle)style;
        if (viewport.Resolution < FinestResolution ||
            viewport.Rotation != 0 ||
            feature is not GeometryFeature { Geometry: Polygon or MultiPolygon } shape ||
            !IsPlain(vector))
        {
            return _stock.Draw(canvas, viewport, layer, feature, style, renderService, iteration);
        }

        var paths = Paths.GetValue(feature, _ =>
            new PolygonPaths(shape.Geometry!, feature[WorldMap.CutAtDateLineField] is true));
        var resolution = viewport.Resolution;
        var detail = paths.At(resolution);

        // RenderFeature wraps every style in a save and restore of the canvas.
        var scale = (float)(1 / resolution);
        canvas.Translate(
            (float)((paths.OriginX - viewport.CenterX) / resolution + viewport.Width / 2),
            (float)((viewport.CenterY - paths.OriginY) / resolution + viewport.Height / 2));
        canvas.Scale(scale);

        var opacity = (float)(layer.Opacity * style.Opacity);
        if (vector.Fill?.Color is { A: > 0 } fillColor)
        {
            var fill = _fill ??= new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            fill.Color = fillColor.ToSkia(opacity);
            canvas.DrawPath(detail.Fill, fill);
        }

        if (vector.Outline is { Width: > 0, Color.A: > 0 } pen)
        {
            var outline = _outline ??= new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true };
            outline.Color = pen.Color.ToSkia(opacity);
            outline.StrokeWidth = (float)(pen.Width * resolution);
            outline.StrokeCap = pen.PenStrokeCap switch
            {
                PenStrokeCap.Round => SKStrokeCap.Round,
                PenStrokeCap.Square => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt
            };
            outline.StrokeJoin = pen.StrokeJoin switch
            {
                StrokeJoin.Miter => SKStrokeJoin.Miter,
                StrokeJoin.Bevel => SKStrokeJoin.Bevel,
                _ => SKStrokeJoin.Round
            };
            outline.StrokeMiter = pen.StrokeMiterLimit;
            canvas.DrawPath(detail.Outline, outline);
        }

        return true;
    }

    /// <summary>A solid fill and a solid contour, the only kinds a path here can paint.</summary>
    private static bool IsPlain(VectorStyle style) =>
        (style.Fill is null || (style.Fill.FillStyle == FillStyle.Solid && style.Fill.Image is null)) &&
        style.Outline is null or { PenStyle: PenStyle.Solid };

    public bool NeedsFeature => ((IFeatureSize)_stock).NeedsFeature;

    public double FeatureSize(IStyle style, RenderService renderService, IFeature? feature = null) =>
        ((IFeatureSize)_stock).FeatureSize(style, renderService, feature);

    /// <summary>
    /// The paths of one polygon, relative to the middle of its bounding box: that
    /// keeps the numbers small, which is what single precision needs, and leaves
    /// a uniform scale as the whole transform because Y already points down.
    /// </summary>
    /// <remarks>
    /// A country cut along the date line meets itself there once the map wraps
    /// around. Its contour leaves that edge out, and its fill runs slightly past it,
    /// because two antialiased edges on the same pixel would let the water show
    /// through the land.
    /// </remarks>
    private sealed class PolygonPaths
    {
        private readonly Geometry _geometry;
        private readonly bool _cutAtDateLine;
        private readonly PolygonDetail?[] _levels = new PolygonDetail?[DetailLevels];

        public PolygonPaths(Geometry geometry, bool cutAtDateLine)
        {
            _geometry = geometry;
            _cutAtDateLine = cutAtDateLine;
            var extent = geometry.EnvelopeInternal;
            OriginX = (extent.MinX + extent.MaxX) / 2;
            OriginY = (extent.MinY + extent.MaxY) / 2;
        }

        public double OriginX { get; }

        public double OriginY { get; }

        public PolygonDetail At(double resolution)
        {
            var level = Math.Clamp((int)Math.Log2(resolution / FinestResolution), 0, DetailLevels - 1);
            return _levels[level] ??= Build(FinestResolution * (1 << level));
        }

        private PolygonDetail Build(double resolution)
        {
            // Even-odd punches the holes whichever way a ring happens to be wound.
            var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            var contour = _cutAtDateLine ? new SKPath() : path;
            var tolerance = resolution * Tolerance;
            var overlap = resolution * DateLineOverlap;
            for (var index = 0; index < _geometry.NumGeometries; index++)
            {
                if (_geometry.GetGeometryN(index) is not Polygon polygon)
                {
                    continue;
                }

                AddRing(path, contour, polygon.ExteriorRing, tolerance, overlap);
                foreach (var hole in polygon.InteriorRings)
                {
                    AddRing(path, contour, hole, tolerance, overlap);
                }
            }

            return new PolygonDetail(path, contour);
        }

        private void AddRing(SKPath path, SKPath contour, LineString ring, double tolerance, double overlap)
        {
            var coordinates = ring.Coordinates;
            if (coordinates.Length < 2)
            {
                return;
            }

            var kept = new List<Coordinate>(Math.Min(coordinates.Length, 256));
            var toleranceSquared = tolerance * tolerance;
            var last = coordinates[0];
            kept.Add(last);
            for (var index = 1; index < coordinates.Length; index++)
            {
                var candidate = coordinates[index];
                var dx = candidate.X - last.X;
                var dy = candidate.Y - last.Y;
                if (dx * dx + dy * dy >= toleranceSquared)
                {
                    kept.Add(candidate);
                    last = candidate;
                }
            }

            // An island too small to have an outline at this zoom stays as a dot
            // of contour instead of vanishing from the coast.
            if (kept.Count == 1)
            {
                kept.Add(coordinates[coordinates.Length / 2]);
            }

            var points = kept.ConvertAll(coordinate => ToLocal(coordinate, overlap)).ToArray();
            path.AddPoly(points, close: true);
            if (_cutAtDateLine)
            {
                AddContour(contour, kept, points);
            }
        }

        /// <summary>The ring as lines, stopping wherever it runs along the date line.</summary>
        private static void AddContour(SKPath contour, List<Coordinate> kept, SKPoint[] points)
        {
            var drawing = false;
            for (var index = 0; index < kept.Count; index++)
            {
                var next = (index + 1) % kept.Count;
                if (WorldWrap.IsOnDateLine(kept[index].X) && kept[index].X * kept[next].X > 0 &&
                    WorldWrap.IsOnDateLine(kept[next].X))
                {
                    drawing = false;
                    continue;
                }

                if (!drawing)
                {
                    contour.MoveTo(points[index]);
                    drawing = true;
                }

                contour.LineTo(points[next]);
            }
        }

        private SKPoint ToLocal(Coordinate coordinate, double overlap)
        {
            var x = coordinate.X;
            if (_cutAtDateLine && WorldWrap.IsOnDateLine(x))
            {
                x += Math.Sign(x) * overlap;
            }

            return new SKPoint((float)(x - OriginX), (float)(OriginY - coordinate.Y));
        }
    }

    /// <summary>One zoom level of a polygon; both are the same path unless the country is cut.</summary>
    private sealed record PolygonDetail(SKPath Fill, SKPath Outline);
}
