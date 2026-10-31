using System.Collections.Concurrent;
using TheEye.Core.Maps;

namespace TheEye.Presentation.Services;

/// <summary>
/// Decodes the bundled SAS Acars PNG for each heading once and shares it across
/// all pilot markers. The decoded frame belongs to the shell (WPF freezes a
/// <c>BitmapImage</c>; Avalonia keeps an immutable <c>Bitmap</c>), so the shell
/// supplies the factory and can decode off the UI thread.
/// </summary>
public sealed class AircraftIconCache<TImage> : IAircraftIconCache
    where TImage : class
{
    private readonly ConcurrentDictionary<int, TImage> _icons = new();
    private readonly Func<int, TImage> _create;

    public AircraftIconCache(Func<int, TImage> create) =>
        _create = create ?? throw new ArgumentNullException(nameof(create));

    public object? Get(int? heading) =>
        _icons.GetOrAdd(AircraftIconCatalog.NormalizeHeading(heading), _create);
}
