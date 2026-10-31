using Avalonia.Threading;
using TheEye.Presentation.Threading;

namespace TheEye.Desktop.Services;

/// <summary>
/// The UI thread of the Avalonia shell. The shared view models post through
/// <see cref="UiThread"/>, so the only thing the shell owns is the toolkit's
/// dispatcher.
/// </summary>
public sealed class AvaloniaDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
