using Mapsui.Styles;

namespace TheEye.Core.Maps;

/// <summary>A screen-space vector marker whose size is resolved for each rendered viewport.</summary>
public sealed class AtcGlyphStyle(string? facility, string? callsign, string? ringColor = null) : BaseStyle
{
    public string? RingColor { get; } = ringColor;

    public AtcMarkerStyle MarkerAtResolution(double resolution) =>
        AtcMarkerStyles.For(facility, callsign, AtcMarkerStyles.ZoomScale(resolution));
}
