using TheEye.Core.Api;
using TheEye.Core.Models;

namespace TheEye.Core.Services;

/// <summary>
/// Fetches the newest flight plan of a single session on demand. The tracker
/// summary the list is built from carries the airports and the route only, so
/// the rest of the filing — the times, the endurance, the alternate and the
/// equipment — is asked for the aircraft the user has selected and cached for a
/// minute. A pilot amends a plan rarely, and the answer is only shown, never
/// counted on, so a minute-old revision is as good as a fresh one.
/// </summary>
public sealed class SessionFlightPlanStore
{
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(1);

    private readonly IvaoApiClient _api;
    private readonly TimeSpan _cacheDuration;
    private readonly Dictionary<long, CacheEntry> _cache = [];
    private readonly System.Threading.Lock _gate = new();

    public SessionFlightPlanStore(IvaoApiClient api, TimeSpan? cacheDuration = null)
    {
        _api = api;
        _cacheDuration = cacheDuration is { TotalSeconds: > 0 } duration ? duration : DefaultCacheDuration;
    }

    public TimeSpan CacheDuration => _cacheDuration;

    /// <summary>
    /// Returns the flight plan of <paramref name="sessionId"/>. The endpoint is
    /// authenticated like the other session feeds: 401 means the caller is not
    /// signed in, 404 that the session has no flight plan (an ATC position, or a
    /// flight that just disconnected). Both are retained so the card can show
    /// the appropriate value, and both are cached for the same short window as a
    /// success so the answer is not requested again on every refresh.
    /// </summary>
    public Task<SessionFlightPlanResult> GetFlightPlanAsync(
        long sessionId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(sessionId, out var cached) &&
                (!cached.FlightPlan.IsCompleted || DateTimeOffset.UtcNow - cached.RequestedAt < _cacheDuration))
            {
                return cached.FlightPlan;
            }

            var entry = new CacheEntry(FetchAsync(sessionId, cancellationToken), DateTimeOffset.UtcNow);
            _cache[sessionId] = entry;
            return entry.FlightPlan;
        }
    }

    /// <summary>Forgets every cached flight plan.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
        }
    }

    private async Task<SessionFlightPlanResult> FetchAsync(long sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var flightPlan = await _api.GetSessionFlightPlanAsync(sessionId, cancellationToken);
            return new SessionFlightPlanResult(flightPlan, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IvaoApiException ex)
        {
            return new SessionFlightPlanResult(null, ex.StatusCode);
        }
        catch (Exception ex)
        {
            // A dropped connection, or a payload this app cannot read. The reason
            // is kept, briefly, because a plan that fails to deserialize is a bug
            // worth naming rather than a card that silently stays empty.
            return new SessionFlightPlanResult(null, null, Describe(ex));
        }
    }

    private static string Describe(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

        return message.Length <= 160 ? message : message[..160];
    }

    private sealed record CacheEntry(Task<SessionFlightPlanResult> FlightPlan, DateTimeOffset RequestedAt);
}

/// <summary>
/// Outcome of a flight-plan request. <see cref="FlightPlan"/> is null when IVAO
/// did not describe one; <see cref="StatusCode"/> then holds the HTTP status of
/// the refusal, and <see cref="Error"/> the reason a request that never got a
/// status failed (dropped connection, payload the model cannot read).
/// </summary>
public sealed record SessionFlightPlanResult(IvaoFlightPlan? FlightPlan, int? StatusCode, string? Error = null);
