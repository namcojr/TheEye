using TheEye.Core.Maps;

namespace TheEye.Presentation.Services;

/// <summary>
/// The pictures a session card shows: remote rating badges and flags. The
/// decoded picture is a platform type (a WPF <c>BitmapImage</c>, an Avalonia
/// <c>Bitmap</c>), so it is handed to the view as an object and each shell
/// binds it where its own image control expects it.
/// </summary>
public interface IImageCache : IDisposable
{
    /// <summary>The picture when it is already decoded, so a redrawn card paints at once.</summary>
    object? TryGet(string? url);

    /// <summary>The picture at <paramref name="url"/>, or null when it cannot be fetched.</summary>
    Task<object?> GetAsync(string? url, CancellationToken cancellationToken = default);
}

/// <summary>The native vector geometry, decoded once per aircraft category.</summary>
public interface IAircraftIconCache
{
    object Get(AircraftGlyph glyph);
}

/// <summary>
/// Both caches the map needs, owned by the shell that knows how to decode
/// pictures. <see cref="MapViewModel"/> disposes the pipeline with itself.
/// </summary>
public interface IImagePipeline : IDisposable
{
    IAircraftIconCache AircraftIcons { get; }

    IImageCache SessionImages { get; }
}
