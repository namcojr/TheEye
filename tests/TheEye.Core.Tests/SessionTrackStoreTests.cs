using System.Collections.Concurrent;
using System.Net;
using System.Text;
using TheEye.Core.Api;
using TheEye.Core.Services;

namespace TheEye.Core.Tests;

public sealed class SessionTrackStoreTests
{
    private const string TracksPath = "/v2/tracker/sessions/4242/tracks";

    private const string Payload = """
        [
          {
            "time": 0,
            "timestamp": "2026-10-31T10:00:00Z",
            "latitude": 52.3105,
            "longitude": 4.7683,
            "altitude": 12.5,
            "onGround": true
          },
          {
            "time": 30,
            "timestamp": "2026-10-31T10:00:30Z",
            "latitude": 52.4,
            "longitude": 4.9,
            "altitude": 10450.75,
            "onGround": false
          }
        ]
        """;

    [Fact]
    public async Task GetTracksAsync_ReturnsTheRecordedPositions()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == TracksPath
                ? Json(Payload)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api);

        var tracks = await store.GetTracksAsync(4242);

        Assert.NotNull(tracks);
        Assert.Equal(2, tracks.Count);
        Assert.Equal(12.5, tracks[0].Altitude);
        Assert.True(tracks[0].OnGround);
        Assert.Equal(52.4, tracks[1].Latitude);
        Assert.Equal(10450.75, tracks[1].Altitude);
        Assert.False(tracks[1].OnGround);
        Assert.Equal(TracksPath, Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task GetTracksAsync_ReusesTheTrackWithinTheCacheWindow()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Payload));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api);

        var first = await store.GetTracksAsync(4242);
        var second = await store.GetTracksAsync(4242);

        Assert.Same(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetTracksAsync_FetchesAgainAfterTheCacheWindow()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Payload));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api, TimeSpan.FromMilliseconds(50));

        await store.GetTracksAsync(4242);
        await Task.Delay(200);
        await store.GetTracksAsync(4242);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetTracksAsync_ReturnsNullWhenTheApiRejectsTheRequest()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("consumer key required")
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api);

        Assert.Null(await store.GetTracksAsync(4242));
    }

    [Fact]
    public async Task Clear_ForgetsEveryCachedTrack()
    {
        var handler = new StubHttpMessageHandler(_ => Json(Payload));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api);

        await store.GetTracksAsync(4242);
        store.Clear();
        await store.GetTracksAsync(4242);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Constructor_ClampsTheCacheDurationToTheDefault()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => Json("[]")));
        using var api = CreateApi(httpClient);

        Assert.Equal(TimeSpan.FromSeconds(20), new SessionTrackStore(api).CacheDuration);
        Assert.Equal(TimeSpan.FromSeconds(20), new SessionTrackStore(api, TimeSpan.Zero).CacheDuration);
        Assert.Equal(TimeSpan.FromSeconds(45), new SessionTrackStore(api, TimeSpan.FromSeconds(45)).CacheDuration);
    }

    private static IvaoApiClient CreateApi(HttpClient httpClient) =>
        new(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin",
                ApiKey = "secret"
            },
            httpClient);

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(request.RequestUri!.AbsolutePath);
            return Task.FromResult(_responseFactory(request));
        }
    }
}
