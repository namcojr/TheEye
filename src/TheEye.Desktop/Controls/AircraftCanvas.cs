using Avalonia;
using Avalonia.Controls;

namespace TheEye.Desktop.Controls;

/// <summary>
/// The surface the aircraft pins sit on. Every aircraft is placed by the view
/// model in screen pixels for the current viewport, so measuring and arranging
/// is a plain translation: the pins follow the map as it pans and zooms without
/// the overlay ever taking the pointer away from it.
/// </summary>
public sealed class AircraftCanvas : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            // The pins have a fixed size; giving them infinite room means their
            // desired size is never squeezed by the edge of the map.
            child.Measure(Size.Infinity);
        }

        return new Size();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                child.Arrange(default);
                continue;
            }

            var size = child.DesiredSize;
            child.Arrange(new Rect(Canvas.GetLeft(child), Canvas.GetTop(child), size.Width, size.Height));
        }

        return finalSize;
    }
}
