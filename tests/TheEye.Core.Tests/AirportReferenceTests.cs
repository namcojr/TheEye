using TheEye.Core.Models;
using TheEye.Core.Reference;

namespace TheEye.Core.Tests;

public sealed class AirportReferenceTests
{
    [Fact]
    public void Parse_ReadsTheAirportAndEveryRunwayOfTheLine()
    {
        var airport = AirportReference.Parse(
            "EGLL|51.4706|-0.4619|London Heathrow Airport|" +
            "51.4775,-0.4614,51.4646,-0.4337,45;51.4775,-0.4864,51.4646,-0.4586,60");

        Assert.NotNull(airport);
        Assert.Equal("EGLL", airport.Icao);
        Assert.Equal(51.4706, airport.Position.Latitude, 4);
        Assert.Equal(-0.4619, airport.Position.Longitude, 4);
        Assert.Equal("London Heathrow Airport", airport.Name);
        Assert.True(airport.HasRunways);
        Assert.Equal(2, airport.Runways.Count);

        var runway = airport.Runways[0];
        Assert.Equal(51.4775, runway.Threshold.Latitude, 4);
        Assert.Equal(-0.4614, runway.Threshold.Longitude, 4);
        Assert.Equal(51.4646, runway.End.Latitude, 4);
        Assert.Equal(-0.4337, runway.End.Longitude, 4);
        Assert.Equal(45, runway.WidthMetres);
        Assert.Equal(60, airport.Runways[1].WidthMetres);
    }

