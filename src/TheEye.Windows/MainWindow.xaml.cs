using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
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
    private readonly TheEye.Rendering.BackdropMapRenderer _mapRenderer = new();
    private readonly DispatcherTimer _aircraftTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    public MainWindow()
    {
        TheEye.Rendering.MapRenderers.Register();
        InitializeComponent();

        // SKElement rasterises on the CPU, which is where the backdrop pays off.
        MapControl.SetMapRenderer(_mapRenderer);
        StateChanged += (_, _) => UpdateMaximizeButton();
        ThemeService.ThemeChanged += OnThemeChanged;
        _aircraftTimer.Tick += OnAircraftTick;
        Loaded += (_, _) =>
        {
            _aircraftTimer.Start();
            RestoreTrafficColumnWidth();
        };
    }

    /// <summary>
    /// Puts the traffic browser back at the width the user left it. The column
    /// lives in the XAML rather than in a binding because a splitter writes the
    /// width of the column itself, not of the panel inside it.
    /// </summary>
    private void RestoreTrafficColumnWidth()
    {
        if (ViewModel is { } viewModel)
        {
            TrafficColumn.Width = new GridLength(viewModel.TrafficColumnWidth);
        }
    }

    /// <summary>Records the width the splitter settled on, and persists it at once.</summary>
    private void OnTrafficSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var width = Math.Round(TrafficColumn.ActualWidth);
        if (width <= 0)
        {
            return;
        }

        // The setter clamps and keeps the value in the settings; the window is not
        // resized again, the column is already where the user dropped it. A drag
        // that ended where it started writes nothing.
        var previous = viewModel.TrafficColumnWidth;
        viewModel.TrafficColumnWidth = width;
        if (viewModel.TrafficColumnWidth != previous)
        {
            viewModel.SaveSettings();
        }
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
        _aircraftTimer.Stop();
        _aircraftTimer.Tick -= OnAircraftTick;
        ThemeService.ThemeChanged -= OnThemeChanged;
        _mapRenderer.Dispose();
        base.OnClosed(e);
    }

    private void OnAircraftTick(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized && ViewModel is { IsMapView: true } viewModel)
        {
            viewModel.Map.AdvanceAircraft();
        }
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

    private void OnMapMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var point = e.GetPosition(MapControl);
        viewModel.Map.SetHoveredAtc(e.LeftButton == System.Windows.Input.MouseButtonState.Pressed
            ? null : new ScreenPosition(point.X, point.Y));
        MapControl.Cursor = viewModel.Map.IsPointerOverAircraft ? System.Windows.Input.Cursors.Hand : null;
    }

    private void OnMapMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ViewModel?.Map.SetHoveredAtc(null);
        MapControl.Cursor = null;
    }

    private void OnMapTapped(object sender, MapEventArgs e)
    {
        ViewModel?.Map.TapAt(e.WorldPosition, e.ScreenPosition);
    }

    /// <summary>
    /// The map overlays sit above the Mapsui control, so a wheel turned over them
    /// never reaches it. Forwarding the wheel from the map host keeps zooming
    /// while the pointer rests on a station badge or a zoom button.
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
