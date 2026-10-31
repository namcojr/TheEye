using System.Net;
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
                ? StubHttpMessageHandler.Json(Payload)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api);

        var result = await store.GetTracksAsync(4242);
        var tracks = result.Tracks;

        Assert.NotNull(tracks);
        Assert.Equal(2, tracks.Count);
        Assert.Equal(12.5, tracks[0].Altitude);
        Assert.True(tracks[0].OnGround);
        Assert.Equal(52.4, tracks[1].Latitude);
        Assert.Equal(10450.75, tracks[1].Altitude);
        Assert.False(tracks[1].OnGround);
        Assert.True(result.HasTrack);
        Assert.Null(result.StatusCode);
        Assert.Equal(TracksPath, Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task GetTracksAsync_ReusesTheTrackWithinTheCacheWindow()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Payload));
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
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Payload));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api, TimeSpan.FromMilliseconds(50));

        await store.GetTracksAsync(4242);
        await Task.Delay(200);
        await store.GetTracksAsync(4242);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetTracksAsync_ReportsTheStatusWhenTheApiRejectsTheRequest()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("consumer key required")
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api);

        var result = await store.GetTracksAsync(4242);

        Assert.Null(result.Tracks);
        Assert.False(result.HasTrack);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task GetTracksAsync_TreatsASinglePositionAsNoTrack()
    {
        const string single = """
            [{ "latitude": 52.3105, "longitude": 4.7683, "altitude": 12.5, "onGround": true }]
            """;
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(single));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionTrackStore(api);

        var result = await store.GetTracksAsync(4242);

        Assert.Equal(1, result.Tracks?.Count);
        Assert.False(result.HasTrack);
        Assert.Null(result.StatusCode);
    }

    [Fact]
    public async Task Clear_ForgetsEveryCachedTrack()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Payload));
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
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("[]")));
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
}
