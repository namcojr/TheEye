using TheEye.Core.Api;
using TheEye.Core.Models;

namespace TheEye.Core.Services;

/// <summary>
/// Polls the IVAO tracker summary endpoints and exposes one consistent snapshot
/// to the UI. The three public endpoints are fetched concurrently so one slow
/// response does not hold up the others.
/// </summary>
public sealed class TrackerService : IDisposable
{
    private readonly IvaoApiClient _api;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Task? _loop;
    private int _started;
    private int _disposed;

    public TrackerService(IvaoApiClient api, TimeSpan? refreshInterval = null)
    {
        _api = api;
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
    /// without a credential, so the shell only switches this on once the user
    /// has signed in or configured a consumer key.
    /// </summary>
    public bool IncludeIdentity { get; set; }

    public TrackerSnapshot? LastSnapshot { get; private set; }

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _loop = RunAsync(_cts.Token);
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

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await RefreshSafelyAsync(cancellationToken);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
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

        var pilotsTask = FetchAsync(
            () => _api.GetPilotSummariesAsync(cancellationToken),
            Array.Empty<IvaoPilotSummary>(),
            warnings,
            "Pilots",
            cancellationToken);

        var atcTask = FetchAsync(
            () => _api.GetAtcSummariesAsync(cancellationToken),
            Array.Empty<IvaoAtcSummary>(),
            warnings,
            "ATC",
            cancellationToken);

        var statsTask = FetchAsync(
            () => _api.GetConnectionStatsAsync(cancellationToken),
            null,
            warnings,
            "Statistics",
            cancellationToken);

        var identityTask = IncludeIdentity
            ? FetchIdentitiesAsync(warnings, cancellationToken)
            : Task.FromResult(IdentityFeeds.None);

        await Task.WhenAll(pilotsTask, atcTask, statsTask);

        var pilots = pilotsTask.Result.Select(p => p.ToPilot()).ToList();
        var atc = atcTask.Result.Select(a => a.ToAtc()).ToList();
        var stats = statsTask.Result ?? CreateFallbackStats(pilots, atc);

        var identity = identityTask.IsCompleted ? identityTask.Result : await identityTask;
        MergeMembers(pilots, identity.Pilots);
        MergeStations(atc, identity.Atc);

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
            identity.Supervisors,
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

    private static async Task<T> FetchAsync<T>(
        Func<Task<T>> fetch,
        T fallback,
        List<string> warnings,
        string label,
        CancellationToken cancellationToken)
    {
        try
        {
            return await fetch();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The public feeds and the three identity feeds are fetched
            // concurrently, so more than one of them can report a failure at
            // the same moment. The list is guarded rather than appended to, so
            // two warnings never race for the same slot.
            lock (warnings)
            {
                warnings.Add($"{label}: {ex.Message}");
            }

            return fallback;
        }
    }

    /// <summary>
    /// The three authenticated feeds, fetched together. A failure leaves its
    /// list empty and adds a warning: the names are a nicety, and the map is
    /// still worth painting without them.
    /// </summary>
    private async Task<IdentityFeeds> FetchIdentitiesAsync(List<string> warnings, CancellationToken cancellationToken)
    {
        var pilotsTask = FetchAsync(
            () => _api.GetPilotsAsync(cancellationToken),
            (IReadOnlyList<IvaoPilot>)Array.Empty<IvaoPilot>(),
            warnings,
            "Member names",
            cancellationToken);

        var atcTask = FetchAsync(
            () => _api.GetAtcsAsync(cancellationToken),
            (IReadOnlyList<IvaoAtc>)Array.Empty<IvaoAtc>(),
            warnings,
            "Controller names",
            cancellationToken);

        var supervisorsTask = FetchAsync(
            () => _api.GetSupervisorsAsync(cancellationToken),
            (IReadOnlyList<IvaoSupervisor>)Array.Empty<IvaoSupervisor>(),
            warnings,
            "Supervisors",
            cancellationToken);

        await Task.WhenAll(pilotsTask, atcTask, supervisorsTask);

        return new IdentityFeeds(pilotsTask.Result, atcTask.Result, supervisorsTask.Result);
    }

    /// <summary>
    /// Copies the member details of the identity feed onto the summary records,
    /// matched by session id (the summaries and the full feeds describe the
    /// same connection).
    /// </summary>
    private static void MergeMembers(List<IvaoPilot> pilots, IReadOnlyList<IvaoPilot> identities)
    {
        if (pilots.Count == 0 || identities.Count == 0)
        {
            return;
        }

        var bySession = new Dictionary<long, IvaoPilot>(identities.Count);
        foreach (var identity in identities)
        {
            bySession[identity.Id] = identity;
        }

        foreach (var pilot in pilots)
        {
            if (bySession.TryGetValue(pilot.Id, out var identity))
            {
                pilot.ApplyMember(identity);
            }
        }
    }

    /// <summary>Same as <see cref="MergeMembers"/>, for the ATC stations.</summary>
    private static void MergeStations(List<IvaoAtc> atc, IReadOnlyList<IvaoAtc> identities)
    {
        if (atc.Count == 0 || identities.Count == 0)
        {
            return;
        }

        var bySession = new Dictionary<long, IvaoAtc>(identities.Count);
        foreach (var identity in identities)
        {
            bySession[identity.Id] = identity;
        }

        foreach (var station in atc)
        {
            if (bySession.TryGetValue(station.Id, out var identity))
            {
                station.ApplyMember(identity);
            }
        }
    }

    private sealed record IdentityFeeds(
        IReadOnlyList<IvaoPilot> Pilots,
        IReadOnlyList<IvaoAtc> Atc,
        IReadOnlyList<IvaoSupervisor> Supervisors)
    {
        public static IdentityFeeds None { get; } = new(
            Array.Empty<IvaoPilot>(),
            Array.Empty<IvaoAtc>(),
            Array.Empty<IvaoSupervisor>());
    }
}

