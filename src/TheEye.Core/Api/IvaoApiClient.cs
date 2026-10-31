using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheEye.Core.Models;

namespace TheEye.Core.Api;

/// <summary>
/// Thin typed client for the IVAO tracker API. The public summary endpoints are
/// queried with an Origin header (the same approach as IVAO WebEye); a consumer
/// key is added only when the user has configured one.
/// </summary>
public sealed class IvaoApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly IvaoApiOptions _options;

    public IvaoApiClient(IvaoApiOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new IvaoApiOptions();
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = _options.Timeout;
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <summary>
    /// True when the client carries a credential the identity feeds accept: a
    /// consumer key, or a token source that may hand out a signed-in member's
    /// token. Without one those feeds answer 401, so a caller that only needs
    /// the public summaries can skip them entirely.
    /// </summary>
    public bool CanAuthenticate => HasApiKey || _options.AccessTokenSource is not null;

    public string BaseAddress => _options.BaseAddress;

    public Task<IReadOnlyList<IvaoPilotSummary>> GetPilotSummariesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoPilotSummary>("v2/tracker/now/pilots/summary", cancellationToken);

    public Task<IReadOnlyList<IvaoAtcSummary>> GetAtcSummariesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoAtcSummary>("v2/tracker/now/atc/summary", cancellationToken);

    /// <summary>
    /// The full pilot list (<c>/v2/tracker/now/pilots</c>): the same sessions as
    /// the summary, with the member behind every callsign — name, division,
    /// ratings — plus the software and simulator the pilot is connected with.
    /// Authenticated, so a caller without a credential gets a 401.
    /// </summary>
    public Task<IReadOnlyList<IvaoPilot>> GetPilotsAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoPilot>("v2/tracker/now/pilots", cancellationToken);

    /// <summary>
    /// The full ATC list (<c>/v2/tracker/now/atc</c>): the summary sessions plus
    /// the member behind every station. Authenticated, like the pilot feed.
    /// </summary>
    public Task<IReadOnlyList<IvaoAtc>> GetAtcsAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoAtc>("v2/tracker/now/atc", cancellationToken);

    /// <summary>
    /// The supervisors connected to the network
    /// (<c>/v2/tracker/now/supervisors</c>): who is watching, on which callsign,
    /// with the staff positions they hold. This feed is the only place a
    /// supervisor appears, since they are neither flying nor controlling.
    /// Authenticated, like the other identity feeds.
    /// </summary>
    public Task<IReadOnlyList<IvaoSupervisor>> GetSupervisorsAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoSupervisor>("v2/tracker/now/supervisors", cancellationToken);

    public Task<IvaoConnectionStats?> GetConnectionStatsAsync(CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoConnectionStats>("v2/tracker/now/connections/stats", null, cancellationToken);

    public Task<IvaoPilot?> GetPilotAsync(long id, CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoPilot>($"v2/tracker/now/pilots/{id}", "pilot", cancellationToken);

    public Task<IvaoAtc?> GetAtcAsync(long id, CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoAtc>($"v2/tracker/now/atc/{id}", "atc", cancellationToken);

    public Task<IvaoFlightPlan?> GetFlightPlanAsync(long id, CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoFlightPlan>($"v2/tracker/now/flight-plans/{id}", "flightPlan", cancellationToken);

    /// <summary>
    /// The newest flight plan IVAO holds for a session
    /// (<c>/v2/tracker/sessions/{id}/flightPlans/latest</c>): what the pilot
    /// filed, including the times, the endurance, the alternate and the
    /// equipment the tracker summary leaves out. Like the other session feeds
    /// this one is authenticated, so callers must cope with a 401.
    /// </summary>
    public Task<IvaoFlightPlan?> GetSessionFlightPlanAsync(
        long sessionId,
        CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoFlightPlan>($"v2/tracker/sessions/{sessionId}/flightPlans/latest", null, cancellationToken);

    /// <summary>
    /// Every recorded position of a pilot or ATC session, keyed by the session
    /// id carried by the tracker summaries (<c>id</c>, not <c>userId</c>). The
    /// feed is authenticated: IVAO answers 401 <c>not_authenticated</c> unless
    /// the request carries an IVAO login token, which is what WebEye sends after
    /// its OAuth sign-in, or a personal consumer key. Callers must cope with an
    /// unauthorised response.
    /// </summary>
    public Task<IReadOnlyList<IvaoPilotTrack>> GetSessionTracksAsync(
        long sessionId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoPilotTrack>($"v2/tracker/sessions/{sessionId}/tracks", cancellationToken);

    /// <summary>
    /// The record of a single session: the member behind the callsign, the
    /// division they fly for, their ratings and the software they are connected
    /// with. Like the tracks feed this endpoint is authenticated, so a caller
    /// without an IVAO sign-in or a consumer key gets a 401 and the card says so
    /// rather than showing half an identity.
    /// </summary>
    public Task<IvaoSessionDetail?> GetSessionAsync(
        long sessionId,
        CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoSessionDetail>($"v2/tracker/sessions/{sessionId}", "session", cancellationToken);

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var document = await GetDocumentAsync(path, cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.Deserialize<List<T>>(IvaoJson.Options) ?? [];
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Array)
        {
            return data.Deserialize<List<T>>(IvaoJson.Options) ?? [];
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            // Some endpoints wrap their list in a named property, e.g.
            // "tracks" for the recorded positions of a session. Any single
            // array is a payload, so the client does not have to know the name.
            foreach (var property in root.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.Deserialize<List<T>>(IvaoJson.Options) ?? [];
                }
            }
        }

        throw new IvaoApiException(200, root.GetRawText(), $"Unexpected response shape for {path}.");
    }

    private async Task<T?> GetObjectAsync<T>(string path, string? wrapperProperty, CancellationToken cancellationToken)
    {
        using var document = await GetDocumentAsync(path, cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        if (root.ValueKind == JsonValueKind.Object &&
            wrapperProperty is not null &&
            root.TryGetProperty(wrapperProperty, out var wrapped))
        {
            return wrapped.Deserialize<T>(IvaoJson.Options);
        }

        if (root.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return root.Deserialize<T>(IvaoJson.Options);
        }

        throw new IvaoApiException(200, root.GetRawText(), $"Unexpected response shape for {path}.");
    }

    private async Task<JsonDocument> GetDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var token = await ResolveAccessTokenAsync(false, cancellationToken);
        var response = await SendAsync(path, token, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized &&
            token is not null &&
            _options.AccessTokenSource is not null)
        {
            // A token IVAO no longer accepts (revoked, or expired early) is
            // renewed once before the refusal is reported.
            response.Dispose();
            token = await ResolveAccessTokenAsync(true, cancellationToken);
            response = await SendAsync(path, token, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && HasApiKey)
            {
                // The sign-in may simply not carry the scope this feed needs,
                // while the consumer key does: try the other credential once.
                response.Dispose();
                token = null;
                response = await SendAsync(path, null, cancellationToken);
            }
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new IvaoApiException(
                    (int)response.StatusCode,
                    Truncate(body, 2000),
                    $"IVAO API returned {(int)response.StatusCode} {response.ReasonPhrase} for {path}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
    }

    private async Task<string?> ResolveAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
        _options.AccessTokenSource is { } source
            ? await source.GetAccessTokenAsync(forceRefresh, cancellationToken)
            : null;

    private async Task<HttpResponseMessage> SendAsync(
        string path,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path));
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Origin", _options.Origin);

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            // The signed-in member's token is what WebEye sends, and the only
            // credential the recorded-tracks feed accepts besides a consumer key.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
        else if (HasApiKey)
        {
            // The IVAO API expects the consumer key in the lowercase apiKey header.
            request.Headers.TryAddWithoutValidation("apiKey", _options.ApiKey);
        }

        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private Uri BuildUri(string path)
    {
        var trimmedBase = _options.BaseAddress.TrimEnd('/');
        return new Uri($"{trimmedBase}/{path.TrimStart('/')}");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
