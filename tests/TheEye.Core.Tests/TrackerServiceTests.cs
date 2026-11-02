using System.Collections.Concurrent;
using System.Net;
using System.Text;
using TheEye.Core.Api;
using TheEye.Core.Services;

namespace TheEye.Core.Tests;

public sealed class TrackerServiceTests
{
    private const string PilotsPath = "/v2/tracker/now/pilots/summary";
    private const string AtcPath = "/v2/tracker/now/atc/summary";
    private const string StatsPath = "/v2/tracker/now/connections/stats";
    private const string PilotIdentityPath = "/v2/tracker/now/pilots";
    private const string AtcIdentityPath = "/v2/tracker/now/atc";
    private const string SupervisorsPath = "/v2/tracker/now/supervisors";

    [Fact]
    public async Task RefreshAsync_FetchesAllEndpointsAndPublishesSnapshot()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            PilotsPath => Json(Fixture("pilots-summary.json")),
            AtcPath => Json(Fixture("atc-summary.json")),
            StatsPath => Json(Fixture("stats.json")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api);
        TrackerSnapshot? published = null;
        var updates = 0;
        service.SnapshotUpdated += (_, snapshot) =>
        {
            published = snapshot;
            updates++;
        };

        await service.RefreshAsync();

        Assert.NotNull(published);
        Assert.Equal(2, published.PilotCount);
        Assert.Equal(1, published.AtcCount);
        Assert.Equal(3, published.Stats.Now.Total);
        Assert.Empty(published.Warnings);
        Assert.False(published.IsPartial);
        Assert.Empty(published.Supervisors);
        Assert.Equal(1, updates);
        Assert.Same(published, service.LastSnapshot);
        Assert.Equal("TEST123", published.Pilots[0].Callsign);
        Assert.Equal("EHAM_TWR", published.Atc[0].Callsign);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains(handler.Requests, request => request.Path == PilotsPath);
        Assert.Contains(handler.Requests, request => request.Path == AtcPath);
        Assert.Contains(handler.Requests, request => request.Path == StatsPath);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("https://test.origin", request.Origin);
            Assert.Null(request.ApiKey);
        });
    }

    [Fact]
    public async Task RefreshAsync_WhenIdentityIsIncluded_MergesMembersAndPublishesSupervisors()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            PilotsPath => Json(Fixture("pilots-summary.json")),
            AtcPath => Json(Fixture("atc-summary.json")),
            StatsPath => Json(Fixture("stats.json")),
            PilotIdentityPath => Json(Fixture("pilots-identity.json")),
            AtcIdentityPath => Json(Fixture("atc-identity.json")),
            SupervisorsPath => Json(Fixture("supervisors.json")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api) { IncludeIdentity = true };
        TrackerSnapshot? published = null;
        service.SnapshotUpdated += (_, snapshot) => published = snapshot;

        await service.RefreshAsync();

        Assert.NotNull(published);
        Assert.Empty(published.Warnings);
        Assert.False(published.IsPartial);

        // The supervisors are their own list: they neither fly nor control, so
        // they never show up among the traffic the summaries describe.
        Assert.Equal(2, published.SupervisorCount);
        var supervisor = published.Supervisors[0];
        Assert.Equal("SUP601", supervisor.Callsign);
        Assert.Equal("Maria Ferreira", supervisor.Name);
        Assert.Equal("Observing", supervisor.ConnectionLabel);
        Assert.Equal("Director (Portugal)", supervisor.StaffText);

        // The identity feed layers the member onto the summary records, so a
        // row can say who is behind a callsign.
        Assert.Equal("Ana Silva", published.Pilots[0].MemberName);
        Assert.Equal("Altitude", published.Pilots[0].SoftwareText);
        Assert.Equal("MSFS", published.Pilots[0].SimulatorText);
        Assert.Equal("WS1-EUW2", published.Pilots[0].ServerText);
        Assert.Equal("Niels de Vries", published.Atc[0].MemberName);
        Assert.Equal("Aurora", published.Atc[0].SoftwareText);

        Assert.NotNull(published.Pilots[0].LastTrack);
        Assert.NotNull(published.Pilots[0].FlightPlan);
        Assert.NotNull(published.Atc[0].Position);
        Assert.Equal(6, handler.Requests.Count);
        Assert.Contains(handler.Requests, request => request.Path == PilotsPath);
        Assert.Contains(handler.Requests, request => request.Path == AtcPath);
        Assert.Contains(handler.Requests, request => request.Path == PilotIdentityPath);
        Assert.Contains(handler.Requests, request => request.Path == AtcIdentityPath);
        Assert.Contains(handler.Requests, request => request.Path == SupervisorsPath);
    }

    [Fact]
    public async Task RefreshAsync_WhenSupervisorFeedIsUnauthorized_KeepsTrafficWithoutWarning()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            PilotsPath => Json(Fixture("pilots-summary.json")),
            AtcPath => Json(Fixture("atc-summary.json")),
            StatsPath => Json(Fixture("stats.json")),
            PilotIdentityPath => Json(Fixture("pilots-identity.json")),
            AtcIdentityPath => Json(Fixture("atc-identity.json")),
            SupervisorsPath => new HttpResponseMessage(HttpStatusCode.Unauthorized),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api) { IncludeIdentity = true };
        TrackerSnapshot? published = null;
        service.SnapshotUpdated += (_, snapshot) => published = snapshot;

        await service.RefreshAsync();

        Assert.NotNull(published);
        Assert.Empty(published.Supervisors);
        Assert.Empty(published.Warnings);

        // Only the list that failed is lost; the traffic and the member names
        // from the feeds that did answer are still published.
        Assert.Equal(2, published.PilotCount);
        Assert.Equal("Ana Silva", published.Pilots[0].MemberName);
        Assert.False(published.IsPartial);
    }

    [Fact]
    public async Task RefreshAsync_OnlySurfacesRepeatedUnauthorizedFeedFailures()
    {
        var clock = new ManualTimeProvider();
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == SupervisorsPath
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : TrackerResponse(request.RequestUri.AbsolutePath));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api, timeProvider: clock) { IncludeIdentity = true };

        await service.RefreshAsync();
        Assert.Empty(service.LastSnapshot!.UserWarnings);

        clock.Advance(TimeSpan.FromMinutes(10));
        await service.RefreshAsync();

        Assert.Single(service.LastSnapshot!.UserWarnings);
        Assert.StartsWith("Repeated Supervisors:", service.LastSnapshot.UserWarnings[0]);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotSurfaceTransientTransportWarnings()
    {
        var clock = new ManualTimeProvider();
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == StatsPath
                ? throw new HttpRequestException("The request timed out.")
                : TrackerResponse(request.RequestUri.AbsolutePath));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api, timeProvider: clock);

        await service.RefreshAsync();

        Assert.True(service.LastSnapshot!.IsPartial);
        Assert.Contains(service.LastSnapshot.Warnings, warning => warning.StartsWith("Statistics:", StringComparison.Ordinal));
        Assert.Empty(service.LastSnapshot.UserWarnings);
    }

    [Fact]
    public async Task RefreshAsync_OnlySurfacesRepeatedNotFoundFeedFailures()
    {
        var clock = new ManualTimeProvider();
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == StatsPath
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : TrackerResponse(request.RequestUri.AbsolutePath));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api, timeProvider: clock);

        await service.RefreshAsync();
        Assert.Empty(service.LastSnapshot!.UserWarnings);

        clock.Advance(TimeSpan.FromMinutes(1));
        await service.RefreshAsync();

        Assert.Single(service.LastSnapshot!.UserWarnings);
        Assert.StartsWith("Repeated Statistics:", service.LastSnapshot.UserWarnings[0]);
    }

    [Fact]
    public async Task RefreshAsync_ReusesOptionalFeedsUntilTheirCacheExpires()
    {
        var clock = new ManualTimeProvider();
        var handler = new StubHttpMessageHandler(request => TrackerResponse(request.RequestUri!.AbsolutePath));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api, timeProvider: clock) { IncludeIdentity = true };

        await service.RefreshAsync();
        clock.Advance(TimeSpan.FromSeconds(30));
        await service.RefreshAsync();

        Assert.Equal(9, handler.Requests.Count);
        Assert.Equal("Ana Silva", service.LastSnapshot!.Pilots[0].MemberName);
        Assert.Equal(2, service.LastSnapshot.SupervisorCount);
        Assert.Single(handler.Requests, request => request.Path == PilotIdentityPath);
        Assert.Single(handler.Requests, request => request.Path == AtcIdentityPath);
        Assert.Single(handler.Requests, request => request.Path == SupervisorsPath);

        clock.Advance(TimeSpan.FromSeconds(90));
        await service.RefreshAsync();

        Assert.Equal(15, handler.Requests.Count);
    }

    [Fact]
    public async Task RefreshAsync_BackToBackCallsDoNotRepeatAnyFeed()
    {
        var clock = new ManualTimeProvider();
        var handler = new StubHttpMessageHandler(request => TrackerResponse(request.RequestUri!.AbsolutePath));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api, timeProvider: clock) { IncludeIdentity = true };

        await Task.WhenAll(service.RefreshAsync(), service.RefreshAsync());

        Assert.Equal(6, handler.Requests.Count);
        Assert.NotNull(service.LastSnapshot!.Pilots[0].LastTrack);
        Assert.NotNull(service.LastSnapshot.Atc[0].Position);
    }

    [Theory]
    [InlineData(PilotsPath, "Pilots:")]
    [InlineData(AtcPath, "ATC:")]
    [InlineData(StatsPath, "Statistics:")]
    [InlineData(PilotIdentityPath, "Member names:")]
    [InlineData(AtcIdentityPath, "Controller names:")]
    [InlineData(SupervisorsPath, "Supervisors:")]
    public async Task RefreshAsync_BacksOffEveryFailingFeedAndRetainsItsLastData(string failedPath, string warningPrefix)
    {
        var clock = new ManualTimeProvider();
        var fail = false;
        var handler = new StubHttpMessageHandler(request =>
            fail && request.RequestUri!.AbsolutePath == failedPath
                ? throw new HttpRequestException("An error occurred while sending the request.")
                : TrackerResponse(request.RequestUri!.AbsolutePath));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api, timeProvider: clock) { IncludeIdentity = true };

        await service.RefreshAsync();
        fail = true;
        clock.Advance(TimeSpan.FromMinutes(2));
        await service.RefreshAsync();
        Assert.Equal(2, handler.Requests.Count(request => request.Path == failedPath));
        Assert.Contains(service.LastSnapshot!.Warnings, warning => warning.StartsWith(warningPrefix, StringComparison.Ordinal));
        Assert.Equal(2, service.LastSnapshot.PilotCount);
        Assert.Equal(1, service.LastSnapshot.AtcCount);
        Assert.Equal(2, service.LastSnapshot.SupervisorCount);
        Assert.NotNull(service.LastSnapshot.Atc[0].Position);
        Assert.Equal("Ana Silva", service.LastSnapshot.Pilots[0].MemberName);

        clock.Advance(TimeSpan.FromSeconds(30));
        await service.RefreshAsync();
        Assert.Equal(2, handler.Requests.Count(request => request.Path == failedPath));
        Assert.Contains(service.LastSnapshot!.Warnings, warning => warning.StartsWith(warningPrefix, StringComparison.Ordinal));

        clock.Advance(TimeSpan.FromSeconds(30));
        await service.RefreshAsync();
        Assert.Equal(3, handler.Requests.Count(request => request.Path == failedPath));

        clock.Advance(TimeSpan.FromMinutes(1));
        await service.RefreshAsync();
        Assert.Equal(3, handler.Requests.Count(request => request.Path == failedPath));

        fail = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        await service.RefreshAsync();
        Assert.Equal(4, handler.Requests.Count(request => request.Path == failedPath));
        Assert.DoesNotContain(service.LastSnapshot!.Warnings, warning => warning.StartsWith(warningPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_DoesNotSendAnApiKeyHeader()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Fixture("pilots-summary.json")));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api);

        await service.RefreshAsync();

        Assert.All(handler.Requests, request => Assert.Null(request.ApiKey));
    }

    [Fact]
    public async Task RefreshAsync_PublishesPartialSnapshotWithFallbackStats()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            PilotsPath => Json(Fixture("pilots-summary.json")),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream failure")
            }
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api);
        TrackerSnapshot? published = null;
        service.SnapshotUpdated += (_, snapshot) => published = snapshot;

        await service.RefreshAsync();

        Assert.NotNull(published);
        Assert.Equal(2, published.PilotCount);
        Assert.Equal(0, published.AtcCount);
        Assert.Equal(2, published.Warnings.Count);
        Assert.Contains(published.Warnings, warning => warning.StartsWith("ATC:", StringComparison.Ordinal));
        Assert.Contains(published.Warnings, warning => warning.StartsWith("Statistics:", StringComparison.Ordinal));
        Assert.True(published.IsPartial);
        Assert.Equal(2, published.Stats.Now.Pilots);
        Assert.Equal(0, published.Stats.Now.Atc);
        Assert.Equal(2, published.Stats.Now.Total);
        Assert.Equal(2, published.Stats.Max.Total);
    }

    [Fact]
    public async Task RefreshAsync_ThrowsWhenEveryEndpointFails()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api);

        var exception = await Assert.ThrowsAsync<TrackerRefreshException>(() => service.RefreshAsync());

        Assert.Contains("did not return any traffic data", exception.Message);
        Assert.Equal(3, exception.Warnings.Count);
        Assert.Null(service.LastSnapshot);
    }

    [Fact]
    public async Task RefreshAsync_ThrowsWhenEveryEndpointReturnsNoTraffic()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            PilotsPath => Json("[]"),
            AtcPath => Json("[]"),
            StatsPath => Json("null"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api);

        var exception = await Assert.ThrowsAsync<TrackerRefreshException>(() => service.RefreshAsync());

        Assert.Contains("did not return any traffic data", exception.Message);
        Assert.Empty(exception.Warnings);
    }

    [Fact]
    public async Task Start_FetchesImmediatelyAndIsIdempotent()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            PilotsPath => Json(Fixture("pilots-summary.json")),
            AtcPath => Json(Fixture("atc-summary.json")),
            StatsPath => Json(Fixture("stats.json")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api);
        var published = new TaskCompletionSource<TrackerSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.SnapshotUpdated += (_, snapshot) => published.TrySetResult(snapshot);

        service.Start();
        service.Start();
        var snapshot = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, snapshot.PilotCount);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Start_WithoutImmediateRefresh_DoesNotDuplicateExplicitRefresh()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath switch
        {
            PilotsPath => Json(Fixture("pilots-summary.json")),
            AtcPath => Json(Fixture("atc-summary.json")),
            StatsPath => Json(Fixture("stats.json")),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        using var service = new TrackerService(api);

        service.Start(refreshImmediately: false);
        Assert.Empty(handler.Requests);
        await service.RefreshAsync();

        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public void Constructor_ClampsRefreshIntervalToSafeRange()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => Json("[]")));
        using var api = CreateApi(httpClient);

        using var tooFast = new TrackerService(api, TimeSpan.FromSeconds(1));
        using var accepted = new TrackerService(api, TimeSpan.FromSeconds(90));

        Assert.Equal(TimeSpan.FromSeconds(30), tooFast.RefreshInterval);
        Assert.Equal(TimeSpan.FromSeconds(90), accepted.RefreshInterval);
    }

    private static IvaoApiClient CreateApi(HttpClient httpClient) =>
        new(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin"
            },
            httpClient);

    private static HttpResponseMessage TrackerResponse(string path) => path switch
    {
        PilotsPath => Json(Fixture("pilots-summary.json")),
        AtcPath => Json(Fixture("atc-summary.json")),
        StatsPath => Json(Fixture("stats.json")),
        PilotIdentityPath => Json(Fixture("pilots-identity.json")),
        AtcIdentityPath => Json(Fixture("atc-identity.json")),
        SupervisorsPath => Json(Fixture("supervisors.json")),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    };

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed record CapturedRequest(string Path, string? Origin, string? ApiKey);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public ConcurrentQueue<CapturedRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(new CapturedRequest(
                request.RequestUri!.AbsolutePath,
                ReadHeader(request, "Origin"),
                ReadHeader(request, "apiKey")));
            return Task.FromResult(_responseFactory(request));
        }

        private static string? ReadHeader(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}

