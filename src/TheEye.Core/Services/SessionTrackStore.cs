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
    /// Returns the recorded track of <paramref name="sessionId"/>. The tracks
    /// feed is an authenticated endpoint: IVAO answers 401 unless the request
    /// carries an IVAO session (the token WebEye gets from its login) or a
    /// personal consumer key, so the refusal is reported instead of hidden.
    /// A failure is cached for the same short window as a success: a session
    /// without track data must not trigger a request on every refresh.
    /// </summary>
    public Task<SessionTrackResult> GetTracksAsync(
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

    private async Task<SessionTrackResult> FetchAsync(long sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var tracks = await _api.GetSessionTracksAsync(sessionId, cancellationToken);
            return new SessionTrackResult(tracks, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IvaoApiException ex)
        {
            // 401 means the request itself was not authenticated, which is a
            // different problem from a session that simply has no track: the
            // caller needs the status to tell the user which one it hit.
            return new SessionTrackResult(null, ex.StatusCode);
        }
        catch
        {
            // A dropped connection or a malformed payload: nothing to trace.
            return new SessionTrackResult(null, null);
        }
    }

    private sealed record CacheEntry(Task<SessionTrackResult> Track, DateTimeOffset RequestedAt);
}

/// <summary>
/// Outcome of a recorded-track request. <see cref="Tracks"/> is null when IVAO
/// did not return a track; <see cref="StatusCode"/> then holds the HTTP status
/// of the refusal, so the map can explain the missing trail instead of silently
/// drawing the filed route.
/// </summary>
public sealed record SessionTrackResult(IReadOnlyList<IvaoPilotTrack>? Tracks, int? StatusCode)
{
    /// <summary>
    /// True when the payload holds enough positions to trace a trail. A single
    /// point is not a trail, so it counts as missing.
    /// </summary>
    public bool HasTrack => Tracks is { Count: >= 2 };
}
