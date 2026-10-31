using TheEye.Core.Models;

namespace TheEye.Core.Services;

public sealed record TrackerSnapshot(
    IReadOnlyList<IvaoPilot> Pilots,
    IReadOnlyList<IvaoAtc> Atc,
    IvaoConnectionStats Stats,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<string> Warnings)
{
    public int PilotCount => Pilots.Count;

    public int AtcCount => Atc.Count;

    public bool IsPartial => Warnings.Count > 0;
}

public sealed class TrackerRefreshException : Exception
{
    public TrackerRefreshException(string message, IReadOnlyList<string> warnings, Exception? innerException = null)
        : base(message, innerException)
    {
        Warnings = warnings;
    }

    public IReadOnlyList<string> Warnings { get; }
}
