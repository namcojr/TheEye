using TheEye.Core.Models;

namespace TheEye.Core.Services;

public sealed record TrackerSnapshot(
    IReadOnlyList<IvaoPilot> Pilots,
    IReadOnlyList<IvaoAtc> Atc,
    IReadOnlyList<IvaoSupervisor> Supervisors,
    IvaoConnectionStats Stats,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Warnings)
{
    public int PilotCount => Pilots.Count;

    public int AtcCount => Atc.Count;

    public int SupervisorCount => Supervisors.Count;

    public bool IsPartial => Warnings.Count > 0;

    public IReadOnlyList<string> UserWarnings => Warnings
        .Where(warning => warning.StartsWith("Repeated ", StringComparison.Ordinal))
        .ToArray();
}

public sealed class TrackerRefreshException : Exception
{
    public TrackerRefreshException(string message, IReadOnlyList<string> warnings, Exception? innerException = null)
        : base(message, innerException)
    {
        Warnings = warnings;
    }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyList<string> UserWarnings => Warnings
        .Where(warning => warning.StartsWith("Repeated ", StringComparison.Ordinal))
        .ToArray();
}
