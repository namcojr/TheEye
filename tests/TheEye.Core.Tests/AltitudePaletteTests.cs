using TheEye.Core.Maps;

namespace TheEye.Core.Tests;

public sealed class AltitudePaletteTests
{
    [Theory]
    [InlineData(0, AltitudeBand.Ground)]
    [InlineData(-1200, AltitudeBand.Ground)]
    [InlineData(1, AltitudeBand.Below3000)]
    [InlineData(2999, AltitudeBand.Below3000)]
    [InlineData(3000, AltitudeBand.Below5000)]
    [InlineData(4999, AltitudeBand.Below5000)]
    [InlineData(5000, AltitudeBand.Below7000)]
    [InlineData(6999, AltitudeBand.Below7000)]
    [InlineData(7000, AltitudeBand.Below15000)]
    [InlineData(14999, AltitudeBand.Below15000)]
    [InlineData(15000, AltitudeBand.Below30000)]
    [InlineData(29999, AltitudeBand.Below30000)]
    [InlineData(30000, AltitudeBand.Below40000)]
    [InlineData(39999, AltitudeBand.Below40000)]
    [InlineData(40000, AltitudeBand.Above40000)]
    [InlineData(51000, AltitudeBand.Above40000)]
    public void Band_UsesTheAltitudeLayersOfTheWebMap(double altitude, AltitudeBand expected) =>
        Assert.Equal(expected, AltitudePalette.Band(altitude));

    [Fact]
    public void Band_PutsGroundAndUnknownAltitudesInTheGroundBand()
    {
        Assert.Equal(AltitudeBand.Ground, AltitudePalette.Band(35000, onGround: true));
        Assert.Equal(AltitudeBand.Ground, AltitudePalette.Band(null));
    }

    [Fact]
    public void For_ReturnsADistinctColourPerBand()
    {
        var bands = Enum.GetValues<AltitudeBand>();
        var colors = bands.Select(AltitudePalette.For).ToList();

        Assert.Equal(bands.Length, colors.Distinct().Count());
        Assert.All(colors, color => Assert.Matches("^#[0-9A-F]{6}$", color));
    }

    [Fact]
    public void For_MatchesTheLegendOfTheWebMap()
    {
        Assert.Equal("#E74C3C", AltitudePalette.For(AltitudeBand.Ground));
        Assert.Equal("#E67E22", AltitudePalette.ForAltitude(2500));
        Assert.Equal("#F1C40F", AltitudePalette.ForAltitude(4500));
        Assert.Equal("#2ECC71", AltitudePalette.ForAltitude(6000));
        Assert.Equal("#22D3EE", AltitudePalette.ForAltitude(12000));
        Assert.Equal("#3498DB", AltitudePalette.ForAltitude(25000));
        Assert.Equal("#1D4ED8", AltitudePalette.ForAltitude(38000));
        Assert.Equal("#8E44AD", AltitudePalette.ForAltitude(41000));
    }
}
