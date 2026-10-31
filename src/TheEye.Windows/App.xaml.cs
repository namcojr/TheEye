using System.IO;
using System.Windows;
using System.Windows.Threading;
using TheEye.Core.Settings;
using TheEye.Presentation.Threading;
using TheEye.Presentation.ViewModels;
using TheEye.Windows.Services;

namespace TheEye.Windows;

public partial class App : Application
{
    private MainViewModel? _viewModel;
    private bool _reportedCrash;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // The shared view models marshal through this dispatcher, and the WPF
        // pipeline decodes the pictures they ask for, before the first one runs.
        UiThread.Initialize(new WpfDispatcher());

        var store = new SettingsStore();
        var settings = store.Load();
        var theme = new WpfThemeService();
        theme.Apply(settings.Theme);

        _viewModel = new MainViewModel(store, settings, theme, new WpfImagePipeline());
        _viewModel.Initialize();

        var window = new MainWindow
        {
            DataContext = _viewModel
        };

        MainWindow = window;
        window.Show();

        try
        {
            await _viewModel.StartAsync();
        }
        catch (Exception ex)
        {
            ReportCrash("Live traffic could not be loaded.", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.SaveSettings();
        _viewModel?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ReportCrash("An unexpected error occurred.", e.Exception);
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
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
        WriteCrashLog(exception);

        if (_reportedCrash)
        {
            return;
        }

        _reportedCrash = true;
        MessageBox.Show(
            $"{headline}{Environment.NewLine}{Environment.NewLine}{exception.Message}",
            "The Eye of IVAO",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TheEyeIVAO");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never take the application down.
        }
    }
}
