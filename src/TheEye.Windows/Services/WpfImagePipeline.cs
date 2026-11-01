using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TheEye.Core.Maps;
using TheEye.Presentation.Services;

namespace TheEye.Windows.Services;

/// <summary>
/// The WPF decoders for the pictures the shared view models ask for: the bulk
/// aircraft SVG becomes frozen native geometry once per category, and badges and flags come
/// from the network. The cache objects themselves are shared code.
/// </summary>
public sealed class WpfImagePipeline : IImagePipeline
{
    public WpfImagePipeline()
    {
        AircraftIcons = new AircraftIconCache<Geometry>(CreateIcon);
        SessionImages = new RemoteImageCache<BitmapImage>(Decode);
    }

    public IAircraftIconCache AircraftIcons { get; }

    public IImageCache SessionImages { get; }

    public void Dispose()
    {
        SessionImages.Dispose();
    }

    private static Geometry CreateIcon(AircraftGlyph glyph)
    {
        var geometry = Geometry.Parse("F1 " + AircraftIconCatalog.PathData(glyph));
        geometry.Freeze();
        return geometry;
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
