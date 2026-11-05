namespace TheEye.Presentation.ViewModels;

public sealed record AirportTrafficStat(
    string Icao,
    int Inbound,
    int Outbound,
    double InboundPercent,
    double OutboundPercent)
{
    public string TrafficText => $"{Inbound} / {Outbound}";
}

public sealed record DashboardActivitySample(int Connections, double BarHeight);