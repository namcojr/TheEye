using Avalonia.Media;
using Avalonia.Media.Imaging;
using TheEye.Core.Maps;
using TheEye.Presentation.Services;

namespace TheEye.Desktop.Services;

/// <summary>
/// The Skia decoders for the pictures the shared view models ask for. The bulk
/// aircraft SVG becomes native geometry once per category;
/// the rating badges and national flags arrive from the network. The caches
/// themselves are shared code; only the geometry and image types are Avalonia's.
/// </summary>
public sealed class AvaloniaImagePipeline : IImagePipeline
{
    public AvaloniaImagePipeline()
    {
        AircraftIcons = new AircraftIconCache<Geometry>(CreateIcon);
        SessionImages = new RemoteImageCache<Bitmap>(Decode);
    }

    public IAircraftIconCache AircraftIcons { get; }

    public IImageCache SessionImages { get; }

    public void Dispose()
    {
        SessionImages.Dispose();
    }

    private static Geometry CreateIcon(AircraftGlyph glyph) =>
        Geometry.Parse("F1 " + AircraftIconCatalog.PathData(glyph));

    /// <summary>
    /// Skia copies the pixels while decoding, so the stream can be disposed as
    /// soon as the bitmap exists; that is what lets both caches decode on a
    /// background thread and hand an immutable frame to the UI.
    /// </summary>
    private static Bitmap Decode(Stream stream) => new(stream);
}
