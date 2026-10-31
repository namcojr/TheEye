using TheEye.Core.Reference;

namespace TheEye.Core.Tests;

public sealed class DivisionCatalogTests
{
    [Theory]
    [InlineData("PT", "Portugal")]
    [InlineData("GB", "United Kingdom")]
    [InlineData("DE", "Germany")]
    [InlineData("BR", "Brazil")]
    [InlineData("FR", "France")]
    public void Name_ResolvesAKnownDivisionCode(string code, string expected) =>
        Assert.Equal(expected, DivisionCatalog.Name(code));

    [Fact]
    public void Name_IgnoresCaseAndSurroundingSpace() =>
        Assert.Equal("Portugal", DivisionCatalog.Name(" pt "));

    [Fact]
    public void Name_EchoesACodeTheCatalogueDoesNotKnow()
    {
        // A division that is not a country still has to read as something.
        Assert.Equal("XX", DivisionCatalog.Name("XX"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_IsEmptyWithoutACode(string? code) =>
        Assert.Equal(string.Empty, DivisionCatalog.Name(code));

    [Fact]
    public void FlagUrl_BuildsTheFlagOfAKnownCountry()
    {
        Assert.Equal("https://flagcdn.com/w40/pt.png", DivisionCatalog.FlagUrl("PT"));
        Assert.Equal("https://flagcdn.com/w40/pt.png", DivisionCatalog.FlagUrl("pt"));

        // France is "-99" in the map the rest of the table came from, so it is
        // the one country that used to show a bare code and no flag.
        Assert.Equal("https://flagcdn.com/w40/fr.png", DivisionCatalog.FlagUrl("FR"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XXX")]
    [InlineData("QQ")]
    public void FlagUrl_IsNullWhenTheCodeIsNotACountry(string? code) =>
        Assert.Null(DivisionCatalog.FlagUrl(code));

    [Fact]
    public void Count_CoversEveryIsoCountry()
    {
        // The catalogue is generated from the world map GeoJSON, so a truncated
        // table would show up here rather than as a nameless division on screen.
        // The map carries 235 of the inhabited countries and territories; the
        // fifteen codes it marks "-99" (France, its overseas departments, Kosovo
        // and a handful of islands) are completed by hand, which leaves the table
        // one entry short of the flag list it draws from.
        Assert.InRange(DivisionCatalog.Count, 245, 260);
    }
}
