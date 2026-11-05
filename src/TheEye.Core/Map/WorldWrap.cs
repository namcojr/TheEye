using Mapsui;
using Mapsui.Extensions;
using Mapsui.Limiting;
using Mapsui.Manipulations;

namespace TheEye.Core.Maps;

/// <summary>
/// The map repeats east and west for ever, so panning past the date line carries
/// on into the other side of the world. There is still one world: the navigator
/// keeps its centre inside it (<see cref="WrappingViewportLimiter"/>) and the
/// renderer paints it once per copy on screen. A copy is that same world moved by
/// a whole number of <see cref="WorldWidth"/>; nothing is stored for it.
/// </summary>
public static class WorldWrap
{
    /// <summary>Half the width of the world in EPSG:3857 metres, i.e. x at 180 degrees.</summary>
    public const double HalfWorldWidth = 20037508.342789244;

    public const double WorldWidth = 2 * HalfWorldWidth;

    /// <summary>
    /// How far, in pixels, what a layer draws may stick out past its extent: pins
    /// and badges are centred on their position and a callsign hangs under them.
    /// </summary>
    private const double SymbolReach = 150;

    /// <summary>The same place in the main world: x between plus and minus half a world.</summary>
    public static double Wrap(double x) => x - WorldWidth * Math.Round(x / WorldWidth);

    /// <summary>The copy of <paramref name="x"/> closest to <paramref name="reference"/>.</summary>
    public static double NearestCopy(double x, double reference) => reference + Wrap(x - reference);

    /// <summary>True for an x on the 180 degree meridian, where the source data is cut.</summary>
    public static bool IsOnDateLine(double x) => Math.Abs(Math.Abs(x) - HalfWorldWidth) < 1;

    /// <summary>
    /// Where a world position lands on screen, taking the copy closest to the
    /// middle of the viewport. That is the only copy of it the pointer logic knows.
    /// </summary>
    public static (double X, double Y) ToScreen(Viewport viewport, double x, double y) =>
        viewport.WorldToScreenXY(NearestCopy(x, viewport.CenterX), y);

    /// <summary>
    /// Moves a pointer position onto the copy <see cref="ToScreen"/> uses. That
    /// changes nothing unless the window is wider than the world, when part of the
    /// map is on screen twice and either place has to find the same aircraft.
    /// </summary>
    public static ScreenPosition OntoNearestCopy(ScreenPosition position, Viewport viewport)
    {
        var fromCenter = (position.X - viewport.Width / 2) * viewport.Resolution;
        return Math.Abs(fromCenter) <= HalfWorldWidth || viewport.Resolution <= 0
            ? position
            : new ScreenPosition(viewport.Width / 2 + Wrap(fromCenter) / viewport.Resolution, position.Y);
    }

    /// <summary>
    /// The copies of something inside <paramref name="extent"/> that can reach the
    /// viewport, as a first and last copy number; copy 0 is the main world. The
    /// first is above the last when no copy can. A layer without an extent draws
    /// on its own and is taken to fill the main world.
    /// </summary>
    public static (int First, int Last) CopiesOf(MRect? extent, Viewport viewport)
    {
        var reach = (viewport.Width / 2 + SymbolReach) * viewport.Resolution;
        var west = extent?.Left ?? -HalfWorldWidth;
        var east = extent?.Right ?? HalfWorldWidth;
        return (
            (int)Math.Ceiling((viewport.CenterX - reach - east) / WorldWidth),
            (int)Math.Floor((viewport.CenterX + reach - west) / WorldWidth));
    }
}

/// <summary>
/// Limits like Mapsui's own limiter, except that a centre leaving the world to
/// the east or west comes back in on the other side instead of stopping there.
/// Every gesture and animation works from the current viewport or from fixed end
/// points, so that jump of a whole world is invisible to them.
/// </summary>
public sealed class WrappingViewportLimiter : IViewportLimiter
{
    private const double NoLimit = 1e12;

    private readonly ViewportLimiter _limiter = new();

    public Viewport Limit(Viewport viewport, MRect? panBounds, MMinMax? zoomBounds)
    {
        var bounds = panBounds is null ? null : new MRect(-NoLimit, panBounds.Bottom, NoLimit, panBounds.Top);
        var limited = _limiter.Limit(viewport, bounds, zoomBounds);

        // When the zoom was limited the navigator scales the change of the centre
        // by that limit and asks again, and a jump here would count as a change.
        return limited.Resolution == viewport.Resolution
            ? limited with { CenterX = WorldWrap.Wrap(limited.CenterX) }
            : limited;
    }
}
