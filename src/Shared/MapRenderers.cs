using Mapsui.Rendering.Skia;
using Mapsui.Styles;
using TheEye.Core.Maps;

namespace TheEye.Rendering;

/// <summary>The renderers this app adds to Mapsui, registered once per process.</summary>
public static class MapRenderers
{
    public static void Register()
    {
        AtcGlyphRenderer.Register();
        MapRenderer.RegisterStyleRenderer(typeof(VectorStyle), new CachedPolygonRenderer());
        MapRenderer.RegisterLayerRenderer(AircraftLayer.RendererName, new AircraftLayerRenderer().Render);
    }
}
