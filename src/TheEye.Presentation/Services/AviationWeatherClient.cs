using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TheEye.Presentation.Services;

public sealed class AviationWeatherClient : IDisposable
{
    private static readonly Uri BaseAddress = new("https://aviationweather.gov/");
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, string Report)> _cache = new(
        StringComparer.OrdinalIgnoreCase);

    public AviationWeatherClient(HttpClient? httpClient = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public async Task<string?> GetMetarAsync(
        string? icao,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(icao))
        {
            return null;
        }

        var airport = icao.Trim().ToUpperInvariant();
        if (_cache.TryGetValue(airport, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Report;
        }

        var requestUri = new Uri(BaseAddress,
            $"api/data/metar?ids={Uri.EscapeDataString(airport)}&format=json");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TheEyeOfIVAO", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var observation in document.RootElement.EnumerateArray())
        {
            if (observation.TryGetProperty("icaoId", out var airportId) &&
                string.Equals(airportId.GetString(), airport, StringComparison.OrdinalIgnoreCase) &&
                observation.TryGetProperty("rawOb", out var rawReport) &&
                !string.IsNullOrWhiteSpace(rawReport.GetString()))
            {
                var report = rawReport.GetString()!;
                _cache[airport] = (DateTimeOffset.UtcNow.Add(CacheDuration), report);
                return report;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}