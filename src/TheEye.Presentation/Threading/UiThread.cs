namespace TheEye.Presentation.Threading;

/// <summary>
/// The UI thread of whichever shell is running. A view model never touches a
/// windowing toolkit directly, so the same view models drive the WPF window and
/// the Avalonia one; each shell installs its dispatcher at startup.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>True when the calling thread is the one that owns the controls.</summary>
    bool CheckAccess();

    /// <summary>Queues <paramref name="action"/> on the UI thread.</summary>
    void Post(Action action);
}

/// <summary>
/// The dispatcher the view models marshal through. Until a shell installs one
/// the actions run inline, which is what unit tests and headless callers want.
/// </summary>
public static class UiThread
{
    private static IUiDispatcher _dispatcher = new ImmediateDispatcher();

    public static void Initialize(IUiDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public static bool CheckAccess() => _dispatcher.CheckAccess();

    public static void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Post(action);
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();
    }
}
