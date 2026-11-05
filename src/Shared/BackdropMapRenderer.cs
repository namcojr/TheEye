using System.IO;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using Mapsui.Widgets;
using SkiaSharp;
using TheEye.Core.Maps;

namespace TheEye.Rendering;

/// <summary>
/// Keeps the part of the map that holds still in an offscreen bitmap, for a
/// shell that renders on the CPU. A pan or an aircraft that moved then costs a
/// copy of that bitmap plus the layers on top of it, instead of filling and
/// stroking every country and sector again.
/// </summary>
/// <remarks>
/// <para>
/// The bitmap can only move by whole pixels, so the layers on top are drawn with
/// the viewport shifted by the same fraction of a pixel. Aircraft and backdrop
/// therefore stay exactly on each other; the whole picture is at most half a pixel
/// from where the navigator says it is, for the length of that frame.
/// </para>
/// <para>
/// Nothing has to tell this class that the backdrop changed: it compares what the
/// layers hold on every frame, and a tile basemap announces each tile that lands
/// through <see cref="ILayer.DataChanged"/>. It steps aside while the resolution
/// changes, because a zoom animation would discard the bitmap on every frame.
/// </para>
/// <para>
/// The Avalonia shell does not use this: it usually draws on the GPU, where the
/// map is cheap to repaint and a CPU bitmap would have to be uploaded.
/// </para>
/// </remarks>
public sealed class BackdropMapRenderer : IMapRenderer, IDisposable
{
    /// <summary>
    /// The last layer of the backdrop. The route, selection and aircraft layers
    /// above it change while the map stands still.
    /// </summary>
    private const string LastBackdropLayer = IvaoMapLayers.AtcStationsLayerName;

    /// <summary>
    /// How far, in device independent pixels, the bitmap reaches past each edge of
    /// the window, i.e. how far a pan gets before it has to be painted again.
    /// </summary>
    private const int Margin = 256;

    private readonly MapRenderer _map = new();
    private BackdropLayerState[] _layers = [];
    private SKSurface? _surface;
    private SKImage? _image;
    private (double X, double Y) _center;
    private (double Width, double Height) _size;
    private double _resolution = double.NaN;
    private double _settledResolution = double.NaN;
    private float _density;
    private Color? _background;
    private ILayer[] _watched = [];
    private int _dataVersion;
    private int _capturedDataVersion;

    public void Render(object target, Viewport viewport, IEnumerable<ILayer> layers, IEnumerable<IWidget> widgets,
        RenderService renderService, Color? background = null, MRect? dirtyRegion = null,
        CoordinateSpace coordinateSpace = CoordinateSpace.World)
    {
        var canvas = (SKCanvas)target;
        var all = layers as ILayer[] ?? layers.ToArray();
        var split = Array.FindIndex(all, layer => layer.Name == LastBackdropLayer) + 1;
        var settled = viewport.Resolution == _settledResolution;
        _settledResolution = viewport.Resolution;

        if (!settled || split == 0 || background is null || viewport.Rotation != 0)
        {
            _map.Render(target, viewport, all, widgets, renderService, background, dirtyRegion, coordinateSpace);
            return;
        }

        var backdrop = all[..split];
        var density = canvas.TotalMatrix.ScaleX;
        if (!IsCurrent(viewport, backdrop, background, density))
        {
            Capture(viewport, backdrop, renderService, background.Value, density);
        }

        // Where the bitmap lands, rounded to the pixels it can actually be copied to.
        var offsetX = (_center.X - viewport.CenterX) / viewport.Resolution;
        var offsetY = (viewport.CenterY - _center.Y) / viewport.Resolution;
        var left = (float)Math.Round((viewport.Width / 2 + offsetX - _size.Width / 2) * density);
        var top = (float)Math.Round((viewport.Height / 2 + offsetY - _size.Height / 2) * density);

        var matrix = canvas.TotalMatrix;
        canvas.Save();
        canvas.ResetMatrix();
        canvas.DrawImage(_image!, left + matrix.TransX, top + matrix.TransY);
        canvas.Restore();

        // The viewport the rounded bitmap really shows, for what is drawn over it.
        var shownX = left / density - viewport.Width / 2 + _size.Width / 2;
        var shownY = top / density - viewport.Height / 2 + _size.Height / 2;
        var shown = new Viewport(
            _center.X - shownX * viewport.Resolution,
            _center.Y + shownY * viewport.Resolution,
            viewport.Resolution,
            0,
            viewport.Width,
            viewport.Height);

        _map.Render(target, shown, all[split..], widgets, renderService);
    }

