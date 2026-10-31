using TheEye.Core.Reference;

namespace TheEye.Core.Tests;

public sealed class DurationTextTests
{
    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(-30, "0 min")]
    [InlineData(1, "1 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min")]
    [InlineData(3540, "59 min")]
    [InlineData(3600, "1 h")]
    [InlineData(3720, "1 h 2 min")]
    [InlineData(59940, "16 h 39 min")]
    [InlineData(90000, "25 h")]
    public void Format_KeepsALongCountShort(int seconds, string expected) =>
        Assert.Equal(expected, DurationText.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Format_DropsTheMinutesWhenTheyAreZero() =>
        Assert.Equal("2 h", DurationText.Format(TimeSpan.FromHours(2)));

    [Fact]
    public void FromSeconds_FormatsATrackerCount() =>
        Assert.Equal("1 h 2 min", DurationText.FromSeconds(3720));

    [Fact]
    public void FromSeconds_IsADashWhenTheCountIsUnknown() =>
        Assert.Equal("—", DurationText.FromSeconds(null));

    [Fact]
    public void FromSeconds_TreatsANegativeCountAsZero() =>
        Assert.Equal("0 min", DurationText.FromSeconds(-5));
}
