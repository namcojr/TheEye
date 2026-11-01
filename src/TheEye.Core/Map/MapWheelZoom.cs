using Mapsui;
using Mapsui.Manipulations;

namespace TheEye.Core.Maps;

/// <summary>Accumulates wheel input in notches so partial events do not each skip a zoom level.</summary>
public sealed class MapWheelZoom
{
    private double _pendingNotches;

    public void Zoom(Navigator navigator, double notches, ScreenPosition position)
    {
        if (!double.IsFinite(notches))
        {
            throw new ArgumentOutOfRangeException(nameof(notches), "Wheel input must be finite.");
        }

        if (notches == 0)
        {
            return;
        }

        if (Math.Sign(notches) != Math.Sign(_pendingNotches))
        {
            _pendingNotches = 0;
        }

        _pendingNotches += notches;
        // Allow for rounding when fractional events add up to one notch.
        while (Math.Abs(_pendingNotches) >= 1 - 1e-9)
        {
            var direction = Math.Sign(_pendingNotches);
            _pendingNotches -= direction;
            navigator.MouseWheelZoom(direction, position);
        }
    }
}
