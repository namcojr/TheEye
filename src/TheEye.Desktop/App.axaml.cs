using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using TheEye.Core.Settings;
using TheEye.Desktop.Dialogs;
using TheEye.Desktop.Services;
using TheEye.Presentation.Threading;
using TheEye.Presentation.ViewModels;

namespace TheEye.Desktop;

public partial class App : Application
{
    private MainViewModel? _viewModel;
    private bool _reportedCrash;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // The shared view models marshal through this dispatcher, and the
            // pipeline decodes the pictures they ask for, before the first one runs.
            UiThread.Initialize(new AvaloniaDispatcher());

            var store = new SettingsStore();
            var settings = store.Load();
            var theme = new AvaloniaThemeService();
            theme.Apply(settings.Theme);

            _viewModel = new MainViewModel(store, settings, theme, new AvaloniaImagePipeline());
            _viewModel.Initialize();

            var window = new MainWindow { DataContext = _viewModel };
            window.Opened += OnMainWindowOpened;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        try
        {
            await _viewModel.StartAsync();
        }
        catch (Exception ex)
        {
            ReportCrash("Live traffic could not be loaded.", ex);
        }
    }

    /// <summary>Saving on shutdown keeps the map view for the next launch.</summary>
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e) => _viewModel?.SaveSettings();

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _viewModel?.SaveSettings();
        _viewModel?.Dispose();
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ReportCrash("An unexpected error occurred.", e.Exception);
    }

    private void OnDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            ReportCrash("A fatal error occurred.", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        ReportCrash("A background task failed.", e.Exception);
    }

    private void ReportCrash(string headline, Exception exception)
    {
        CrashLog.Write(exception);

        if (_reportedCrash)
        {
            return;
        }

        _reportedCrash = true;

        if (Dispatcher.UIThread.CheckAccess())
        {
            _ = MessageDialog.ShowAsync(headline, exception.Message);
        }
        else
        {
            Dispatcher.UIThread.Post(() => _ = MessageDialog.ShowAsync(headline, exception.Message));
        }
    }
}
