using TheEye.Presentation.Services;

namespace TheEye.Presentation.ViewModels;

/// <summary>How the reported wind reads on the selected runway end.</summary>
public enum RunwayWindKind
{
    /// <summary>No usable METAR wind group.</summary>
    None,
    Calm,
    Variable,
    Headwind,
    /// <summary>Wind straight across the runway, no head or tail component.</summary>
    Crosswind,
    Tailwind
}

/// <summary>
/// Everything the runway diagram draws: the selected runway end, its
/// reciprocal and the METAR wind. The diagram is north-up, so headings and the
/// wind direction are true degrees, as in the rest of the airport panel.
/// </summary>
public sealed record RunwayDiagramState(
    string Designation,
    string ReciprocalDesignation,
    double TrueHeadingDegrees,
    double LengthMetres,
    double WidthMetres,
    RunwayWindKind WindKind,
    double? WindDirectionDegrees,
    double WindSpeedKnots)
{
    public bool IsTailwind => WindKind == RunwayWindKind.Tailwind;

    /// <summary>Wind with a direction worth drawing as an arrow.</summary>
    public bool HasWindVector => WindDirectionDegrees is not null && WindKind
        is RunwayWindKind.Headwind or RunwayWindKind.Crosswind or RunwayWindKind.Tailwind;

    public static RunwayDiagramState Create(RunwayEndOption runwayEnd, MetarWind? wind)
    {
        var kind = Classify(runwayEnd.TrueHeadingDegrees, wind);
        return new RunwayDiagramState(
            runwayEnd.Designation,
            runwayEnd.ReciprocalDesignation,
            runwayEnd.TrueHeadingDegrees,
            runwayEnd.Runway.LengthMetres,
            runwayEnd.Runway.WidthMetres,
            kind,
            wind?.DirectionDegrees,
            wind?.SpeedKnots ?? 0);
    }

    public static RunwayWindKind Classify(double runwayHeadingDegrees, MetarWind? wind)
    {
        if (wind is not { } reported)
        {
            return RunwayWindKind.None;
        }

        if (reported.SpeedKnots < 0.5)
        {
            return RunwayWindKind.Calm;
        }

        if (MetarWindCalculator.Calculate(reported, runwayHeadingDegrees) is not { } components)
        {
            return RunwayWindKind.Variable;
        }

        return components.TailwindKnots > 0 ? RunwayWindKind.Tailwind
            : components.HeadwindKnots > 0 ? RunwayWindKind.Headwind
            : RunwayWindKind.Crosswind;
    }
}
