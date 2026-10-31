using Avalonia;
using Avalonia.Fonts.Inter;

namespace TheEye.Desktop;

internal static class Program
{
    // Avalonia needs an STA thread on Windows for the clipboard, the file
    // dialogs and the shell integration; on Linux the attribute is ignored.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// The same entry point the Avalonia designer and the tests look for by
    /// convention: platform detection picks X11 or Wayland on Linux and Win32 on
    /// Windows, and Inter is embedded so the window looks the same wherever it
    /// runs, whatever fonts the distribution ships.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // Avalonia's X11 backend exports a native menu through D-Bus for every
            // window it creates, transient popups included, and registers it with
            // com.canonical.AppMenu.Registrar. That service is missing on most
            // desktops, and the exporter in 11.3.22 forgets the task that unregisters
            // the window, so the resulting ServiceUnknown failure is rethrown by the
            // finalizer and reaches TaskScheduler.UnobservedTaskException as a crash.
            // The windows draw their own menu, so there is nothing to export.
            .With(new X11PlatformOptions { UseDBusMenu = false })
            .WithInterFont()
            .LogToTrace();
}
