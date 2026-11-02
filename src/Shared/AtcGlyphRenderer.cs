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
        using var path = new SKPath();
        if (ring || marker.Shape == AtcMarkerShape.Circle)
        {
            path.AddCircle(0, 0, reach);
        }
        else
        {
            using var star = SKPath.ParseSvgPathData(AtcMarkerStyles.StarPathData);
            star.Transform(SKMatrix.CreateTranslation(-16, -16));
            star.Transform(SKMatrix.CreateScale(reach / 16, reach / 16));
            if (marker.Shape == AtcMarkerShape.RotatedStar)
            {
                star.Transform(SKMatrix.CreateRotationDegrees(45));
            }

            path.AddPath(star);
        }

        var opacity = (float)(layer.Opacity * glyph.Opacity);
        var color = WorldMap.Parse(glyph.RingColor ?? marker.Fill);

        // The contour of an observer is dashed, in screen pixels like its width,
        // so the dashed ring stays dashed at every zoom.
        using var dashEffect = marker.Dashed
            ? SKPathEffect.CreateDash(
                [(float)AtcMarkerStyles.ObserverDashLength, (float)AtcMarkerStyles.ObserverDashLength], 0)
            : null;
        using var fill = new SKPaint
        {
            Color = WorldMap.WithAlpha(color, marker.FillAlpha).ToSkia(opacity),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var outline = new SKPaint
        {
            Color = color.ToSkia(opacity),
            StrokeWidth = width,
            Style = SKPaintStyle.Stroke,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            PathEffect = dashEffect
        };
        feature.CoordinateVisitor((x, y, _) =>
        {
            var point = viewport.WorldToScreen(new MPoint(x, y));
            canvas.Save();
            try
            {
                canvas.Translate((float)point.X, (float)point.Y);
                if (!ring)
                {
                    canvas.DrawPath(path, fill);
                }

                canvas.DrawPath(path, outline);
            }
            finally
            {
                canvas.Restore();
            }
        });
        return true;
    }

    public bool NeedsFeature => false;

    public double FeatureSize(IStyle style, RenderService renderService, IFeature? feature = null) =>
        Math.Max(AtcMarkerStyles.MaxStarDiameter, AtcMarkerStyles.MaxCircleDiameter)
        * (32d / 30) * Math.Max(1, AtcMarkerStyles.RingScale);
}
