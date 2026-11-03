using TheEye.Core.Reference;

namespace TheEye.Presentation.ViewModels;

public sealed record RunwayEndOption(string Designation, double TrueHeadingDegrees, AirportRunway Runway)
{
    public string ReciprocalDesignation => Runway.RunwayDesignations
        .Split('/')
        .First(designation => designation != Designation);

    public string DisplayText => $"{Designation}   {Runway.LengthMetres:N0} m";

    public string HeadingText => $"{TrueHeadingDegrees:000}°T";

    public string LengthText => $"{Runway.LengthMetres:N0} m";

    public string WidthText => $"{Runway.WidthMetres:0} m";
}