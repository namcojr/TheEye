using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace TheEye.Windows.Services;

/// <summary>
/// Downloads the small pictures a pilot card shows — IVAO rating badges and
/// national flags — once per address and shares the frozen frames between
/// selections. A picture that cannot be fetched is remembered as missing, so a
/// badge IVAO does not publish is not requested again for every member who
/// holds it. Nothing is written to disk: these files are a few kilobytes and
/// the memory lasts as long as the window.
/// </summary>
public sealed class RemoteImageCache : IDisposable
{
    private readonly ConcurrentDictionary<string, BitmapImage> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<BitmapImage?>> _requests = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public RemoteImageCache(HttpClient? httpClient = null)
    {
        // A client handed in belongs to the caller and is left alone; one made
        // here is this cache's to dispose, which is what the window does when it
        // closes. A shared static client would be disposed by the first window
        // that closed and left every later one without a connection.
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? CreateClient();
    }

    /// <summary>
    /// The picture when it is already decoded, so a card that is drawn again can
    /// paint its badge and flag without a blank frame first.
    /// </summary>
    public BitmapImage? TryGet(string? url) =>
        !string.IsNullOrWhiteSpace(url) && _images.TryGetValue(url, out var image) ? image : null;

    /// <summary>
    /// The picture at <paramref name="url"/>, or null when it cannot be fetched.
    /// Concurrent callers share one request, and a failure is remembered for the
    /// rest of the run; cancelling one caller does not cancel the download the
    /// others are waiting for.
    /// </summary>
    public Task<BitmapImage?> GetAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<BitmapImage?>(null);
        }

        if (_images.TryGetValue(url, out var cached))
        {
            return Task.FromResult<BitmapImage?>(cached);
        }

        var request = _requests.GetOrAdd(url, DownloadAsync);
        return cancellationToken.CanBeCanceled ? request.WaitAsync(cancellationToken) : request;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private async Task<BitmapImage?> DownloadAsync(string url)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            // The stream of a network read cannot be seeked, and WPF cannot finish
            // loading — let alone freeze — a picture from a stream it cannot
            // rewind: the frame stayed unfreezable and the card silently showed
            // nothing. A badge or a flag is a few kilobytes, so the bytes are
            // pulled into memory and decoded from there.
            var bytes = await response.Content.ReadAsByteArrayAsync();
            using var buffer = new MemoryStream(bytes, writable: false);
            var image = Decode(buffer);
            _images[url] = image;
            return image;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException
            or NotSupportedException or FormatException or ArgumentException or InvalidOperationException)
        {
            // A badge that 404s, a blocked host, a timeout or a body that is not
            // an image: the card simply has nothing to draw.
            return null;
        }
    }

    private static BitmapImage Decode(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        // OnLoad reads the whole picture at EndInit, which is what allows the
        // frame to be frozen and handed to the UI thread.
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static HttpClient CreateClient()
    {
        // The badges are served from IVAO's own site and flagcdn.com; both answer
        // a plain GET, and a named client keeps the request out of the anonymous
        // traffic some hotlink filters drop.
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TheEye/1.0");
        return client;
    }
}
