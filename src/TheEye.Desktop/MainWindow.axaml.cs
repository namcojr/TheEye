using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mapsui;
using Mapsui.Manipulations;
using Mapsui.UI.Avalonia;
using TheEye.Core.Maps;
using TheEye.Core.Settings;
using TheEye.Desktop.Dialogs;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Desktop;

/// <summary>
/// The Avalonia shell around the shared <see cref="MainViewModel"/>. The window
/// draws its own chrome, so the caption row moves the window by hand; the map is
/// a Mapsui control with the aircraft pins layered above it, exactly like the
/// Windows shell, and the wheel is forwarded from the host so zooming keeps
/// working while the pointer rests on a pin or an overlay.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MapWheelZoom _mapWheelZoom = new();
    private readonly DispatcherTimer _aircraftTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    public MainWindow()
    {
        TheEye.Rendering.AtcGlyphRenderer.Register();
        InitializeComponent();
        MapHost.AddHandler(PointerWheelChangedEvent, OnMapWheel, RoutingStrategies.Tunnel);
        _aircraftTimer.Tick += OnAircraftTick;
        Opened += (_, _) =>
        {
            _aircraftTimer.Start();
            RestoreTrafficColumnWidth();
        };
        // The splitter writes the column width itself, so the setting is only
        // recorded once the drag is over; the tunnel handler sees the release
        // before the splitter consumes it.
        TrafficSplitter.AddHandler(
            PointerReleasedEvent,
            OnTrafficSplitterReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        Closed += (_, _) =>
        {
            _aircraftTimer.Stop();
            _aircraftTimer.Tick -= OnAircraftTick;
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
    private void OnTrafficSplitterReleased(object? sender, PointerReleasedEventArgs e)
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
        // resized again, the column is already where the user dropped it. A click
        // that moved nothing writes nothing.
        var previous = viewModel.TrafficColumnWidth;
        viewModel.TrafficColumnWidth = width;
        if (viewModel.TrafficColumnWidth != previous)
        {
            viewModel.SaveSettings();
        }
    }

    private void OnAircraftTick(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized) ViewModel?.Map.AdvanceAircraft();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    /// <summary>
    /// The traffic browser column. Avalonia does not name definition objects, so
    /// the column is reached through the grid that owns it.
    /// </summary>
    private ColumnDefinition TrafficColumn => MainGrid.ColumnDefinitions[0];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeButton();
        }
    }

    /// <summary>
    /// The borderless caption row: a drag moves the window, a double click
    /// toggles maximise, and a press on a caption button is left to the button.
    /// </summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || BelongsToButton(e.Source as Visual))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        // A maximised window cannot be dragged, and Avalonia would throw rather
        // than ignore the call on platforms that only move normal windows.
        if (WindowState == WindowState.Normal)
        {
            BeginMoveDrag(e);
        }
    }

    private static bool BelongsToButton(Visual? source)
    {
        for (var visual = source; visual is not null and not Window; visual = visual.GetVisualParent())
        {
            if (visual is Button or ToggleButton or TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    /// <summary>
    /// Swaps the glyph and the accessible name with the window state, the way
    /// the Windows caption buttons do.
    /// </summary>
    private void UpdateMaximizeButton()
    {
        if (MaximizeButton is null || MaximizeGlyph is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Data = Geometry.Parse(maximized
            ? "M 2.5,0.5 L 9.5,0.5 L 9.5,7.5 M 0.5,2.5 L 7.5,2.5 L 7.5,9.5 L 0.5,9.5 Z"
            : "M 0.5,0.5 L 9.5,0.5 L 9.5,9.5 L 0.5,9.5 Z");

        var label = maximized ? "Restore" : "Maximize";
        ToolTip.SetTip(MaximizeButton, label);
        AutomationProperties.SetName(MaximizeButton, label);
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => _ = ShowSettingsAsync();

    private async Task ShowSettingsAsync()
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var dialog = new SettingsWindow(
            new SettingsViewModel(viewModel.CreateSettingsSnapshot()),
            IvaoSignInService.SignInAsync);

        // The title-bar close box leaves no result, which must not look like a save.
        if (await dialog.ShowDialog<bool?>(this) == true)
        {
            await ApplySettingsAsync(viewModel, dialog.ViewModel.CreateSettings());
        }
    }

    private static async Task ApplySettingsAsync(MainViewModel viewModel, TheEyeSettings settings)
    {
        try
        {
            await viewModel.ApplySettingsAsync(settings);
        }
        catch (Exception ex)
        {
            await MessageDialog.ShowAsync(
                "The new settings could not be applied.",
                ex.Message);
        }
    }

    /// <summary>
    /// A double click on a list row frames the selection, the same gesture the
    /// Windows shell binds to its list double clicks.
    /// </summary>
    private void OnTrafficDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { } viewModel && viewModel.LocateSelectedCommand.CanExecute(null))
        {
            viewModel.LocateSelectedCommand.Execute(null);
        }
    }

    private void OnPilotMarkerEnter(object? sender, PointerEventArgs e)
    {
        if (sender is Button { DataContext: PilotItem pilot })
        {
            ViewModel?.Map.SetHoveredPilot(pilot);
        }
    }

    private void OnPilotMarkerLeave(object? sender, PointerEventArgs e) =>
        ViewModel?.Map.SetHoveredPilot(null);

    private void OnPilotMarkerClick(object? sender, RoutedEventArgs e)
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

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(MapView);
        ViewModel?.Map.SetHoveredAtc(point.Properties.IsLeftButtonPressed
            ? null : new ScreenPosition(point.Position.X, point.Position.Y));
    }

    private void OnMapPointerExited(object? sender, PointerEventArgs e) =>
        ViewModel?.Map.SetHoveredAtc(null);

    private void OnMapTapped(object? sender, MapEventArgs e)
    {
        ViewModel?.Map.SelectAt(e.WorldPosition, e.ScreenPosition);
    }

    /// <summary>
    /// The aircraft pins and the map overlays sit above the Mapsui control, so a
    /// wheel turned over them never reaches it. Handle the tunnelling event on
    /// the host so the map and overlays share one accumulator and Mapsui does
    /// not also process the same wheel event.
    /// </summary>
    private void OnMapWheel(object? sender, PointerWheelEventArgs e)
    {
        if (MapView is null || MapView.Map is not { } map)
        {
            return;
        }

        var position = e.GetPosition(MapView);
        e.Handled = true;
        _mapWheelZoom.Zoom(
            map.Navigator,
            e.Delta.Y,
            new ScreenPosition(position.X, position.Y));
    }
}
