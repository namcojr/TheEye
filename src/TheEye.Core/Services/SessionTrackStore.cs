using TheEye.Core.Api;
using TheEye.Core.Models;

namespace TheEye.Core.Services;

/// <summary>
/// Fetches the recorded track of a single session on demand. The tracker is
/// polled for summaries every refresh, but a full track is only useful for the
/// flight or station the user has selected, so results are cached briefly and
/// reused by the next refresh instead of being requested again.
/// </summary>
public sealed class SessionTrackStore
{
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromSeconds(20);

    private readonly IvaoApiClient _api;
    private readonly TimeSpan _cacheDuration;
    private readonly Dictionary<long, CacheEntry> _cache = [];
    private readonly System.Threading.Lock _gate = new();

    public SessionTrackStore(IvaoApiClient api, TimeSpan? cacheDuration = null)
    {
        _api = api;
        _cacheDuration = cacheDuration is { TotalSeconds: > 0 } duration ? duration : DefaultCacheDuration;
    }

    public TimeSpan CacheDuration => _cacheDuration;

    /// <summary>
    /// Returns the recorded track of <paramref name="sessionId"/>, or null when
    /// the API does not expose it. The tracks endpoint needs a consumer key and
    /// answers 401 without one, so a failure is cached for the same short window
    /// as a success: a flight without track data must not trigger a request on
    /// every refresh.
    /// </summary>
    public Task<IReadOnlyList<IvaoPilotTrack>?> GetTracksAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(sessionId, out var cached) &&
                (!cached.Track.IsCompleted || DateTimeOffset.UtcNow - cached.RequestedAt < _cacheDuration))
            {
                return cached.Track;
            }

            var entry = new CacheEntry(FetchAsync(sessionId, cancellationToken), DateTimeOffset.UtcNow);
            _cache[sessionId] = entry;
            return entry.Track;
        }
    }

    /// <summary>Forgets every cached track, e.g. after the API key changed.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
        }
    }

    private async Task<IReadOnlyList<IvaoPilotTrack>?> FetchAsync(long sessionId, CancellationToken cancellationToken)
    {
        try
        {
            return await _api.GetSessionTracksAsync(sessionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Without a consumer key, or for a session the tracker has already
            // dropped, there is simply nothing to trace.
            return null;
        }
    }

    private sealed record CacheEntry(Task<IReadOnlyList<IvaoPilotTrack>?> Track, DateTimeOffset RequestedAt);
}
