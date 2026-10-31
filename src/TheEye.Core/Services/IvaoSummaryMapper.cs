using TheEye.Core.Models;

namespace TheEye.Core.Services;

public static class IvaoSummaryMapper
{
    public static IvaoPilot ToPilot(this IvaoPilotSummary summary) => new()
    {
        Id = summary.Id,
        UserId = summary.UserId,
        Callsign = summary.Callsign,
        ConnectionType = summary.ConnectionType,
        IsMilitary = summary.IsMilitary,
        IsWorldTour = summary.IsWorldTour,
        LastTrack = summary.LastTrack,
        FlightPlan = summary.FlightPlan
    };

    public static IvaoAtc ToAtc(this IvaoAtcSummary summary) => new()
    {
        Id = summary.Id,
        UserId = summary.UserId,
        Callsign = summary.Callsign,
        ConnectionType = summary.ConnectionType,
        AtcSession = summary.AtcSession,
        AtcPosition = summary.AtcPosition,
        Subcenter = summary.Subcenter
    };
}
