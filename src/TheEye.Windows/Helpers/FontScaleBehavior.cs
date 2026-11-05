using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace TheEye.Windows.Helpers;

public static class FontScaleBehavior
{
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
        "Scale",
        typeof(double),
        typeof(FontScaleBehavior),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.Inherits, OnScaleChanged));

    private static readonly ConditionalWeakTable<FrameworkElement, BaseFontSize> BaseFontSizes = new();

    public static void SetScale(DependencyObject element, double value) => element.SetValue(ScaleProperty, value);

    public static double GetScale(DependencyObject element) => (double)element.GetValue(ScaleProperty);

    private static void OnScaleChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        var scale = args.NewValue is double value && double.IsFinite(value) && value > 0 ? value : 1;
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
        element.FontSize = BaseFontSizes.GetValue(element, item => new BaseFontSize(((TextBlock)item).FontSize)).Value * scale;

    private static void ApplyScale(TextBox element, double scale) =>
        element.FontSize = BaseFontSizes.GetValue(element, item => new BaseFontSize(((TextBox)item).FontSize)).Value * scale;

    private sealed record BaseFontSize(double Value);
}