    [Fact]
    public void Parse_TrimsTheIcaoAndFallsBackToItWhenTheNameIsMissing()
    {
        var airport = AirportReference.Parse(" egll |51.4706|-0.4619|  |51.4775,-0.4614,51.4646,-0.4337,45");

        Assert.NotNull(airport);
        Assert.Equal("EGLL", airport.Icao);
        Assert.Equal("EGLL", airport.Name);
        Assert.Equal("EGLL", airport.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EGLL|51.4706|-0.4619")]
    [InlineData("EGLL|north|-0.4619|Heathrow")]
    [InlineData("EGLL|51.4706|west|Heathrow")]
    [InlineData("EGLL|99.0|-0.4619|Nowhere")]
    [InlineData("|51.4706|-0.4619|No Code")]
    public void Parse_RejectsLinesItCannotUse(string line) =>
        Assert.Null(AirportReference.Parse(line));

    [Fact]
    public void Parse_AssumesAWidthWhenTheSourceHasNone()
    {
        var airport = AirportReference.Parse("LFPG|49.0097|2.5479|Paris|49.0128,2.5350,49.0097,2.5700");

        var runway = Assert.Single(airport!.Runways);
        Assert.Equal(45, runway.WidthMetres);
    }

    [Theory]
    [InlineData("300", 120)]
    [InlineData("3", 10)]
    [InlineData("0", 45)]
    public void Parse_KeepsThePublishedWidthWithinTheDrawnRange(string width, double expected)
    {
        var airport = AirportReference.Parse("LFPG|49.0097|2.5479|Paris|49.0128,2.5350,49.0097,2.5700," + width);

        Assert.Equal(expected, Assert.Single(airport!.Runways).WidthMetres);
    }

    [Fact]
    public void Parse_DropsRunwaysWithoutTwoDistinctThresholds()
    {
        // A strip whose thresholds collapse onto one point, a truncated runway
        // without an end and a text code are all dropped; the valid one survives.
        var airport = AirportReference.Parse(
            "LFPG|49.0097|2.5479|Paris|49.0128,2.5350,49.0128,2.5350,45;49.0097,2.5700,2.5479;" +
            "north,2.5350,49.0097,2.5700,45;49.0128,2.5350,49.0097,2.5700,45");

        var runway = Assert.Single(airport!.Runways);
        Assert.Equal(49.0128, runway.Threshold.Latitude, 4);
        Assert.Equal(49.0097, runway.End.Latitude, 4);
    }

    [Fact]
    public void Parse_LeavesAnAirportWithoutPavementWhenNoRunwaySurvives()
    {
        var airport = AirportReference.Parse("LFPG|49.0097|2.5479|Paris|");

        Assert.NotNull(airport);
        Assert.False(airport.HasRunways);
        Assert.Null(airport.LongestRunway);
    }

    [Fact]
    public void Runway_MeasuresItsLengthBetweenTheThresholds()
    {
        // A degree of latitude is 111.32 km, and a degree of longitude is that
        // much shorter by the cosine of the latitude: half of it at 60° north.
        var meridian = new AirportRunway(new GeoCoordinate(51, 0), new GeoCoordinate(51.01, 0), 45);
        var parallel = new AirportRunway(new GeoCoordinate(60, 0), new GeoCoordinate(60, 1), 45);

        Assert.InRange(meridian.LengthMetres, 1110, 1117);
        Assert.InRange(parallel.LengthMetres, 55_600, 55_720);
        Assert.Equal(51.005, meridian.Midpoint.Latitude, 4);
        Assert.Equal(0.5, parallel.Midpoint.Longitude, 4);
    }

    [Fact]
    public void Airport_LongestRunway_PicksTheStripThatMatters()
    {
        var airport = new AirportRecord(
            "LFPG",
            new GeoCoordinate(49.0097, 2.5479),
            "Paris Charles de Gaulle",
            [
                new AirportRunway(new GeoCoordinate(49.0128, 2.5350), new GeoCoordinate(49.0097, 2.5700), 45),
                new AirportRunway(new GeoCoordinate(49.0030, 2.5350), new GeoCoordinate(49.0000, 2.6000), 60)
            ]);

        Assert.Equal(60, airport.LongestRunway!.WidthMetres);
        Assert.Equal("Paris Charles de Gaulle", airport.DisplayName);
    }

    [Fact]
    public void Find_IgnoresCaseAndSpacingButAnswersNullForAnUnknownCode()
    {
        var airport = AirportReference.Find("  egll ");

        Assert.NotNull(airport);
        Assert.Equal("EGLL", airport.Icao);
        Assert.Null(AirportReference.Find("ZZZZ"));
        Assert.Null(AirportReference.Find(""));
        Assert.Null(AirportReference.Find(null));
    }

    [Fact]
    public void FindInBounds_ReturnsTheAirportsInsideTheBoxInLatitudeOrder()
    {
        // A box the size of Greater London: it holds Heathrow and a handful of
        // airfields, but neither Paris nor Amsterdam.
        var bounds = new GeoBounds(51.2, -0.7, 51.8, 0.4);

        var airports = AirportReference.FindInBounds(bounds);

        Assert.NotEmpty(airports);
        Assert.Contains(airports, airport => airport.Icao == "EGLL");
        Assert.DoesNotContain(airports, airport => airport.Icao is "LFPG" or "EHAM");
        Assert.All(airports, airport =>
        {
            Assert.InRange(airport.Position.Latitude, bounds.MinLatitude, bounds.MaxLatitude);
            Assert.InRange(airport.Position.Longitude, bounds.MinLongitude, bounds.MaxLongitude);
        });
        Assert.Equal(
            airports.Select(airport => airport.Position.Latitude).Order(),
            airports.Select(airport => airport.Position.Latitude));
    }

    [Fact]
    public void FindInBounds_RunwaysOnly_SkipsTheAirportsWithoutPavement()
    {
        var bounds = new GeoBounds(45.0, -5.0, 55.0, 15.0);

        var all = AirportReference.FindInBounds(bounds);
        var withRunways = AirportReference.FindInBounds(bounds, runwaysOnly: true);

        Assert.NotEmpty(withRunways);
        Assert.True(withRunways.Count <= all.Count);
        Assert.All(withRunways, airport => Assert.True(airport.HasRunways));
        Assert.Equal(all.Where(airport => airport.HasRunways), withRunways);
    }

    [Fact]
    public void FindInBounds_AnswersNothingForAnInvalidOrEmptyBox()
    {
        Assert.Empty(AirportReference.FindInBounds(new GeoBounds(51.8, -0.7, 51.2, 0.4)));
        Assert.Empty(AirportReference.FindInBounds(new GeoBounds(double.NaN, 0, 1, 1)));
        Assert.Empty(AirportReference.FindInBounds(new GeoBounds(89.5, -180, 90, 180)));
    }

    [Fact]
    public void BundledTable_ShipsTheWorldAirportsAndTheirPavement()
    {
        Assert.True(AirportReference.Count > 10_000, $"Only {AirportReference.Count} airports were bundled.");

        var heathrow = AirportReference.Find("EGLL");

        Assert.NotNull(heathrow);
        Assert.True(heathrow.HasRunways);
        Assert.InRange(heathrow.LongestRunway!.LengthMetres, 3500, 4200);
    }
}
