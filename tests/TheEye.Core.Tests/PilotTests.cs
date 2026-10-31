using TheEye.Core.Models;

namespace TheEye.Core.Tests;

public sealed class PilotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EstimatedArrival_ComesFromTheRemainingDistanceAndTheGroundSpeed()
    {
        var pilot = CreatePilot(arrivalDistance: 120, groundSpeed: 480);

        // 120 NM at 480 kt is a quarter of an hour.
        Assert.Equal(Now.AddMinutes(15), pilot.EstimatedArrival(Now));
    }

    [Fact]
    public void EstimatedArrival_IsNullWithoutATrack() =>
        Assert.Null(new IvaoPilot().EstimatedArrival(Now));

    [Fact]
    public void EstimatedArrival_IsNullWhenStandingStill()
    {
        // A time derived from a taxi speed would be nonsense.
        Assert.Null(CreatePilot(arrivalDistance: 120, groundSpeed: 0).EstimatedArrival(Now));
        Assert.Null(CreatePilot(arrivalDistance: 120, groundSpeed: null).EstimatedArrival(Now));
    }

    [Fact]
    public void EstimatedArrival_IsNullWhenThereIsNoDistanceLeft()
    {
        // An arrival in the past would look like an error.
        Assert.Null(CreatePilot(arrivalDistance: 0, groundSpeed: 250).EstimatedArrival(Now));
        Assert.Null(CreatePilot(arrivalDistance: null, groundSpeed: 250).EstimatedArrival(Now));
    }

    [Fact]
    public void EstimatedArrival_IsNullWhileTaxiing()
    {
        // A taxi speed over the whole route would put the arrival days away.
        var pilot = CreatePilot(arrivalDistance: 120, groundSpeed: 15, onGround: true);

        Assert.Null(pilot.EstimatedArrival(Now));
    }

    private static IvaoPilot CreatePilot(double? arrivalDistance, int? groundSpeed, bool? onGround = null) =>
        new()
        {
            LastTrack = new IvaoLastTrack
            {
                ArrivalDistance = arrivalDistance,
                GroundSpeed = groundSpeed,
                OnGround = onGround
            }
        };
}
