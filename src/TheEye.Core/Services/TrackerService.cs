using TheEye.Core.Api;
using TheEye.Core.Models;

namespace TheEye.Core.Services;

/// <summary>
/// Polls the IVAO tracker summary endpoints and exposes one consistent snapshot
/// to the UI. The traffic, statistics and optional supervisor feeds are fetched
/// concurrently so one slow response does not hold up the others.
/// </summary>
public sealed class TrackerService : IDisposable
{
    private readonly IvaoApiClient _api;
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, CachedFeed> _feedCache = [];
    private readonly System.Threading.Lock _cacheGate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Task? _loop;
    private int _started;
    private int _disposed;

    public TrackerService(IvaoApiClient api, TimeSpan? refreshInterval = null, TimeProvider? timeProvider = null)
    {
        _api = api;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interval = refreshInterval is { TotalSeconds: >= 5 } interval
            ? interval
            : TimeSpan.FromSeconds(30);
    }

    public event EventHandler<TrackerSnapshot>? SnapshotUpdated;

    public event EventHandler<TrackerRefreshException>? RefreshFailed;

    public TimeSpan RefreshInterval => _interval;

    /// <summary>
    /// True when the authenticated identity feeds should be fetched alongside
    /// the public summaries: the member behind each callsign, the software they
    /// are connected with and the connected supervisors. Those feeds answer 401
    /// without a sign-in, so the shell only switches this on once the user has
    /// signed in.
    /// </summary>
    public bool IncludeIdentity { get; set; }

    public TrackerSnapshot? LastSnapshot { get; private set; }

