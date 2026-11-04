using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using TheEye.Presentation.ViewModels;
using TheEye.Rendering;

namespace TheEye.Desktop.Controls;

/// <summary>
/// Hosts the shared Skia runway diagram. The brushes come from the theme
/// through DynamicResource, so the arrow and labels follow theme changes.
/// </summary>
public sealed class RunwayDiagram : Control
{
    public static readonly StyledProperty<RunwayDiagramState?> StateProperty =
        AvaloniaProperty.Register<RunwayDiagram, RunwayDiagramState?>(nameof(State));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<RunwayDiagram, IBrush?>(nameof(TextBrush));

    public static readonly StyledProperty<IBrush?> MutedTextBrushProperty =
        AvaloniaProperty.Register<RunwayDiagram, IBrush?>(nameof(MutedTextBrush));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<RunwayDiagram, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> HeadwindBrushProperty =
        AvaloniaProperty.Register<RunwayDiagram, IBrush?>(nameof(HeadwindBrush));

    public static readonly StyledProperty<IBrush?> TailwindBrushProperty =
        AvaloniaProperty.Register<RunwayDiagram, IBrush?>(nameof(TailwindBrush));

    public static readonly StyledProperty<IBrush?> HaloBrushProperty =
        AvaloniaProperty.Register<RunwayDiagram, IBrush?>(nameof(HaloBrush));

    static RunwayDiagram() => AffectsRender<RunwayDiagram>(
        StateProperty, TextBrushProperty, MutedTextBrushProperty, AccentBrushProperty,
        HeadwindBrushProperty, TailwindBrushProperty, HaloBrushProperty);

    public RunwayDiagramState? State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public IBrush? MutedTextBrush
    {
        get => GetValue(MutedTextBrushProperty);
        set => SetValue(MutedTextBrushProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? HeadwindBrush
    {
        get => GetValue(HeadwindBrushProperty);
        set => SetValue(HeadwindBrushProperty, value);
    }

    public IBrush? TailwindBrush
    {
        get => GetValue(TailwindBrushProperty);
        set => SetValue(TailwindBrushProperty, value);
    }

    public IBrush? HaloBrush
    {
        get => GetValue(HaloBrushProperty);
        set => SetValue(HaloBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var colors = new RunwayDiagramColors(
            ToSkia(TextBrush, SKColors.White),
            ToSkia(MutedTextBrush, SKColors.Gray),
            ToSkia(AccentBrush, SKColors.DeepSkyBlue),
            ToSkia(HeadwindBrush, SKColors.MediumSeaGreen),
            ToSkia(TailwindBrush, SKColors.OrangeRed),
            ToSkia(HaloBrush, SKColors.Black));
        context.Custom(new DrawOperation(new Rect(Bounds.Size), State, colors));
    }

    private static SKColor ToSkia(IBrush? brush, SKColor fallback) => brush is ISolidColorBrush solid
        ? new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, (byte)(solid.Color.A * Math.Clamp(solid.Opacity, 0, 1)))
        : fallback;

    private sealed class DrawOperation(Rect bounds, RunwayDiagramState? state, RunwayDiagramColors colors) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point point) => false;

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            try
            {
                canvas.ClipRect(SKRect.Create((float)bounds.Width, (float)bounds.Height));
                RunwayDiagramRenderer.Draw(canvas, (float)bounds.Width, (float)bounds.Height, state, colors);
            }
            finally
            {
                canvas.Restore();
            }
        }
    }
}
