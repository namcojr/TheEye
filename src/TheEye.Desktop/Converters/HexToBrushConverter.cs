using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TheEye.Desktop.Converters;

/// <summary>
/// Paints the "#RRGGBB" strings the theme table hands out. The settings tiles
/// use it for their palette preview, exactly as the Windows shell does.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static HexToBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && Color.TryParse(hex, out var color)
            ? new SolidColorBrush(color)
            : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
