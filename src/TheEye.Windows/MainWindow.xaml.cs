using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Mapsui;
using Mapsui.Manipulations;
using TheEye.Core.Maps;
using TheEye.Presentation.Services;
using TheEye.Windows.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Windows;

public partial class MainWindow : Window
{
    private readonly MapWheelZoom _mapWheelZoom = new();

    public MainWindow()
    {
        TheEye.Rendering.AtcGlyphRenderer.Register();
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

        var dialog = new SettingsWindow(
            new SettingsViewModel(viewModel.CreateSettingsSnapshot()),
            IvaoSignInService.SignInAsync)
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

    private void OnPilotMarkerEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button { DataContext: PilotItem pilot })
        {
            ViewModel?.Map.SetHoveredPilot(pilot);
        }
    }

    private void OnPilotMarkerLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        ViewModel?.Map.SetHoveredPilot(null);

    private void OnPilotMarkerClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not Button { DataContext: PilotItem pilot })
        {
            return;
        }

        viewModel.Map.SetHoveredPilot(null);
        if (ReferenceEquals(viewModel.SelectedPilot, pilot))
        {
            // Clicking the marker that is already selected should still bring the
            // flight back into view after the map has been panned away, unless the
            // user switched map-click centring off.
            if (viewModel.Map is { CenterOnMapClick: true } map)
            {
                map.CenterOnPilot(pilot);
            }

            return;
        }

        // A marker click selects like the traffic list, but only centres the map
        // while the option in Settings > Map behaviour is enabled.
        viewModel.SelectPilotFromMap(pilot);
    }

    private void OnMapTapped(object sender, MapEventArgs e)
    {
        ViewModel?.Map.SelectAt(e.WorldPosition, e.ScreenPosition);
    }

    /// <summary>
    /// The aircraft marker buttons and the map overlays sit above the Mapsui
    /// control, so a wheel turned over them never reaches it. Forwarding the
    /// wheel from the map host keeps zooming while the pointer rests on a
    /// marker, a station badge or a zoom button.
    /// </summary>
    private void OnMapWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (MapControl?.Map is not { } map)
        {
            return;
        }

        var position = e.GetPosition(MapControl);
        e.Handled = true;
        _mapWheelZoom.Zoom(map.Navigator, e.Delta / 120.0, new ScreenPosition(position.X, position.Y));
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
