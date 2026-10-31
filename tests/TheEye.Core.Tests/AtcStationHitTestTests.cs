using TheEye.Core.Maps;
using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class AtcStationHitTestTests
{
    [Fact]
    public void Find_ReturnsTheBadgeUnderThePointer()
    {
        var tower = CreateAtc("EDDB_TWR", "TWR");
        AtcStationHitTarget[] targets = [CreateTarget(tower, x: 100, y: 100, radius: 12)];

        Assert.Same(tower, AtcStationHitTest.Find(targets, 104, 105));
        Assert.Same(tower, AtcStationHitTest.Find(targets, 100, 100));
    }

    [Fact]
    public void Find_ReturnsNullOutsideEveryBadge()
    {
        var tower = CreateAtc("EDDB_TWR", "TWR");
        AtcStationHitTarget[] targets = [CreateTarget(tower, x: 100, y: 100, radius: 12)];

        Assert.Null(AtcStationHitTest.Find(targets, 100, 130));
        Assert.Null(AtcStationHitTest.Find([], 100, 100));
    }

    [Fact]
    public void Find_PrefersTheClosestBadgeWhenBadgesOverlap()
    {
        var ground = CreateAtc("EDDB_GND", "GND");
        var tower = CreateAtc("EDDB_TWR", "TWR");
        AtcStationHitTarget[] targets =
        [
            CreateTarget(ground, x: 100, y: 100, radius: 14),
            CreateTarget(tower, x: 112, y: 100, radius: 14)
        ];

        Assert.Same(tower, AtcStationHitTest.Find(targets, 109, 100));
        Assert.Same(ground, AtcStationHitTest.Find(targets, 103, 100));
    }

    [Fact]
    public void Find_PrefersTheSmallerFacilityWhenBadgesOverlapEqually()
    {
        var control = CreateAtc("EDWW_CTR", "CTR");
        var tower = CreateAtc("EDDB_TWR", "TWR");
        AtcStationHitTarget[] targets =
        [
            CreateTarget(control, x: 100, y: 100, radius: 14),
            CreateTarget(tower, x: 100, y: 100, radius: 14)
        ];

        Assert.Same(tower, AtcStationHitTest.Find(targets, 100, 104));
    }

    [Fact]
    public void Find_AcceptsThePointerOnTheBadgeEdge()
    {
        var tower = CreateAtc("EDDB_TWR", "TWR");
        AtcStationHitTarget[] targets = [CreateTarget(tower, x: 100, y: 100, radius: 12)];

        Assert.Same(tower, AtcStationHitTest.Find(targets, 111, 100));
        Assert.Null(AtcStationHitTest.Find(targets, 113, 100));
    }

    private static AtcStationHitTarget CreateTarget(IvaoAtc atc, double x, double y, double radius) =>
        new(atc, x, y, radius, AtcMarkerStyles.SortOrder(atc.Facility, atc.Callsign));

    private static IvaoAtc CreateAtc(string callsign, string facility) =>
        new()
        {
            Callsign = callsign,
            AtcSession = new IvaoAtcSession { Position = facility, Frequency = 118.275 },
            AtcPosition = new IvaoAtcPosition { Position = facility, AtcCallsign = callsign }
        };
}
