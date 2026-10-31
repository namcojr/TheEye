using System.Windows;
using System.Windows.Threading;
using TheEye.Presentation.Threading;

namespace TheEye.Windows.Services;

/// <summary>
/// Hands the shared view models the WPF dispatcher, so an answer that arrives on
/// a worker thread is applied on the thread that owns the controls.
/// </summary>
public sealed class WpfDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Application.Current?.Dispatcher.CheckAccess() ?? true;

    public void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }
}
