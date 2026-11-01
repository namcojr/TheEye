using Mapsui;
using Mapsui.Extensions;
using Mapsui.Manipulations;

namespace TheEye.Core.Maps;

/// <summary>Accumulates wheel input into animated half-level steps per full notch.</summary>
public sealed class MapWheelZoom
{
    private double _pendingNotches;
    private Navigator? _navigator;
    private double _destinationResolution;
    private long _animationStartedAt;

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
            ZoomHalfLevel(navigator, direction, position);
        }
    }

    private void ZoomHalfLevel(Navigator navigator, int direction, ScreenPosition position)
    {
        if (navigator.ZoomLock)
        {
            return;
        }

        var now = Environment.TickCount64;
        var elapsed = now - _animationStartedAt;
        // Rapid notches must build on the target, not an unfinished animation frame.
        var current = ReferenceEquals(_navigator, navigator)
            && elapsed >= 0 && elapsed < navigator.MouseWheelAnimation.Duration
            ? _destinationResolution
            : navigator.Viewport.Resolution;
        var resolution = current / Math.Pow(2, direction * 0.5);
        if (navigator.ZoomBounds is { } bounds)
        {
            resolution = Math.Clamp(resolution, bounds.Min, bounds.Max);
        }

        _navigator = navigator;
        _destinationResolution = resolution;
        _animationStartedAt = now;
        if (resolution == navigator.Viewport.Resolution)
        {
            return;
        }

        if (navigator.PanLock)
        {
            position = navigator.Viewport.WorldToScreen(navigator.Viewport.CenterX, navigator.Viewport.CenterY);
        }

        navigator.ZoomTo(resolution, position,
            navigator.MouseWheelAnimation.Duration, navigator.MouseWheelAnimation.Easing);
    }
}
