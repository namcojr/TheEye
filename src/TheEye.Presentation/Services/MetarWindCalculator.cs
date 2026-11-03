using System.Text.RegularExpressions;

namespace TheEye.Presentation.Services;

public readonly record struct MetarWind(double? DirectionDegrees, double SpeedKnots)
{
    public bool HasVariableDirection => DirectionDegrees is null;
}

public readonly record struct RunwayWindComponents(
    double CrosswindKnots,
    bool CrosswindFromRight,
    double HeadwindKnots,
    double TailwindKnots);

public static class MetarWindCalculator
{
    private static readonly Regex WindGroup = new(
        @"(?<!\S)(?<direction>\d{3}|VRB)(?<speed>\d{1,3})(?:G\d{2,3})?(?<unit>KT|MPS|KMH)(?!\S)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static MetarWind? Parse(string? report)
    {
        if (string.IsNullOrWhiteSpace(report))
        {
            return null;
        }

        var match = WindGroup.Match(report);
        if (!match.Success || !double.TryParse(match.Groups["speed"].Value, out var speed))
        {
            return null;
        }

        double? direction = match.Groups["direction"].Value == "VRB"
            ? null
            : double.TryParse(match.Groups["direction"].Value, out var parsedDirection)
                ? parsedDirection % 360
                : null;
        if (match.Groups["direction"].Value != "VRB" && direction is null)
        {
            return null;
        }

        var knots = match.Groups["unit"].Value switch
        {
            "KT" => speed,
            "MPS" => speed * 1.943844492,
            "KMH" => speed / 1.852,
            _ => double.NaN
        };

        return double.IsFinite(knots) ? new MetarWind(direction, knots) : null;
    }

    public static RunwayWindComponents? Calculate(MetarWind wind, double runwayHeadingDegrees)
    {
        if (wind.DirectionDegrees is not { } direction)
        {
            return null;
        }

        var angle = (direction - runwayHeadingDegrees) * Math.PI / 180;
        var along = wind.SpeedKnots * Math.Cos(angle);
        var across = wind.SpeedKnots * Math.Sin(angle);
        var crosswind = Math.Abs(across);

        if (crosswind < 0.05)
        {
            crosswind = 0;
        }

        var headwind = Math.Max(0, along);
        var tailwind = Math.Max(0, -along);
        if (headwind < 0.05)
        {
            headwind = 0;
        }

        if (tailwind < 0.05)
        {
            tailwind = 0;
        }

        return new RunwayWindComponents(crosswind, crosswind > 0 && across > 0, headwind, tailwind);
    }
}