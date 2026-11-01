using System.Net;
using TheEye.Core.Api;

namespace TheEye.Core.Tests;

public sealed class IvaoApiClientTests
{
    private const string TracksPath = "/v2/tracker/sessions/4242/tracks";

    private const string SessionPath = "/v2/tracker/sessions/4242";

    // The session record is a single object, either bare or wrapped in "session";
    // its members are what the pilot card shows next to the tracker summary.
    private const string SessionObject = """
        {
          "id": 4242,
          "callsign": "TAP123",
          "userId": 123456,
          "connectionType": "PILOT",
          "time": 3720,
          "softwareTypeId": "ALTITUDE",
          "softwareVersion": "0.4.1",
          "createdAt": "2026-10-31T10:00:00Z",
          "user": {
            "id": 123456,
            "firstName": "Ana",
            "lastName": "Silva",
            "divisionId": "PT",
            "rating": { "pilotRating": { "id": 3, "name": "Senior Flight Student", "shortName": "FS3" } }
          }
        }
        """;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RateLimitedResponse_PausesOtherEndpointsWithoutSendingRequests(bool useDate)
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = StubHttpMessageHandler.Json(HttpStatusCode.TooManyRequests, "{}");
            response.Headers.RetryAfter = useDate
                ? new System.Net.Http.Headers.RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(10))
                : new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);

        var first = await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSupervisorsAsync());
        var pilots = await Assert.ThrowsAsync<IvaoApiException>(() => api.GetPilotSummariesAsync());
        var atc = await Assert.ThrowsAsync<IvaoApiException>(() => api.GetAtcSummariesAsync());
        var tracks = await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSessionTracksAsync(4242));

        Assert.Equal(429, first.StatusCode);
        Assert.Equal(429, pilots.StatusCode);
        Assert.Equal(429, atc.StatusCode);
        Assert.Equal(429, tracks.StatusCode);
        Assert.Single(handler.Requests);
        Assert.Contains("paused", pilots.Message);
    }

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
            StubHttpMessageHandler.Json("""{"message":"authentication required"}""")));
        using var api = CreateApi(httpClient);

        await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSessionTracksAsync(4242));
    }

    [Fact]
    public async Task GetSessionTracksAsync_AsksTheSessionEndpointWithoutAnApiKey()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TrackArray));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);

        await api.GetSessionTracksAsync(4242);

        Assert.Equal(TracksPath, Assert.Single(handler.Requests));
        Assert.Equal(string.Empty, Assert.Single(handler.ApiKeys));
    }

    [Fact]
    public async Task GetSessionTracksAsync_OmitsTheApiKeyHeader()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var httpClient = new HttpClient(handler);
        using var api = new IvaoApiClient(
            new IvaoApiOptions { BaseAddress = "https://test.local", Origin = "https://test.origin" },
            httpClient);

        await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSessionTracksAsync(4242));

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
                AccessTokenSource = new StubTokenSource("access-1")
            },
            httpClient);

        await api.GetSessionTracksAsync(4242);

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
    public async Task GetSessionAsync_ReadsABareSessionObject()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(SessionObject)));
        using var api = CreateApi(httpClient);

        var session = await api.GetSessionAsync(4242);

        Assert.NotNull(session);
        Assert.Equal("TAP123", session.Callsign);
        Assert.Equal(123456, session.UserId);
        Assert.Equal("Ana Silva", session.User?.FullName);
        Assert.Equal("Portugal", session.User?.DivisionName);
        Assert.Equal("FS3", session.User?.PilotRatingText);
        Assert.Equal("ALTITUDE 0.4.1", session.SoftwareText);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero), session.CreatedAt);
    }

    [Fact]
    public async Task GetSessionAsync_ReadsASessionWrappedInANamedProperty()
    {
        var wrapped = $$"""{"session":{{SessionObject}}}""";
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(wrapped)));
        using var api = CreateApi(httpClient);

        var session = await api.GetSessionAsync(4242);

        Assert.Equal(4242, session?.Id);
        Assert.Equal(3720, session?.Time);
    }

    [Fact]
    public async Task GetSessionAsync_AsksTheSessionEndpointWithoutAnApiKey()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(SessionObject));
        using var httpClient = new HttpClient(handler);
        using var api = CreateApi(httpClient);

        await api.GetSessionAsync(4242);

        Assert.Equal(SessionPath, Assert.Single(handler.Requests));
        Assert.Equal(string.Empty, Assert.Single(handler.ApiKeys));
    }

    [Fact]
    public async Task GetSessionAsync_ReportsTheRefusalWhenTheFeedIsNotAuthenticated()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json(
                HttpStatusCode.Unauthorized,
                """{"message":"not_authenticated"}""")));
        using var api = new IvaoApiClient(
            new IvaoApiOptions { BaseAddress = "https://test.local", Origin = "https://test.origin" },
            httpClient);

        var exception = await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSessionAsync(4242));

        Assert.Equal(401, exception.StatusCode);
        Assert.True(exception.IsUnauthorized);
    }

    [Fact]
    public async Task GetSessionAsync_ThrowsWhenTheResponseCarriesNoObject()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("\"not a session\"")));
        using var api = CreateApi(httpClient);

        await Assert.ThrowsAsync<IvaoApiException>(() => api.GetSessionAsync(4242));
    }

    private static IvaoApiClient CreateApi(HttpClient httpClient) =>
        new(
            new IvaoApiOptions
            {
                BaseAddress = "https://test.local",
                Origin = "https://test.origin",
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
