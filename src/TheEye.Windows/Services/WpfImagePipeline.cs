using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;
using TheEye.Core.Maps;
using TheEye.Presentation.Services;

namespace TheEye.Windows.Services;

/// <summary>
/// The WPF decoders for the pictures the shared view models ask for: the bulk
/// aircraft icon lives in the Core resources and is decoded once per heading
/// (frozen, so it can be built off the UI thread), and the badges and flags come
/// from the network. The cache objects themselves are shared code.
/// </summary>
public sealed class WpfImagePipeline : IImagePipeline
{
    public WpfImagePipeline()
    {
        AircraftIcons = new AircraftIconCache<BitmapImage>(CreateIcon);
        SessionImages = new RemoteImageCache<BitmapImage>(Decode);
    }

    public IAircraftIconCache AircraftIcons { get; }

    public IImageCache SessionImages { get; }

    public void Dispose()
    {
        // Only the remote cache holds disposables (its HTTP client); the icon
        // cache is pure bitmaps that the GC reclaims with the pipeline.
        SessionImages.Dispose();
    }

    private static BitmapImage CreateIcon(int heading)
    {
        using var stream = AircraftIconCatalog.Open(heading);
        return Decode(stream);
    }

    private static BitmapImage Decode(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
