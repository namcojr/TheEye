using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TheEye.Windows.Converters;

/// <summary>
/// Paints a colour the shared view models carry as "#RRGGBB" or "#AARRGGBB".
/// The facility colours of an ATC card and the preview tiles in Settings are
/// strings, because a brush is a WPF type the shared layer cannot hold.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
        {
            return Brushes.Transparent;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(hex, out var cached))
            {
                return cached;
            }

            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Cache[hex] = brush;
            return brush;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
