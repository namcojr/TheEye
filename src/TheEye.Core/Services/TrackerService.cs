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

        await Task.WhenAll(pilotsTask, atcTask, statsTask);

        var pilots = pilotsTask.Result.Select(p => p.ToPilot()).ToList();
        var atc = atcTask.Result.Select(a => a.ToAtc()).ToList();
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
            warnings.Add($"{label}: {ex.Message}");
            return fallback;
        }
    }
}

