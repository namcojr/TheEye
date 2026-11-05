using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.Extensions;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using SkiaSharp;
using TheEye.Core.Maps;

namespace TheEye.Rendering;

/// <summary>Shared by the WPF and Avalonia shells; contours are drawn in screen pixels.</summary>
public sealed class AtcGlyphRenderer : ISkiaStyleRenderer, IFeatureSize
{
    private static readonly SKPath StarPath = CreateStarPath();

    private static readonly SKPathEffect ObserverDash = SKPathEffect.CreateDash(
        [(float)AtcMarkerStyles.ObserverDashLength, (float)AtcMarkerStyles.ObserverDashLength], 0);

    // Reused between glyphs. A shell renders on one thread, but the tests render
    // in parallel, so each thread keeps its own pair.
    [ThreadStatic]
    private static SKPaint? _fill;

    [ThreadStatic]
    private static SKPaint? _outline;

    public static void Register() =>
        MapRenderer.RegisterStyleRenderer(typeof(AtcGlyphStyle), new AtcGlyphRenderer());

    public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer, IFeature feature,
        IStyle style, RenderService renderService, long iteration)
    {
        var glyph = (AtcGlyphStyle)style;
        var marker = glyph.MarkerAtResolution(viewport.Resolution);
        var ring = glyph.RingColor is not null;
        var width = (float)(AtcMarkerStyles.OutlineWidth * (ring ? 2 : 1));
        var radius = (float)(ring
            ? 16 * marker.Scale * AtcMarkerStyles.RingScale
            : 15 * marker.Scale);
        var reach = radius - width / 2;

        var opacity = (float)(layer.Opacity * glyph.Opacity);
        var color = WorldMap.Parse(glyph.RingColor ?? marker.Fill);
        var fill = _fill ??= new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
        fill.Color = WorldMap.WithAlpha(color, marker.FillAlpha).ToSkia(opacity);

        // The contour of an observer is dashed, in screen pixels like its width,
        // so the dashed ring stays dashed at every zoom. The supervisor and
        // selection rings stay solid: they mark the member, not the facility.
        var outline = _outline ??= new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        outline.Color = color.ToSkia(opacity);
        outline.StrokeWidth = width;
        outline.PathEffect = marker.Dashed && !ring ? ObserverDash : null;

        var circle = ring || marker.Shape == AtcMarkerShape.Circle;
        using var star = circle ? null : StarAt(reach, marker.Shape);
        feature.CoordinateVisitor((x, y, _) =>
        {
            var (screenX, screenY) = viewport.WorldToScreenXY(x, y);
            canvas.Save();
            canvas.Translate((float)screenX, (float)screenY);
            if (circle)
            {
                if (!ring)
                {
                    canvas.DrawCircle(0, 0, reach, fill);
                }

                canvas.DrawCircle(0, 0, reach, outline);
            }
            else
            {
                canvas.DrawPath(star!, fill);
                canvas.DrawPath(star!, outline);
            }

            canvas.Restore();
        });
        return true;
    }

    private static SKPath StarAt(float reach, AtcMarkerShape shape)
    {
        var scale = SKMatrix.CreateScale(reach / 16, reach / 16);
        var matrix = shape == AtcMarkerShape.RotatedStar
            ? SKMatrix.CreateRotationDegrees(45).PreConcat(scale)
            : scale;
        var path = new SKPath();
        StarPath.Transform(matrix, path);
        return path;
    }

    private static SKPath CreateStarPath()
    {
        var star = SKPath.ParseSvgPathData(AtcMarkerStyles.StarPathData);
        star.Transform(SKMatrix.CreateTranslation(-16, -16));
        return star;
    }

    public bool NeedsFeature => false;

    public double FeatureSize(IStyle style, RenderService renderService, IFeature? feature = null) =>
        Math.Max(AtcMarkerStyles.MaxStarDiameter, AtcMarkerStyles.MaxCircleDiameter)
        * (32d / 30) * Math.Max(1, AtcMarkerStyles.RingScale);
}
