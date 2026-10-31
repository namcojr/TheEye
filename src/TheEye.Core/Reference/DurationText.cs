namespace TheEye.Core.Reference;

/// <summary>
/// Formats the durations the tracker reports in seconds — time online, endurance,
/// estimated time enroute — the way the pilot card shows them. A long count is
/// kept short on purpose: "16 h 32 min" reads better in a narrow panel than a
/// clock ("16:32:07") that never looks like an amount of time.
/// </summary>
public static class DurationText
{
    public static string Format(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "0 min";
        }

        if (duration.TotalMinutes < 1)
        {
            return $"{duration.TotalSeconds:0} s";
        }

        if (duration.TotalHours < 1)
        {
            return $"{duration.TotalMinutes:0} min";
        }

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;
        return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";
    }

    /// <summary>Formats a count of seconds, which is how the tracker reports durations.</summary>
    public static string FromSeconds(long? seconds) =>
        seconds is { } value ? Format(TimeSpan.FromSeconds(Math.Max(0, value))) : "—";
}
