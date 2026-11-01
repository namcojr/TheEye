using System.Net;
using TheEye.Core.Api;
using TheEye.Core.Services;

namespace TheEye.Core.Tests;

public sealed class SessionDetailStoreTests
{
    private const string SessionPath = "/v2/tracker/sessions/4242";

    // The record carries who is flying, since when and with which client, which
    // is everything the pilot card shows on top of the tracker summary.
    private const string Payload = """
        {
          "id": 4242,
          "callsign": "TAP123",
          "userId": 123456,
          "connectionType": "PILOT",
          "serverId": "EU-1",
          "time": 3720,
          "softwareTypeId": "ALTITUDE",
          "softwareVersion": "0.4.1",
          "softwareType": { "id": 4, "name": "Altitude" },
          "createdAt": "2026-10-31T10:00:00Z",
          "pilotSession": { "simulatorId": "MSFS", "textureId": 7 },
          "user": {
            "id": 123456,
            "firstName": "Ana",
            "lastName": "Silva",
            "divisionId": "PT",
            "rating": {
              "isAtc": true,
              "isPilot": true,
              "pilotRating": { "id": 3, "name": "Senior Flight Student", "shortName": "FS3" },
              "atcRating": { "id": 2, "name": "Approach Controller", "shortName": "APC" }
            }
          }
        }
        """;

    [Fact]
    public async Task GetSessionAsync_ReturnsTheMemberBehindTheSession()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath == SessionPath
                ? StubHttpMessageHandler.Json(Payload)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        var result = await store.GetSessionAsync(4242);
        var session = result.Session;

        Assert.NotNull(session);
        Assert.Equal("TAP123", session.Callsign);
        Assert.Equal(123456, session.UserId);
        Assert.True(session.IsPilot);
        Assert.Equal("Ana Silva", session.User?.FullName);
        Assert.Equal("Portugal", session.User?.DivisionName);
        Assert.Equal("FS3", session.User?.PilotRatingText);
        Assert.Equal("Altitude 0.4.1", session.SoftwareText);
        Assert.Equal("MSFS", session.SimulatorText);
        Assert.Equal("EU-1", session.ServerText);
        Assert.Null(result.StatusCode);
        Assert.Equal(SessionPath, Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task GetSessionAsync_ReusesTheRecordWithinTheCacheWindow()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Payload));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        var first = await store.GetSessionAsync(4242);
        var second = await store.GetSessionAsync(4242);

        Assert.Same(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSessionAsync_FetchesAgainAfterTheCacheWindow()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Payload));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api, TimeSpan.FromMilliseconds(50));

        await store.GetSessionAsync(4242);
        await Task.Delay(200);
        await store.GetSessionAsync(4242);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetSessionAsync_ReportsTheStatusWhenIvaoRefusesTheRequest()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"message":"not_authenticated"}""")
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        var result = await store.GetSessionAsync(4242);

        Assert.Null(result.Session);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task GetSessionAsync_KeepsTheRefusalForTheCacheWindow()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        var first = await store.GetSessionAsync(4242);
        var second = await store.GetSessionAsync(4242);

        // A session IVAO will not describe is not asked for again on every refresh.
        Assert.Equal(403, first.StatusCode);
        Assert.Equal(403, second.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetSessionAsync_ReportsNoStatusWhenTheConnectionDrops()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("network down"));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        var result = await store.GetSessionAsync(4242);

        Assert.Null(result.Session);
        Assert.Null(result.StatusCode);
        Assert.Equal("network down", result.Error);
    }

    [Fact]
    public async Task GetSessionAsync_NamesTheMemberThatCouldNotBeRead()
    {
        // IVAO answered, but with a shape the model does not accept: the card has
        // to say which member broke instead of only that nothing could be loaded.
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("""{ "id": 4242, "time": "not-a-number" }"""));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        var result = await store.GetSessionAsync(4242);

        Assert.Null(result.Session);
        Assert.Null(result.StatusCode);
        Assert.NotNull(result.Error);
        Assert.Contains("$.time", result.Error);
    }

    [Fact]
    public async Task GetSessionAsync_ReadsTheSoftwareSlugIvaoSends()
    {
        // The live payload identifies the client as "altitude/win"; a number-only
        // model used to lose every session record over it.
        var handler = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json(Payload.Replace("\"id\": 4, \"name\": \"Altitude\"", "\"id\": \"altitude/win\", \"name\": \"Altitude/win\"")));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        var result = await store.GetSessionAsync(4242);

        Assert.NotNull(result.Session);
        Assert.Equal("altitude/win", result.Session.SoftwareType?.Id);
        Assert.Equal("Altitude/win 0.4.1", result.Session.SoftwareText);
    }

    [Fact]
    public async Task GetSessionAsync_ReadsEachSessionSeparately()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Payload));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        await store.GetSessionAsync(4242);
        await store.GetSessionAsync(4243);

        Assert.Equal(
            ["/v2/tracker/sessions/4242", "/v2/tracker/sessions/4243"],
            handler.Requests.ToArray());
    }

    [Fact]
    public async Task Clear_ForgetsEveryCachedRecord()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Payload));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);
        var store = new SessionDetailStore(api);

        await store.GetSessionAsync(4242);
        store.Clear();
        await store.GetSessionAsync(4242);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void Constructor_ClampsTheCacheDurationToTheDefault()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}")));
        using var api = CreateApi(httpClient);

        Assert.Equal(TimeSpan.FromMinutes(1), new SessionDetailStore(api).CacheDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), new SessionDetailStore(api, TimeSpan.Zero).CacheDuration);
        Assert.Equal(TimeSpan.FromSeconds(45), new SessionDetailStore(api, TimeSpan.FromSeconds(45)).CacheDuration);
    }

    private static IvaoApiClient CreateApi(HttpClient httpClient) =>
        new(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin"
            },
            httpClient);
}
