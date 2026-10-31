using TheEye.Presentation.Threading;

namespace TheEye.Presentation.Services;

/// <summary>
/// Fills the badge and flag slots of a session card. Whatever
/// <see cref="IImageCache"/> already decoded is painted at once, and whatever is
/// not is fetched in the background and dropped in when it arrives, so an answer
/// from IVAO never waits for a picture. Both the flight card and the station card
/// use this, because both show the same two images.
/// </summary>
internal static class SessionImageBinder
{
    /// <summary>
    /// Applies the picture at <paramref name="url"/> to <paramref name="apply"/>
    /// on the UI thread. A missing picture leaves the slot empty rather than
    /// showing a broken image.
    /// </summary>
    public static async Task ApplyAsync(IImageCache images, string? url, Action<object?> apply)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        object? image;
        try
        {
            image = await images.GetAsync(url);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (image is null)
        {
            // Nothing was published for this rating or division, which is not a
            // reason to show a broken picture: the slot simply stays empty.
            return;
        }

        UiThread.Post(() => apply(image));
    }
}
