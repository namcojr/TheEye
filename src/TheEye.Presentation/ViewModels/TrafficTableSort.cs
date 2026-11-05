namespace TheEye.Presentation.ViewModels;

public static class TrafficTableSort
{
    public static int CompareDurationSeconds(long? leftSeconds, long? rightSeconds, bool ascending) =>
        ascending
            ? Nullable.Compare(leftSeconds, rightSeconds)
            : Nullable.Compare(rightSeconds, leftSeconds);
}