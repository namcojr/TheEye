using TheEye.Core.Reference;

namespace TheEye.Core.Tests;

public sealed class RatingCatalogTests
{
    [Fact]
    public void PilotBadgeUrl_PointsAtTheBadgeIvaoPublishes()
    {
        Assert.Equal(
            "https://www.ivao.aero/data/images/ratings/pilot/3.gif",
            RatingCatalog.PilotBadgeUrl(3));
    }

    [Fact]
    public void AtcBadgeUrl_PointsAtTheBadgeIvaoPublishes()
    {
        Assert.Equal(
            "https://www.ivao.aero/data/images/ratings/atc/5.gif",
            RatingCatalog.AtcBadgeUrl(5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void PilotBadgeUrl_IsNullWithoutARating(int? ratingId) =>
        Assert.Null(RatingCatalog.PilotBadgeUrl(ratingId));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void AtcBadgeUrl_IsNullWithoutARating(int? ratingId) =>
        Assert.Null(RatingCatalog.AtcBadgeUrl(ratingId));
}
