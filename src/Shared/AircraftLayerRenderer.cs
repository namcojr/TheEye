using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using SkiaSharp;
using TheEye.Core.Maps;

namespace TheEye.Rendering;

/// <summary>
/// Paints every aircraft in one pass of the map's render. Positions are worked
/// out here from the viewport of the frame being drawn, so pin and map can never
/// disagree. Each rotated pin is rasterised once into a sprite and stamped from
/// then on: 3000 vector silhouettes and their shadows take about 20 ms on the
/// software canvas WPF uses, the same 3000 sprites take about 2.5 ms.
/// </summary>
public sealed class AircraftLayerRenderer
{
    /// <summary>The callsign size the map shows at a font scale of 1, in device independent pixels.</summary>
    private const float LabelFontSize = 10;

    /// <summary>How far the drop shadow sits under the pin, in device independent pixels.</summary>
    private const float ShadowOffset = 1.5f;

    /// <summary>The opacity of an aircraft on the ground.</summary>
    private const float GroundOpacity = 0.72f;

    /// <summary>Side of the artwork, the viewBox of the aircraft SVG files.</summary>
    private const float ArtworkSize = AircraftIconCatalog.ViewBoxSize;

    /// <summary>
    /// Extra room around a pin: a rotated square needs up to √2 of its side, and the
    /// shadow and the anti-aliased edge need a little more.
    /// </summary>
    private const float SpriteMargin = 1.45f;

    /// <summary>Room a callsign may stick out past the edge of the map and still show.</summary>
    private const float CullMargin = 120;

    /// <summary>The sprites are dropped when they outgrow this, and rebuilt as needed.</summary>
    private const long SpriteBudgetBytes = 256L * 1024 * 1024;

    /// <summary>The callsigns kept shaped; connections come and go during a long session.</summary>
    private const int LabelLimit = 16384;

    private static readonly Dictionary<AircraftGlyph, SKPath> Silhouettes = [];

    private static readonly SKSamplingOptions SubPixel = new(SKFilterMode.Linear);

    private readonly Dictionary<SpriteKey, SKImage> _sprites = [];
    private readonly Dictionary<string, Label> _labels = new(StringComparer.Ordinal);
    private readonly SKPaint _spritePaint = new();
    private readonly SKPaint _labelPaint = new() { IsAntialias = true };
    private readonly object _renderLock = new();
    private long _spriteBytes;
    private SKFont? _labelFont;
    private SKTypeface? _labelTypeface;
    private float _labelFontSize;
    private uint _shadowColor;

    /// <summary>Hands the layer to the one renderer; registered by <see cref="MapRenderers.Register"/>.</summary>
    public void Render(SKCanvas canvas, Viewport viewport, ILayer layer, RenderService renderService)
    {
        if (layer is not AircraftLayer aircraft)
        {
            return;
        }

        // A shell has one render thread. The lock is for the tests, which render
        // on several, and it costs nothing when nobody is waiting for it.
        lock (_renderLock)
        {
            Draw(canvas, viewport, aircraft);
        }
    }

    private void Draw(SKCanvas canvas, Viewport viewport, AircraftLayer layer)
    {
        var markers = layer.Markers;
        if (markers.Length == 0)
        {
            return;
        }

        // The sprites are made for the pixels of this canvas, so they are stamped
        // in device coordinates: the DPI scale would otherwise resample them.
        var matrix = canvas.TotalMatrix;
        var density = matrix.ScaleX;
        var offsetX = matrix.TransX;
        var offsetY = matrix.TransY;
        var resolution = viewport.Resolution;
        var centerX = viewport.CenterX;
        var centerY = viewport.CenterY;
        var halfWidth = viewport.Width / 2;
        var halfHeight = viewport.Height / 2;
        var reachX = halfWidth + CullMargin;
        var reachY = halfHeight + CullMargin;

        PrepareFrame(layer, density);

        canvas.Save();
        canvas.ResetMatrix();
        var font = _labelFont!;
        var ascent = -font.Metrics.Ascent;
        _labelPaint.Color = new SKColor(layer.LabelColor);

        foreach (var marker in markers)
        {
            if (!marker.HasPosition)
            {
                continue;
            }

            var screenX = (marker.WorldX - centerX) / resolution;
            var screenY = (centerY - marker.WorldY) / resolution;
            if (Math.Abs(screenX) > reachX || Math.Abs(screenY) > reachY)
            {
                continue;
            }

            var x = (float)((screenX + halfWidth) * density + offsetX);
            var y = (float)((screenY + halfHeight) * density + offsetY);

            var sprite = SpriteFor(marker, density);
            canvas.DrawImage(sprite, x - sprite.Width / 2f, y - sprite.Height / 2f, SubPixel, _spritePaint);

            if (marker.Callsign.Length > 0 && LabelFor(marker.Callsign, font) is { Blob: { } blob } label)
            {
                // The callsign hangs from the bottom of the clickable box.
                canvas.DrawText(
                    blob,
                    x - label.Width / 2,
                    y + marker.BoxSize * density / 2 + ascent,
                    _labelPaint);
            }
        }

        canvas.Restore();
    }

