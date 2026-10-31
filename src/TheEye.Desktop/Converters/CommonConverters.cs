using System.Globalization;
using Avalonia.Data.Converters;

namespace TheEye.Desktop.Converters;

/// <summary>Shows a detail pane only while something is selected.</summary>
public sealed class IsNotNullConverter : IValueConverter
{
    public static IsNotNullConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows a notice block only while there is text to show.</summary>
public sealed class IsNotEmptyConverter : IValueConverter
{
    public static IsNotEmptyConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// The negation of <see cref="IsNotNullConverter"/>: shows the empty-state pane
/// only while nothing is selected.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static InverseBoolConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Picks one of two numbers for the marker artwork: the "true" value first, the
/// "false" value second ("24,21" grows the icon when the aircraft is selected,
/// "0.72,1" dims the icon while it sits on the ground).
/// </summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public static BoolToDoubleConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (whenTrue, whenFalse) = Parse(parameter as string);
        return value is true ? whenTrue : whenFalse;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static (double WhenTrue, double WhenFalse) Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (1d, 0d);
        }

        var parts = text.Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var whenTrue) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var whenFalse))
        {
            return (1d, 0d);
        }

        return (whenTrue, whenFalse);
    }
}