    private bool IsCurrent(Viewport viewport, ILayer[] backdrop, Color? background, float density)
    {
        if (_image is null ||
            viewport.Resolution != _resolution ||
            density != _density ||
            background != _background ||
            Volatile.Read(ref _dataVersion) != _capturedDataVersion ||
            _size != (viewport.Width + 2 * Margin, viewport.Height + 2 * Margin) ||
            Math.Abs(_center.X - viewport.CenterX) / viewport.Resolution > Margin ||
            Math.Abs(_center.Y - viewport.CenterY) / viewport.Resolution > Margin ||
            _layers.Length != backdrop.Length)
        {
            return false;
        }

        for (var index = 0; index < backdrop.Length; index++)
        {
            if (!_layers[index].Matches(backdrop[index]))
            {
                return false;
            }
        }

        return true;
    }

    private void Capture(Viewport viewport, ILayer[] backdrop, RenderService renderService, Color background, float density)
    {
        var size = (Width: viewport.Width + 2 * Margin, Height: viewport.Height + 2 * Margin);
        var pixelWidth = (int)Math.Ceiling(size.Width * density);
        var pixelHeight = (int)Math.Ceiling(size.Height * density);
        if (_surface is null || _surface.Canvas.DeviceClipBounds.Size != new SKSizeI(pixelWidth, pixelHeight))
        {
            _surface?.Dispose();
            _surface = SKSurface.Create(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));
        }

        // Read first: a tile that lands while this paints gets the next frame to repaint.
        Watch(backdrop);
        _capturedDataVersion = Volatile.Read(ref _dataVersion);

        _image?.Dispose();
        var canvas = _surface.Canvas;
        canvas.ResetMatrix();
        canvas.Scale(density);
        var capture = new Viewport(viewport.CenterX, viewport.CenterY, viewport.Resolution, 0, size.Width, size.Height);
        _map.Render(canvas, capture, backdrop, [], renderService, background);
        _image = _surface.Snapshot();

        _center = (viewport.CenterX, viewport.CenterY);
        _size = size;
        _resolution = viewport.Resolution;
        _density = density;
        _background = background;
        _layers = Array.ConvertAll(backdrop, BackdropLayerState.Of);
    }

    /// <summary>Follows the layers that load their data on their own, i.e. the tiles.</summary>
    private void Watch(ILayer[] backdrop)
    {
        var loading = Array.FindAll(backdrop, layer => layer is not MemoryLayer);
        if (loading.SequenceEqual(_watched))
        {
            return;
        }

        foreach (var layer in _watched)
        {
            layer.DataChanged -= OnLayerDataChanged;
        }

        foreach (var layer in loading)
        {
            layer.DataChanged += OnLayerDataChanged;
        }

        _watched = loading;
    }

    /// <summary>Raised on the fetcher's thread.</summary>
    private void OnLayerDataChanged(object? sender, Mapsui.Fetcher.DataChangedEventArgs e) =>
        Interlocked.Increment(ref _dataVersion);

    public void Dispose()
    {
        foreach (var layer in _watched)
        {
            layer.DataChanged -= OnLayerDataChanged;
        }

        _image?.Dispose();
        _surface?.Dispose();
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

    /// <summary>What a layer showed when the bitmap was painted.</summary>
    private readonly record struct BackdropLayerState(
        ILayer Layer, bool Enabled, double Opacity, object? Style, object? Features)
    {
        public static BackdropLayerState Of(ILayer layer) =>
            new(layer, layer.Enabled, layer.Opacity, layer.Style, (layer as MemoryLayer)?.Features);

        public bool Matches(ILayer layer) =>
            ReferenceEquals(layer, Layer) &&
            layer.Enabled == Enabled &&
            layer.Opacity == Opacity &&
            ReferenceEquals(layer.Style, Style) &&
            ReferenceEquals((layer as MemoryLayer)?.Features, Features);
    }
}
