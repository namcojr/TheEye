using System.Windows;
using System.Windows.Media;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using TheEye.Presentation.ViewModels;
using TheEye.Rendering;

namespace TheEye.Windows.Controls;

/// <summary>
/// Hosts the shared Skia runway diagram. The brushes come from the theme
/// through DynamicResource, so the arrow and labels follow theme changes.
/// </summary>
public sealed class RunwayDiagram : SKElement
{
    public static readonly DependencyProperty StateProperty = Register(nameof(State), typeof(RunwayDiagramState));
    public static readonly DependencyProperty TextBrushProperty = Register(nameof(TextBrush), typeof(Brush));
    public static readonly DependencyProperty MutedTextBrushProperty = Register(nameof(MutedTextBrush), typeof(Brush));
    public static readonly DependencyProperty AccentBrushProperty = Register(nameof(AccentBrush), typeof(Brush));
    public static readonly DependencyProperty HeadwindBrushProperty = Register(nameof(HeadwindBrush), typeof(Brush));
    public static readonly DependencyProperty TailwindBrushProperty = Register(nameof(TailwindBrush), typeof(Brush));
    public static readonly DependencyProperty HaloBrushProperty = Register(nameof(HaloBrush), typeof(Brush));

    public RunwayDiagramState? State
    {
        get => (RunwayDiagramState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public Brush? TextBrush
    {
        get => (Brush?)GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    public Brush? MutedTextBrush
    {
        get => (Brush?)GetValue(MutedTextBrushProperty);
        set => SetValue(MutedTextBrushProperty, value);
    }

    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush? HeadwindBrush
    {
        get => (Brush?)GetValue(HeadwindBrushProperty);
        set => SetValue(HeadwindBrushProperty, value);
    }

    public Brush? TailwindBrush
    {
        get => (Brush?)GetValue(TailwindBrushProperty);
        set => SetValue(TailwindBrushProperty, value);
    }

    public Brush? HaloBrush
    {
        get => (Brush?)GetValue(HaloBrushProperty);
        set => SetValue(HaloBrushProperty, value);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        // The surface is in device pixels; the renderer works in DIPs like the rest of the layout.
        var scale = (float)(e.Info.Width / ActualWidth);
        canvas.Save();
        canvas.Scale(scale);
        var colors = new RunwayDiagramColors(
            ToSkia(TextBrush, SKColors.White),
            ToSkia(MutedTextBrush, SKColors.Gray),
            ToSkia(AccentBrush, SKColors.DeepSkyBlue),
            ToSkia(HeadwindBrush, SKColors.MediumSeaGreen),
            ToSkia(TailwindBrush, SKColors.OrangeRed),
            ToSkia(HaloBrush, SKColors.Black));
        RunwayDiagramRenderer.Draw(canvas, (float)ActualWidth, (float)ActualHeight, State, colors);
        canvas.Restore();
    }

    private static DependencyProperty Register(string name, Type type) =>
        DependencyProperty.Register(name, type, typeof(RunwayDiagram),
            new FrameworkPropertyMetadata(null, (target, _) => ((RunwayDiagram)target).InvalidateVisual()));

    private static SKColor ToSkia(Brush? brush, SKColor fallback) => brush is SolidColorBrush solid
        ? new SKColor(solid.Color.R, solid.Color.G, solid.Color.B, (byte)(solid.Color.A * Math.Clamp(solid.Opacity, 0, 1)))
        : fallback;
}
