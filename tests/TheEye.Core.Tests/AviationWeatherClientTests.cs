using System.Net;
using TheEye.Presentation.Services;

namespace TheEye.Core.Tests;

public sealed class AviationWeatherClientTests
{
    [Fact]
    public async Task GetMetar_RequestsThePublicServiceAndCachesTheReport()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """[{"icaoId":"EGLL","rawOb":"METAR EGLL 031350Z 24010KT 9999 SCT030 12/08 Q1013"}]"""));
        using var httpClient = new HttpClient(handler);
        using var client = new AviationWeatherClient(httpClient);

        var report = await client.GetMetarAsync(" egll ");
        var cachedReport = await client.GetMetarAsync("EGLL");

        Assert.Equal("METAR EGLL 031350Z 24010KT 9999 SCT030 12/08 Q1013", report);
        Assert.Equal(report, cachedReport);
        Assert.Single(handler.Requests);
        Assert.Equal("GET", Assert.Single(handler.Methods));
    }

    [Fact]
    public async Task GetMetar_ReturnsNullWhenTheServiceHasNoMatchingObservation()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("[]"));
        using var httpClient = new HttpClient(handler);
        using var client = new AviationWeatherClient(httpClient);

        Assert.Null(await client.GetMetarAsync("ZZZZ"));
        Assert.Null(await client.GetMetarAsync(" "));
        Assert.Single(handler.Requests);
    }
}