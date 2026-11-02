using TheEye.Core.Maps;
using TheEye.Core.Models;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Core.Tests;

public sealed class PilotItemTests
{
    [Fact]
    public void UsesExtendedFlightPlanFieldsFromTheMainPilotPayload()
    {
        var images = new EmptyImages();
        var item = new PilotItem(
            new IvaoPilot
            {
                FlightPlan = new IvaoFlightPlan
                {
                    Speed = "N0450",
                    Level = "FL350",
                    PeopleOnBoard = 153,
                    AircraftEquipments = ["S", "D"],
                    AircraftTransponderTypes = ["C", "E"],
                    EetSeconds = 3600,
                    EnduranceSeconds = 7200,
                    DepartureTimeSeconds = 12 * 60 * 60,
                    ActualDepartureTimeSeconds = 13 * 60 * 60
                }
            },
            images,
            images);

        Assert.Equal("N0450", item.CruisingSpeed);
        Assert.Equal("FL350", item.CruisingLevel);
        Assert.Equal("153", item.PeopleOnBoardText);
        Assert.Equal("S D", item.EquipmentText);
        Assert.Equal("C E", item.TransponderTypeText);
        Assert.True(item.Pilot.FlightPlan!.HasExtendedDetails);
    }

    private sealed class EmptyImages : IAircraftIconCache, IImageCache
    {
        public object Get(AircraftGlyph glyph) => glyph;

        public object? TryGet(string? url) => null;

        public Task<object?> GetAsync(string? url, CancellationToken cancellationToken = default) =>
            Task.FromResult<object?>(null);

        public void Dispose() { }
    }
}