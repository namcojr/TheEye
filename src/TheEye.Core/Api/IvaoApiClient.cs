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

    public string BaseAddress => _options.BaseAddress;

    public Task<IReadOnlyList<IvaoPilotSummary>> GetPilotSummariesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoPilotSummary>("v2/tracker/now/pilots/summary", cancellationToken);

    public Task<IReadOnlyList<IvaoAtcSummary>> GetAtcSummariesAsync(CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoAtcSummary>("v2/tracker/now/atc/summary", cancellationToken);

    public Task<IvaoConnectionStats?> GetConnectionStatsAsync(CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoConnectionStats>("v2/tracker/now/connections/stats", null, cancellationToken);

    public Task<IvaoPilot?> GetPilotAsync(long id, CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoPilot>($"v2/tracker/now/pilots/{id}", "pilot", cancellationToken);

    public Task<IvaoAtc?> GetAtcAsync(long id, CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoAtc>($"v2/tracker/now/atc/{id}", "atc", cancellationToken);

    public Task<IvaoFlightPlan?> GetFlightPlanAsync(long id, CancellationToken cancellationToken = default) =>
        GetObjectAsync<IvaoFlightPlan>($"v2/tracker/now/flight-plans/{id}", "flightPlan", cancellationToken);

    /// <summary>
    /// Every recorded position of a pilot or ATC session. This is the feed the
    /// IVAO web map uses to trace the path an aircraft has flown; it requires a
    /// consumer key, so callers must cope with an unauthorised response.
    /// </summary>
    public Task<IReadOnlyList<IvaoPilotTrack>> GetSessionTracksAsync(
        long sessionId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<IvaoPilotTrack>($"v2/tracker/sessions/{sessionId}/tracks", cancellationToken);

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
        var uri = BuildUri(path);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Origin", _options.Origin);

        if (HasApiKey)
        {
            // The IVAO API expects the consumer key in the lowercase apiKey header.
            request.Headers.TryAddWithoutValidation("apiKey", _options.ApiKey);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

    private Uri BuildUri(string path)
    {
        var trimmedBase = _options.BaseAddress.TrimEnd('/');
        return new Uri($"{trimmedBase}/{path.TrimStart('/')}");
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
