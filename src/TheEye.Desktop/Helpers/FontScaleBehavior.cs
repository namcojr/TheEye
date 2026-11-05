using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace TheEye.Desktop.Helpers;

public sealed class FontScaleBehavior
{
    public static readonly AttachedProperty<double> ScaleProperty =
        AvaloniaProperty.RegisterAttached<FontScaleBehavior, Control, double>("Scale", 1d, inherits: true);

    private static readonly ConditionalWeakTable<Control, BaseFontSize> BaseFontSizes = new();

    static FontScaleBehavior()
    {
        ScaleProperty.Changed.AddClassHandler<Control>(OnScaleChanged);
    }

    private FontScaleBehavior()
    {
    }

    public static void SetScale(Control element, double value) => element.SetValue(ScaleProperty, value);

    public static double GetScale(Control element) => element.GetValue(ScaleProperty);

    private static void OnScaleChanged(Control element, AvaloniaPropertyChangedEventArgs args)
    {
        var scale = element.GetValue(ScaleProperty);
        if (element is TextBlock textBlock)
        {
            ApplyScale(textBlock, scale);
        }
        else if (element is TextBox textBox)
        {
            ApplyScale(textBox, scale);
        }
    }

    private static void ApplyScale(TextBlock element, double scale) =>
        element.FontSize = BaseFontSizes.GetValue(element, item => new BaseFontSize(((TextBlock)item).FontSize)).Value *
                           (double.IsFinite(scale) && scale > 0 ? scale : 1);

    private static void ApplyScale(TextBox element, double scale) =>
        element.FontSize = BaseFontSizes.GetValue(element, item => new BaseFontSize(((TextBox)item).FontSize)).Value *
                           (double.IsFinite(scale) && scale > 0 ? scale : 1);

    private sealed record BaseFontSize(double Value);
}