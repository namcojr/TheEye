using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

public sealed class TrafficTableSortTests
{
    [Theory]
    [InlineData(true, -1)]
    [InlineData(false, 1)]
    public void CompareDurationSeconds_OrdersNineMinutesAndFiveHoursThirtyMinutes(
        bool ascending,
        int expected)
    {
        var comparison = TrafficTableSort.CompareDurationSeconds(9 * 60, 5 * 60 * 60 + 30 * 60, ascending);

        Assert.Equal(expected, comparison);
    }
}