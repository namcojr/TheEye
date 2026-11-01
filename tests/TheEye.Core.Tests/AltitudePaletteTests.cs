using TheEye.Core.Maps;

namespace TheEye.Core.Tests;

public sealed class AltitudePaletteTests
{
    [Theory]
    [InlineData(0, AltitudeBand.Ground)]
    [InlineData(-1200, AltitudeBand.Ground)]
    [InlineData(900, AltitudeBand.Ground)]
    [InlineData(901, AltitudeBand.Below3000)]
    [InlineData(2900, AltitudeBand.Below3000)]
    [InlineData(2901, AltitudeBand.Below5000)]
    [InlineData(4900, AltitudeBand.Below5000)]
    [InlineData(4901, AltitudeBand.Below10000)]
    [InlineData(9900, AltitudeBand.Below10000)]
    [InlineData(9901, AltitudeBand.Below15000)]
    [InlineData(14900, AltitudeBand.Below15000)]
    [InlineData(14901, AltitudeBand.Below20000)]
    [InlineData(19900, AltitudeBand.Below20000)]
    [InlineData(19901, AltitudeBand.Below25000)]
    [InlineData(24900, AltitudeBand.Below25000)]
    [InlineData(24901, AltitudeBand.Below30000)]
    [InlineData(29900, AltitudeBand.Below30000)]
    [InlineData(29901, AltitudeBand.Below42000)]
    [InlineData(42000, AltitudeBand.Below42000)]
    [InlineData(42001, AltitudeBand.Above42000)]
    [InlineData(51000, AltitudeBand.Above42000)]
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
        Assert.Equal("#FF0000", AltitudePalette.For(AltitudeBand.Ground));
        Assert.Equal("#FF7F00", AltitudePalette.ForAltitude(2500));
        Assert.Equal("#FFFF00", AltitudePalette.ForAltitude(4500));
        Assert.Equal("#7FFF00", AltitudePalette.ForAltitude(6000));
        Assert.Equal("#00FF00", AltitudePalette.ForAltitude(12000));
        Assert.Equal("#00FF7F", AltitudePalette.ForAltitude(18000));
        Assert.Equal("#00FFFF", AltitudePalette.ForAltitude(22000));
        Assert.Equal("#007FFF", AltitudePalette.ForAltitude(27000));
        Assert.Equal("#0000FF", AltitudePalette.ForAltitude(38000));
        Assert.Equal("#7F00FF", AltitudePalette.ForAltitude(45000));
    }
}