    /// <summary>Drops what the theme or the display scale of this frame made stale.</summary>
    private void PrepareFrame(AircraftLayer layer, float density)
    {
        if (layer.ShadowColor != _shadowColor)
        {
            ClearSprites();
            _shadowColor = layer.ShadowColor;
        }

        var size = (float)(LabelFontSize * layer.LabelScale * density);
        if (_labelFont is null || size != _labelFontSize)
        {
            ClearLabels();
            _labelFont?.Dispose();
            _labelTypeface ??= CreateLabelTypeface();
            _labelFont = new SKFont(_labelTypeface, size)
            {
                Edging = SKFontEdging.Antialias,
                Subpixel = true
            };
            _labelFontSize = size;
        }

        if (_spriteBytes > SpriteBudgetBytes)
        {
            ClearSprites();
        }

        if (_labels.Count > LabelLimit)
        {
            ClearLabels();
        }
    }

    private static SKTypeface CreateLabelTypeface() =>
        SKTypeface.FromFamilyName(
            "Segoe UI",
            new SKFontStyle(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright));

    private SKImage SpriteFor(AircraftMarker marker, float density)
    {
        var key = new SpriteKey(marker.Glyph, marker.Heading, marker.Size, marker.Color, marker.IsOnGround, density);
        if (!_sprites.TryGetValue(key, out var sprite))
        {
            sprite = CreateSprite(key);
            _sprites.Add(key, sprite);
            _spriteBytes += sprite.Width * sprite.Height * 4L;
        }

        return sprite;
    }

    private SKImage CreateSprite(SpriteKey key)
    {
        var side = (int)Math.Ceiling(key.Size * SpriteMargin * key.Density) + 4;
        using var surface = SKSurface.Create(new SKImageInfo(side, side, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var silhouette = SilhouetteOf(key.Glyph);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        // The shadow keeps its strength on the ground: only the pin itself fades.
        paint.Color = new SKColor(_shadowColor);
        DrawSilhouette(canvas, silhouette, paint, key, side, ShadowOffset);

        var color = new SKColor(key.Color);
        paint.Color = key.IsOnGround
            ? color.WithAlpha((byte)Math.Round(color.Alpha * GroundOpacity))
            : color;
        DrawSilhouette(canvas, silhouette, paint, key, side, 0);

        return surface.Snapshot();
    }

    private static void DrawSilhouette(
        SKCanvas canvas, SKPath silhouette, SKPaint paint, SpriteKey key, int side, float offsetY)
    {
        canvas.Save();
        canvas.Translate(side / 2f, side / 2f + offsetY * key.Density);
        canvas.RotateDegrees(key.Heading);
        canvas.Scale(key.Size * key.Density / ArtworkSize);
        canvas.Translate(-ArtworkSize / 2, -ArtworkSize / 2);
        canvas.DrawPath(silhouette, paint);
        canvas.Restore();
    }

    private static SKPath SilhouetteOf(AircraftGlyph glyph)
    {
        lock (Silhouettes)
        {
            if (!Silhouettes.TryGetValue(glyph, out var silhouette))
            {
                // The artwork relies on SVG's default non-zero fill rule.
                silhouette = SKPath.ParseSvgPathData(AircraftIconCatalog.PathData(glyph));
                silhouette.FillType = SKPathFillType.Winding;
                Silhouettes.Add(glyph, silhouette);
            }

            return silhouette;
        }
    }

    private Label LabelFor(string callsign, SKFont font)
    {
        if (!_labels.TryGetValue(callsign, out var label))
        {
            label = new Label(SKTextBlob.Create(callsign, font), font.MeasureText(callsign));
            _labels.Add(callsign, label);
        }

        return label;
    }

    private void ClearSprites()
    {
        foreach (var sprite in _sprites.Values)
        {
            sprite.Dispose();
        }

        _sprites.Clear();
        _spriteBytes = 0;
    }

    private void ClearLabels()
    {
        foreach (var label in _labels.Values)
        {
            label.Blob?.Dispose();
        }

        _labels.Clear();
    }

    private readonly record struct SpriteKey(
        AircraftGlyph Glyph,
        int Heading,
        int Size,
        uint Color,
        bool IsOnGround,
        float Density);

    private readonly record struct Label(SKTextBlob? Blob, float Width);
}
