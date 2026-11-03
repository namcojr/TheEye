using System.Net;
using TheEye.Presentation.Services;

namespace TheEye.Core.Tests;

public sealed class AviationWeatherClientTests
{
    [Theory]
    [InlineData("METAR EGLL 031350Z 09010KT 9999 SCT030", 90, 10, 0, 0, 0)]
    [InlineData("METAR EGLL 031350Z 09010KT 9999 SCT030", 270, 0, 0, 10, 0)]
    [InlineData("METAR EGLL 031350Z 27010KT 9999 SCT030", 90, 0, 0, 10, 0)]
    [InlineData("METAR EGLL 031350Z 18010KT 9999 SCT030", 90, 0, 10, 0, 1)]
    [InlineData("METAR EGLL 031350Z 00010KT 9999 SCT030", 90, 0, 10, 0, 0)]
    public void MetarWindCalculator_CalculatesComponentsForRunwayHeading(
        string report,
        double runwayHeading,
        double headwind,
        double crosswind,
        double tailwind,
        int crosswindFromRight)
    {
        var wind = MetarWindCalculator.Parse(report);

        Assert.NotNull(wind);
        var components = MetarWindCalculator.Calculate(wind.Value, runwayHeading);

        Assert.NotNull(components);
        Assert.Equal(headwind, components.Value.HeadwindKnots, 1);
        Assert.Equal(crosswind, components.Value.CrosswindKnots, 1);
        Assert.Equal(tailwind, components.Value.TailwindKnots, 1);
        Assert.Equal(crosswindFromRight == 1, components.Value.CrosswindFromRight);
    }

    [Theory]
    [InlineData("METAR EGLL 031350Z 09010G20KT 9999 SCT030", 10.0)]
    [InlineData("METAR EGLL 031350Z 09005MPS 9999 SCT030", 9.7192)]
    [InlineData("METAR EGLL 031350Z 09018KMH 9999 SCT030", 9.7192)]
    public void MetarWindCalculator_ParsesWindDirectionAndSpeed(string report, double expectedKnots)
    {
        var wind = MetarWindCalculator.Parse(report);

        if (double.IsNaN(expectedKnots))
        {
            Assert.Null(wind);
            return;
        }

        Assert.NotNull(wind);
        Assert.Equal(90, wind.Value.DirectionDegrees);
        Assert.Equal(expectedKnots, wind.Value.SpeedKnots, 3);
    }

    [Theory]
    [InlineData("METAR EGLL 031350Z VRB4KT 9999 SCT030", 4)]
    [InlineData("METAR EGLL 031350Z VRB04KT 9999 SCT030", 4)]
    [InlineData("METAR EGLL 031350Z VRB05KT 9999 SCT030", 5)]
    public void MetarWindCalculator_PreservesVariableWindSpeedWithoutInventingComponents(
        string report,
        double expectedKnots)
    {
        var wind = MetarWindCalculator.Parse(report);

        Assert.NotNull(wind);
        Assert.True(wind.Value.HasVariableDirection);
        Assert.Equal(expectedKnots, wind.Value.SpeedKnots);
        Assert.Null(MetarWindCalculator.Calculate(wind.Value, 90));
    }

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