    public void Start(bool refreshImmediately = true)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _loop = RunAsync(refreshImmediately, _cts.Token);
    }

    /// <summary>
    /// Fetches a snapshot immediately. Safe to call while the periodic loop is
    /// running; overlapping calls are serialized.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            await FetchAndPublishAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _cts.Cancel();
        _cts.Dispose();
        _refreshGate.Dispose();
    }

    private async Task RunAsync(bool refreshImmediately, CancellationToken cancellationToken)
    {
        try
        {
            if (refreshImmediately)
            {
                await RefreshSafelyAsync(cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_interval, cancellationToken);
                await RefreshSafelyAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the window closes.
        }
    }

    private async Task RefreshSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the window closes.
        }
        catch (Exception ex)
        {
            try
            {
                RefreshFailed?.Invoke(this, new TrackerRefreshException(
                    $"Could not refresh IVAO traffic: {ex.Message}",
                    [ex.Message],
                    ex));
            }
            catch
            {
                // A failing UI handler must not kill the polling loop.
            }
        }
    }

    private async Task FetchAndPublishAsync(CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var pilotsTask = FetchPilotsAsync(warnings, cancellationToken);
        var atcTask = FetchAtcAsync(warnings, cancellationToken);
        var statsTask = FetchCachedAsync(
            () => _api.GetConnectionStatsAsync(cancellationToken),
            null,
            warnings,
            "Statistics",
            _interval,
            cancellationToken);
        var supervisorsTask = IncludeIdentity
            ? FetchCachedAsync(
                () => _api.GetSupervisorsAsync(cancellationToken),
                (IReadOnlyList<IvaoSupervisor>)Array.Empty<IvaoSupervisor>(),
                warnings,
                "Supervisors",
                TimeSpan.FromMinutes(2),
                cancellationToken)
            : Task.FromResult<IReadOnlyList<IvaoSupervisor>>(Array.Empty<IvaoSupervisor>());
        var pilotIdentitiesTask = IncludeIdentity
            ? FetchCachedAsync(
                () => _api.GetPilotsAsync(cancellationToken),
                (IReadOnlyList<IvaoPilot>)Array.Empty<IvaoPilot>(),
                warnings,
                "Member names",
                TimeSpan.FromMinutes(2),
                cancellationToken)
            : Task.FromResult<IReadOnlyList<IvaoPilot>>(Array.Empty<IvaoPilot>());
        var atcIdentitiesTask = IncludeIdentity
            ? FetchCachedAsync(
                () => _api.GetAtcsAsync(cancellationToken),
                (IReadOnlyList<IvaoAtc>)Array.Empty<IvaoAtc>(),
                warnings,
                "Controller names",
                TimeSpan.FromMinutes(2),
                cancellationToken)
            : Task.FromResult<IReadOnlyList<IvaoAtc>>(Array.Empty<IvaoAtc>());

        await Task.WhenAll(pilotsTask, atcTask, statsTask, supervisorsTask, pilotIdentitiesTask, atcIdentitiesTask);

        var pilots = pilotsTask.Result;
        var atc = atcTask.Result;
        var pilotIdentities = pilotIdentitiesTask.Result.ToDictionary(pilot => pilot.Id);
        var atcIdentities = atcIdentitiesTask.Result.ToDictionary(station => station.Id);
        foreach (var pilot in pilots)
        {
            if (pilotIdentities.TryGetValue(pilot.Id, out var identity))
            {
                pilot.ApplyMember(identity);
            }
        }

        foreach (var station in atc)
        {
            if (atcIdentities.TryGetValue(station.Id, out var identity))
            {
                station.ApplyMember(identity);
            }
        }

        var stats = statsTask.Result ?? CreateFallbackStats(pilots, atc);

        if (pilots.Count == 0 && atc.Count == 0 && statsTask.Result is null)
        {
            throw new TrackerRefreshException(
                "The IVAO API did not return any traffic data.",
                warnings,
                new AggregateException(warnings.Select(w => new InvalidOperationException(w))));
        }

        var snapshot = new TrackerSnapshot(
            pilots,
            atc,
            supervisorsTask.Result,
            stats,
            DateTimeOffset.UtcNow,
            warnings);

        LastSnapshot = snapshot;

        try
        {
            SnapshotUpdated?.Invoke(this, snapshot);
        }
        catch
        {
            // A failing UI handler must not kill the polling loop.
        }
    }

    private static IvaoConnectionStats CreateFallbackStats(
        IReadOnlyCollection<IvaoPilot> pilots,
        IReadOnlyCollection<IvaoAtc> atc)
    {
        var counts = new IvaoConnectionCounts
        {
            Pilots = pilots.Count,
            Atc = atc.Count,
            Total = pilots.Count + atc.Count
        };

        return new IvaoConnectionStats
        {
            Now = counts,
            Max = new IvaoConnectionCounts
            {
                Pilots = counts.Pilots,
                Atc = counts.Atc,
                Total = counts.Total
            }
        };
    }

    private async Task<List<IvaoPilot>> FetchPilotsAsync(
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var summaries = await FetchCachedAsync(
            () => _api.GetPilotSummariesAsync(cancellationToken),
            (IReadOnlyList<IvaoPilotSummary>)Array.Empty<IvaoPilotSummary>(),
            warnings,
            "Pilots",
            _interval,
            cancellationToken);
        return summaries.Select(pilot => pilot.ToPilot()).ToList();
    }

    private async Task<List<IvaoAtc>> FetchAtcAsync(
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var summaries = await FetchCachedAsync(
            () => _api.GetAtcSummariesAsync(cancellationToken),
            (IReadOnlyList<IvaoAtcSummary>)Array.Empty<IvaoAtcSummary>(),
            warnings,
            "ATC",
            _interval,
            cancellationToken);
        return summaries.Select(station => station.ToAtc()).ToList();
    }

    private async Task<T> FetchCachedAsync<T>(
        Func<Task<T>> fetch,
        T fallback,
        List<string> warnings,
        string label,
        TimeSpan cacheDuration,
        CancellationToken cancellationToken)
    {
        CachedFeed? cached;
        lock (_cacheGate)
        {
            _feedCache.TryGetValue(label, out cached);
        }

        if (cached is not null && _timeProvider.GetUtcNow() < cached.NextAttempt)
        {
            AddWarning(warnings, cached.Warning);
            return (T)cached.Value!;
        }

        try
        {
            var value = await fetch();
            lock (_cacheGate)
            {
                _feedCache[label] = new CachedFeed(value, _timeProvider.GetUtcNow() + cacheDuration, null, 0);
            }

            return value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IvaoApiException ex) when (ex.IsUnauthorized)
        {
            var failures = (cached?.Failures ?? 0) + 1;
            var retryAt = _timeProvider.GetUtcNow() + TimeSpan.FromMinutes(10);
            var warning = failures >= 2
                ? $"Repeated {label}: {ex.Message} Next attempt at {retryAt.ToLocalTime():HH:mm:ss}; showing last available data."
                : null;
            lock (_cacheGate)
            {
                _feedCache[label] = new CachedFeed(fallback, retryAt, warning, failures);
            }

            AddWarning(warnings, warning);
            return fallback;
        }
        catch (Exception ex)
        {
            var failures = Math.Min((cached?.Failures ?? 0) + 1, 5);
            var retryDelay = TimeSpan.FromMinutes(Math.Min(Math.Pow(2, failures - 1), 10));
            var repeatedStatus = ex is IvaoApiException && failures >= 2 ? "Repeated " : string.Empty;
            var warning = $"{repeatedStatus}{label}: {ex.Message} Next attempt at {(_timeProvider.GetUtcNow() + retryDelay).ToLocalTime():HH:mm:ss}; showing last available data.";
            var value = cached is null ? fallback : (T)cached.Value!;
            lock (_cacheGate)
            {
                _feedCache[label] = new CachedFeed(value, _timeProvider.GetUtcNow() + retryDelay, warning, failures);
            }

            AddWarning(warnings, warning);
            return value;
        }
    }

    private static void AddWarning(List<string> warnings, string? warning)
    {
        if (warning is null) return;
        lock (warnings)
        {
            warnings.Add(warning);
        }
    }

    private sealed record CachedFeed(object? Value, DateTimeOffset NextAttempt, string? Warning, int Failures);
}

