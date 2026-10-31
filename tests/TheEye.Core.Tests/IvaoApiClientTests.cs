using System.Net;
using TheEye.Core.Api;

namespace TheEye.Core.Tests;

public sealed class IvaoApiClientTests
{
    private const string TracksPath = "/v2/tracker/sessions/4242/tracks";

    // The tracks feed is a list of positions, newest shape shown first: the
    // reported altitude is the one a trail segment is coloured by.
    private const string TrackArray = """
        [
          {
            "altitude": 19,
            "altitudeDifference": -102,
            "bank": 0,
            "groundSpeed": 0,
            "heading": 45,
            "latitude": 34.868385,
            "longitude": 33.60998,
            "onGround": true,
            "pitch": 0.703125,
            "state": "En Route",
            "timestamp": "2026-10-31T12:47:39.000Z",
            "transponder": 1000,
            "transponderMode": "N",
            "time": 1
          },
          {
            "altitude": 12400.5,
            "groundSpeed": 310,
            "heading": 45,
            "latitude": 35.1,
            "longitude": 33.9,
            "onGround": false,
            "state": "En Route",
            "timestamp": "2026-10-31T12:57:39.000Z",
            "time": 601
          }
        ]
        """;

    [Fact]
    public async Task GetSessionTracksAsync_ReadsABareArray()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TrackArray)));
        using var api = CreateApi(httpClient);

        var tracks = await api.GetSessionTracksAsync(4242);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(19, tracks[0].Altitude);
        Assert.True(tracks[0].OnGround);
        Assert.Equal(12400.5, tracks[1].Altitude);
        Assert.Equal(601, tracks[1].Time);
    }

    [Fact]
    public async Task GetSessionTracksAsync_ReadsAListWrappedInANamedProperty()
    {
        var wrapped = $$"""{"tracks":{{TrackArray}}}""";
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(wrapped)));
        using var api = CreateApi(httpClient);

        var tracks = await api.GetSessionTracksAsync(4242);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(34.868385, tracks[0].Latitude!.Value, 6);
    }

    [Fact]
    public async Task GetSessionTracksAsync_ReadsAListWrappedInData()
    {
        var wrapped = $$"""{"data":{{TrackArray}}}""";
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(wrapped)));
        using var api = CreateApi(httpClient);

        Assert.Equal(2, (await api.GetSessionTracksAsync(4242)).Count);
    }

    [Fact]
    public async Task GetSessionTracksAsync_ThrowsWhenTheResponseCarriesNoList()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("""{"message":"consumer key required"}""")));
        using var api = CreateApi(httpClient);

        await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSessionTracksAsync(4242));
    }

    [Fact]
    public async Task GetSessionTracksAsync_AsksTheSessionEndpointAndSendsTheConsumerKey()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TrackArray));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);

        await api.GetSessionTracksAsync(4242);

        Assert.Equal(TracksPath, Assert.Single(handler.Requests));
        Assert.Equal("consumer-key", Assert.Single(handler.ApiKeys));
    }

    [Fact]
    public async Task GetSessionTracksAsync_OmitsTheKeyHeaderWhenNoneIsConfigured()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = new HttpClient(handler);
        using var api = new IvaoApiClient(
            new IvaoApiOptions { BaseAddress = "https://test.local", Origin = "https://test.origin" },
            httpClient);

        await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSessionTracksAsync(4242));

        Assert.False(api.HasApiKey);
        Assert.Equal(string.Empty, Assert.Single(handler.ApiKeys));
    }

    [Fact]
    public async Task GetSessionTracksAsync_SendsTheSignedInTokenAsBearer()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TrackArray));
        using var httpClient = new HttpClient(handler);
        using var api = new IvaoApiClient(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin",
                ApiKey = "consumer-key",
                AccessTokenSource = new StubTokenSource("access-1")
            },
            httpClient);

        await api.GetSessionTracksAsync(4242);

        // The sign-in wins over the consumer key: the token is what WebEye sends.
        Assert.Equal("Bearer access-1", Assert.Single(handler.Authorizations));
        Assert.Equal(string.Empty, Assert.Single(handler.ApiKeys));
    }

    [Fact]
    public async Task GetSessionTracksAsync_RenewsTheTokenOnceWhenIvaoRefusesIt()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var authorization = request.Headers.Authorization?.ToString();
            return authorization == "Bearer stale"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : StubHttpMessageHandler.Json(TrackArray);
        });
        var tokens = new StubTokenSource("stale", "fresh");
        using var httpClient = new HttpClient(handler);
        using var api = new IvaoApiClient(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin",
                AccessTokenSource = tokens
            },
            httpClient);

        var tracks = await api.GetSessionTracksAsync(4242);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(["Bearer stale", "Bearer fresh"], handler.Authorizations.ToArray());
        Assert.Equal([false, true], tokens.Requests.ToArray());
    }

    [Fact]
    public async Task GetSessionTracksAsync_ReportsTheRefusalWhenTheRenewedTokenIsRefusedToo()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var tokens = new StubTokenSource("stale", null);
        using var httpClient = new HttpClient(handler);
        using var api = new IvaoApiClient(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin",
                AccessTokenSource = tokens
            },
            httpClient);

        var exception = await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSessionTracksAsync(4242));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal([false, true], tokens.Requests.ToArray());
    }

    [Fact]
    public async Task GetSessionTracksAsync_FallsBackToTheConsumerKeyWhenTheTokenIsRefused()
    {
        var handler = new StubHttpMessageHandler(request =>
            string.IsNullOrEmpty(request.Headers.Authorization?.ToString())
                ? StubHttpMessageHandler.Json(TrackArray)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = new HttpClient(handler);
        using var api = new IvaoApiClient(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin",
                ApiKey = "consumer-key",
                AccessTokenSource = new StubTokenSource("access-1")
            },
            httpClient);

        var tracks = await api.GetSessionTracksAsync(4242);

        Assert.Equal(2, tracks.Count);
        Assert.Equal(["Bearer access-1", "Bearer access-1", string.Empty], handler.Authorizations.ToArray());
        Assert.Equal(["", "", "consumer-key"], handler.ApiKeys.ToArray());
    }

    private static IvaoApiClient CreateApi(HttpClient httpClient) =>
        new(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin",
                ApiKey = "consumer-key"
            },
            httpClient);

    /// <summary>Hands out the tokens in order and remembers what was asked for.</summary>
    private sealed class StubTokenSource(params string?[] tokens) : IIvaoAccessTokenSource
    {
        private int _served;

        public List<bool> Requests { get; } = [];

        public Task<string?> GetAccessTokenAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(forceRefresh);
            var token = tokens[Math.Min(_served, tokens.Length - 1)];
            _served++;
            return Task.FromResult(token);
        }
    }
}
