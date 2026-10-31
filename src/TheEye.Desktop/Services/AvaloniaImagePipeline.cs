using Avalonia.Media.Imaging;
using TheEye.Core.Maps;
using TheEye.Presentation.Services;

namespace TheEye.Desktop.Services;

/// <summary>
/// The Skia decoders for the pictures the shared view models ask for. The bulk
/// aircraft icon ships in the Core resources and is decoded once per heading;
/// the rating badges and national flags arrive from the network. The caches
/// themselves are shared code; only the decoded frame type is Avalonia's.
/// </summary>
public sealed class AvaloniaImagePipeline : IImagePipeline
{
    public AvaloniaImagePipeline()
    {
        AircraftIcons = new AircraftIconCache<Bitmap>(CreateIcon);
        SessionImages = new RemoteImageCache<Bitmap>(Decode);
    }

    public IAircraftIconCache AircraftIcons { get; }

    public IImageCache SessionImages { get; }

    public void Dispose()
    {
        // Only the remote cache holds disposables (its HTTP client); the icon
        // cache is plain bitmaps that the GC reclaims with the pipeline.
        SessionImages.Dispose();
    }

    private static Bitmap CreateIcon(int heading)
    {
        using var stream = AircraftIconCatalog.Open(heading);
        return Decode(stream);
    }

    /// <summary>
    /// Skia copies the pixels while decoding, so the stream can be disposed as
    /// soon as the bitmap exists; that is what lets both caches decode on a
    /// background thread and hand an immutable frame to the UI.
    /// </summary>
    private static Bitmap Decode(Stream stream) => new(stream);
}
