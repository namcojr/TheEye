using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Mapsui;
using TheEye.Windows.Services;
using TheEye.Windows.ViewModels;

namespace TheEye.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => UpdateMaximizeButton();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowFrame.Apply(this);
        UpdateMaximizeButton();
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            WindowFrame.Apply(this);
            return;
        }

        Dispatcher.Invoke(() => WindowFrame.Apply(this));
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new SettingsWindow(new SettingsViewModel(viewModel.CreateSettingsSnapshot()))
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _ = ApplySettingsAsync(viewModel, dialog.ViewModel.CreateSettings());
        }
    }

    private static async Task ApplySettingsAsync(MainViewModel viewModel, TheEye.Core.Settings.TheEyeSettings settings)
    {
        try
        {
            await viewModel.ApplySettingsAsync(settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The new settings could not be applied.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "The Eye of IVAO",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnTrafficDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel is { } viewModel && viewModel.LocateSelectedCommand.CanExecute(null))
        {
            viewModel.LocateSelectedCommand.Execute(null);
        }
    }

    private void OnPilotMarkerClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && sender is Button { DataContext: PilotItem pilot })
        {
            viewModel.SelectedPilot = pilot;
        }
    }

    private void OnMapTapped(object sender, MapEventArgs e)
    {
        ViewModel?.Map.SelectAt(e.WorldPosition, e.ScreenPosition);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void UpdateMaximizeButton()
    {
        if (MaximizeButton is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        if (MaximizeButton.Content is TextBlock glyph)
        {
            glyph.Text = maximized ? "\uE923" : "\uE922";
        }

        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");
    }
}
