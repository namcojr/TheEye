using System.Collections.Concurrent;
using TheEye.Core.Maps;

namespace TheEye.Presentation.Services;

/// <summary>
/// Shares native vector geometry across markers. Heading and theme colour
/// belong to the marker template, not the category cache.
/// </summary>
public sealed class AircraftIconCache<TImage> : IAircraftIconCache
    where TImage : class
{
    private readonly ConcurrentDictionary<AircraftGlyph, Lazy<TImage>> _icons = new();
    private readonly Func<AircraftGlyph, TImage> _create;

    public AircraftIconCache(Func<AircraftGlyph, TImage> create) =>
        _create = create ?? throw new ArgumentNullException(nameof(create));

    public object Get(AircraftGlyph glyph) =>
        _icons.GetOrAdd(glyph, category => new Lazy<TImage>(() => _create(category))).Value;
}
