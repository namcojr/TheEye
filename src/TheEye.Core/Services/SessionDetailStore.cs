using TheEye.Core.Api;
using TheEye.Core.Models;

namespace TheEye.Core.Services;

/// <summary>
/// Fetches the record of a single session on demand — who is flying, the
/// ratings, the home division and the connected software. The tracker summary
/// already carries the flight plan and the last position, so this is only asked
/// for the flight the user has selected, and the answer is cached for a minute:
/// identity and software do not change while a member stays connected, and the
/// time online is recomputed from the connection timestamp instead of the
/// cached count.
/// </summary>
public sealed class SessionDetailStore
{
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(1);

    private readonly IvaoApiClient _api;
    private readonly TimeSpan _cacheDuration;
    private readonly Dictionary<long, CacheEntry> _cache = [];
    private readonly System.Threading.Lock _gate = new();

    public SessionDetailStore(IvaoApiClient api, TimeSpan? cacheDuration = null)
    {
        _api = api;
        _cacheDuration = cacheDuration is { TotalSeconds: > 0 } duration ? duration : DefaultCacheDuration;
    }

    public TimeSpan CacheDuration => _cacheDuration;

    /// <summary>
    /// Returns the record of <paramref name="sessionId"/>. Like the tracks feed
    /// this endpoint is authenticated: IVAO answers 401 unless the request
    /// carries an IVAO session or a personal consumer key, so the refusal is
    /// reported rather than hidden. A failure is cached for the same short
    /// window as a success, so a session IVAO will not describe is not requested
    /// again on every refresh.
    /// </summary>
    public Task<SessionDetailResult> GetSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(sessionId, out var cached) &&
                (!cached.Session.IsCompleted || DateTimeOffset.UtcNow - cached.RequestedAt < _cacheDuration))
            {
                return cached.Session;
            }

            var entry = new CacheEntry(FetchAsync(sessionId, cancellationToken), DateTimeOffset.UtcNow);
            _cache[sessionId] = entry;
            return entry.Session;
        }
    }

    /// <summary>Forgets every cached record, e.g. after the API key changed.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
        }
    }

    private async Task<SessionDetailResult> FetchAsync(long sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _api.GetSessionAsync(sessionId, cancellationToken);
            return new SessionDetailResult(session, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IvaoApiException ex)
        {
            // The status tells the card whether the caller is not signed in (401)
            // or the session is gone (404), which are different things to say.
            return new SessionDetailResult(null, ex.StatusCode);
        }
        catch (Exception ex)
        {
            // A dropped connection or a payload this app cannot read. The reason
            // is kept, briefly, because a record that fails to deserialize is a
            // bug worth naming rather than a card that silently stays empty.
            return new SessionDetailResult(null, null, Describe(ex));
        }
    }

    private static string Describe(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

        // JsonException already names the member and the path that broke; the
        // card has room for a sentence, not for a stack of them.
        return message.Length <= 160 ? message : message[..160];
    }

    private sealed record CacheEntry(Task<SessionDetailResult> Session, DateTimeOffset RequestedAt);
}

/// <summary>
/// Outcome of a session-record request. <see cref="Session"/> is null when IVAO
/// did not describe the session; <see cref="StatusCode"/> then holds the HTTP
/// status of the refusal, which is what the card shows to explain the gap, and
/// <see cref="Error"/> the reason a request that never got a status failed
/// (dropped connection, payload the model cannot read).
/// </summary>
public sealed record SessionDetailResult(IvaoSessionDetail? Session, int? StatusCode, string? Error = null);
