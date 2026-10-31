using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using TheEye.Core.Maps;

namespace TheEye.Windows.Services;

/// <summary>
/// Decodes the bundled SAS Acars PNG for each heading once and shares it across
/// all pilot markers. Images are frozen so they can be created off the UI thread.
/// </summary>
public sealed class AircraftIconCache
{
    private readonly ConcurrentDictionary<int, BitmapImage> _icons = new();

    public BitmapImage Get(int? heading)
    {
        var normalized = AircraftIconCatalog.NormalizeHeading(heading);
        return _icons.GetOrAdd(normalized, Create);
    }

    private static BitmapImage Create(int heading)
    {
        using var stream = AircraftIconCatalog.Open(heading);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
