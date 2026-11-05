using Mapsui;
using Mapsui.Layers;

namespace TheEye.Core.Maps;

/// <summary>
/// What the renderer needs to paint one aircraft. The view model owns it and
/// writes it on the UI thread; a shell may render on another thread (Avalonia
/// does), which is harmless because every field is read on its own and the
/// worst a torn read can do is paint one frame with a coordinate from the frame
/// before. The object is reused across refreshes, so moving thousands of
/// aircraft allocates nothing.
/// </summary>
public sealed class AircraftMarker
{
    /// <summary>Projected position, in EPSG:3857 metres.</summary>
    public double WorldX { get; set; }

    /// <summary>Projected position, in EPSG:3857 metres.</summary>
    public double WorldY { get; set; }

    /// <summary>False leaves the aircraft out of the frame.</summary>
    public bool HasPosition { get; set; }

    public AircraftGlyph Glyph { get; set; }

    /// <summary>Heading in whole degrees, 0 to 359.</summary>
    public int Heading { get; set; }

    /// <summary>Side of the artwork in device independent pixels, growth included.</summary>
    public int Size { get; set; }

    /// <summary>Side of the clickable box the artwork and the callsign hang from.</summary>
    public int BoxSize { get; set; }

    /// <summary>Colour of the artwork as 0xAARRGGBB.</summary>
    public uint Color { get; set; }

    /// <summary>Aircraft on the ground are painted a little see-through.</summary>
    public bool IsOnGround { get; set; }

    public string Callsign { get; set; } = string.Empty;
}

/// <summary>
/// Paints the aircraft from inside Mapsui's own render pass, so a pin is placed
/// with the very viewport the map under it is drawn with and the two cannot land
/// on different frames. The aircraft are not features: the renderer draws the
/// whole array in one pass, which avoids sorting and style lookups per aircraft.
/// </summary>
public sealed class AircraftLayer : BaseLayer
{
    public const string LayerName = "Aircraft";

    /// <summary>The key of the layer renderer each shell registers.</summary>
    public const string RendererName = "theeye-aircraft";

    public AircraftLayer()
        : base(LayerName)
    {
        CustomLayerRendererName = RendererName;
        Style = null;
    }

    /// <summary>Replaced as a whole when the traffic changes, so a frame sees one list.</summary>
    public AircraftMarker[] Markers { get; set; } = [];

    /// <summary>Colour of the callsigns as 0xAARRGGBB.</summary>
    public uint LabelColor { get; set; } = 0xFFFFFFFF;

    /// <summary>Colour of the drop shadow as 0xAARRGGBB.</summary>
    public uint ShadowColor { get; set; } = 0x66000000;

    /// <summary>The interface font scale, which the callsigns follow.</summary>
    public double LabelScale { get; set; } = 1;

    public override IEnumerable<IFeature> GetFeatures(MRect box, double resolution) => [];
}
