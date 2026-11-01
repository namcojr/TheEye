using Mapsui.Extensions;
using Mapsui.Manipulations;
using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Core.Services;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

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

    [Fact]
    public void Find_CyclesBadgesAndAllOverlappingSectors()
    {
        var ground = CreateAtc("EHAM_GND", "GND", id: 1);
        var tower = CreateAtc("EHAM_TWR", "TWR", id: 2);
        var approach = CreateAtc("EHAM_APP", "APP", id: 3);
        var center = CreateAtc("EHAA_CTR", "CTR", id: 4);
        AtcStationHitTarget[] targets = [CreateTarget(ground, 100, 100, 12)];
        IvaoAtc[] regions = [tower, approach, center];

        Assert.Same(ground, AtcStationHitTest.Find(targets, 100, 100, regions: regions));
        Assert.Same(tower, AtcStationHitTest.Find(targets, 100, 100, ground.Id, regions));
        Assert.Same(approach, AtcStationHitTest.Find(targets, 100, 100, tower.Id, regions));
        Assert.Same(center, AtcStationHitTest.Find(targets, 100, 100, approach.Id, regions));
        Assert.Same(ground, AtcStationHitTest.Find(targets, 100, 100, center.Id, regions));
        Assert.Same(ground, AtcStationHitTest.Find(targets, 100, 100, 999, regions));
    }

    [Fact]
    public void Find_CyclesOverlappingSectorsWithoutBadges()
    {
        var tower = CreateAtc("EHAM_TWR", "TWR", id: 1);
        var center = CreateAtc("EHAA_CTR", "CTR", id: 2);
        IvaoAtc[] regions = [tower, center];

        Assert.Same(tower, AtcStationHitTest.Find([], 100, 100, regions: regions));
        Assert.Same(center, AtcStationHitTest.Find([], 100, 100, tower.Id, regions));
        Assert.Same(tower, AtcStationHitTest.Find([], 100, 100, center.Id, regions));
    }

    [Fact]
    public void Find_DoesNotRepeatStationsWithBothBadgeAndPolygon()
    {
        var tower = CreateAtc("EHAM_TWR", "TWR", id: 1);
        var center = CreateAtc("EHAA_CTR", "CTR", id: 2);
        AtcStationHitTarget[] targets = [CreateTarget(tower, 100, 100, 12)];
        IvaoAtc[] regions = [tower, center, center];

        Assert.Same(center, AtcStationHitTest.Find(targets, 100, 100, tower.Id, regions));
        Assert.Same(tower, AtcStationHitTest.Find(targets, 100, 100, center.Id, regions));
    }

    [Fact]
    public void Find_CyclesOverlappingBadgesAtDifferentCoordinates()
    {
        var ground = CreateAtc("EDDB_GND", "GND", id: 1);
        var tower = CreateAtc("EDDB_TWR", "TWR", id: 2);
        var distant = CreateAtc("EDDT_TWR", "TWR", id: 3);
        AtcStationHitTarget[] targets =
        [
            CreateTarget(ground, 100, 100, 14),
            CreateTarget(tower, 112, 100, 14),
            CreateTarget(distant, 200, 100, 14)
        ];

        Assert.Same(tower, AtcStationHitTest.Find(targets, 109, 100));
        Assert.Same(ground, AtcStationHitTest.Find(targets, 109, 100, tower.Id));
        Assert.Same(tower, AtcStationHitTest.Find(targets, 109, 100, ground.Id));
    }

    [Fact]
    public void SelectAt_CyclesGroundBadgeAndPolygonTower()
    {
        var ground = CreateAtc("EHAM_GND", "GND", id: 1);
        ground.AtcPosition!.Airport = new IvaoAirport { Icao = "EHAM", Latitude = 52.3086, Longitude = 4.7639 };
        var tower = CreateAtc("EHAM_TWR", "TWR", id: 2);
        tower.AtcPosition!.RegionMapPolygon =
        [
            new IvaoCoordinate(51, 3), new IvaoCoordinate(51, 7),
            new IvaoCoordinate(54, 7), new IvaoCoordinate(54, 3)
        ];
        var placement = Assert.Single(IvaoMapLayers.CreateAtcStationPlacements([ground, tower]));
        Assert.Same(ground, placement.Station);
        using var model = new MapViewModel(new Images()) { CenterOnMapClick = false };
        model.Initialize(new MapPalette("#000000", "#111111", "#222222", "#FFFFFF", "#FFFFFF", true),
            new GeoCoordinate(52.5, 5), 10000);
        model.Map!.Navigator.SetSize(800, 600);
        model.UpdateTraffic(new TrackerSnapshot([], [ground, tower], [], new IvaoConnectionStats(),
            DateTimeOffset.UtcNow, []));
        var screen = model.Map.Navigator.Viewport.WorldToScreen(placement.Point);
        var position = new ScreenPosition(screen.X, screen.Y);

        model.SelectAt(placement.Point, position);
        Assert.Same(ground, model.SelectedAtc?.Atc);
        model.SelectAt(placement.Point, position);
        Assert.Same(tower, model.SelectedAtc?.Atc);
        model.SelectAt(placement.Point, position);
        Assert.Same(ground, model.SelectedAtc?.Atc);

        model.ShowAtc = false;
        model.SelectAt(placement.Point, position);
        Assert.Null(model.SelectedAtc);
    }

    private sealed class Images : IImagePipeline, IAircraftIconCache, IImageCache
    {
        public IAircraftIconCache AircraftIcons => this;
        public IImageCache SessionImages => this;
        public object Get(AircraftGlyph glyph) => glyph;
        public object? TryGet(string? url) => null;
        public Task<object?> GetAsync(string? url, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);
        public void Dispose() { }
    }

    private static AtcStationHitTarget CreateTarget(IvaoAtc atc, double x, double y, double radius) =>
        new(atc, x, y, radius, AtcMarkerStyles.SortOrder(atc.Facility, atc.Callsign));

    private static IvaoAtc CreateAtc(string callsign, string facility, long id = 0) =>
        new()
        {
            Id = id,
            Callsign = callsign,
            AtcSession = new IvaoAtcSession { Position = facility, Frequency = 118.275 },
            AtcPosition = new IvaoAtcPosition { Position = facility, AtcCallsign = callsign }
        };
}
