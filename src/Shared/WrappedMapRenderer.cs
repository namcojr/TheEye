using System.IO;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using Mapsui.Widgets;
using TheEye.Core.Maps;

namespace TheEye.Rendering;

/// <summary>
/// Paints the world once per copy of it on screen, see <see cref="WorldWrap"/>.
/// A copy is the same layer drawn with the viewport moved by whole worlds, so no
/// renderer below this one knows about wrapping, and the viewport's culling leaves
/// a copy with nothing to do unless it really is in view. The usual frame is far
/// from the date line and goes straight through.
/// </summary>
/// <remarks>
/// Layers stay in their drawing order, copy by copy inside each layer, or a sector
/// of one copy could end up over the aircraft of the next. A tile layer is the
/// exception: its schema already reaches past the edges of the world, so it is
/// drawn once (<see cref="WrappedTileSource"/>).
/// </remarks>
public sealed class WrappedMapRenderer : IMapRenderer
{
    private readonly MapRenderer _map = new();

    public void Render(object target, Viewport viewport, IEnumerable<ILayer> layers, IEnumerable<IWidget> widgets,
        RenderService renderService, Color? background = null, MRect? dirtyRegion = null,
        CoordinateSpace coordinateSpace = CoordinateSpace.World)
    {
        var all = layers as ILayer[] ?? layers.ToArray();
        if (Array.TrueForAll(all, layer => IsInMainWorldOnly(layer, viewport)))
        {
            _map.Render(target, viewport, all, widgets, renderService, background, dirtyRegion, coordinateSpace);
            return;
        }

        _map.Render(target, viewport, [], [], renderService, background);
        foreach (var layer in all)
        {
            if (layer is TileLayer)
            {
                _map.Render(target, viewport, [layer], [], renderService);
                continue;
            }

            if (!layer.Enabled)
            {
                continue;
            }

            var (first, last) = WorldWrap.CopiesOf(layer.Extent, viewport);
            for (var copy = first; copy <= last; copy++)
            {
                var moved = viewport with { CenterX = viewport.CenterX - copy * WorldWrap.WorldWidth };
                _map.Render(target, moved, [layer], [], renderService);
            }
        }

        _map.Render(target, viewport, [], widgets, renderService);
    }

    private static bool IsInMainWorldOnly(ILayer layer, Viewport viewport)
    {
        if (layer is TileLayer || !layer.Enabled)
        {
            return true;
        }

        var (first, last) = WorldWrap.CopiesOf(layer.Extent, viewport);
        return first > last || (first == 0 && last == 0);
    }

    public void UpdateDrawables(Viewport viewport, ILayer layer, RenderService renderService) =>
        _map.UpdateDrawables(viewport, layer, renderService);

    public IDrawable? CreateDrawableForFeature(Viewport viewport, ILayer layer, IFeature feature, IStyle style,
        RenderService renderService) =>
        _map.CreateDrawableForFeature(viewport, layer, feature, style, renderService);

    public MemoryStream RenderToBitmapStream(Viewport viewport, IEnumerable<ILayer> layers, RenderService renderService,
        Color? background = null, float pixelDensity = 1, IEnumerable<IWidget>? widgets = null,
        RenderFormat renderFormat = RenderFormat.Png, int quality = 100) =>
        _map.RenderToBitmapStream(viewport, layers, renderService, background, pixelDensity, widgets, renderFormat, quality);

    public bool TryGetWidgetRenderer(Type widgetType, out IWidgetRenderer? widgetRenderer) =>
        _map.TryGetWidgetRenderer(widgetType, out widgetRenderer);

    public bool TryGetStyleRenderer(Type styleType, out IStyleRenderer? styleRenderer) =>
        _map.TryGetStyleRenderer(styleType, out styleRenderer);

    public MapInfo GetMapInfo(ScreenPosition screenPosition, Viewport viewport, IEnumerable<ILayer> layers,
        RenderService renderService, int margin = 0) =>
        _map.GetMapInfo(screenPosition, viewport, layers, renderService, margin);
}
