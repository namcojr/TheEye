using System.Windows.Media;
using TheEye.Core.Settings;

namespace TheEye.Windows.ViewModels;

/// <summary>
/// Editable copy of the persisted settings. Nothing is written until the user
/// confirms, so cancel leaves the live configuration untouched.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(TheEyeSettings draft)
    {
        Draft = draft;
        Draft.Normalize();

        ThemeOptions =
        [
            CreateThemeOption(TheEyeThemes.Dark, "Dark", "#12171C", "#38BDF8"),
            CreateThemeOption(TheEyeThemes.Light, "Light", "#F2F5F8", "#0369A1"),
            CreateThemeOption(TheEyeThemes.MidnightBlue, "Midnight Blue", "#07101F", "#4C9FFF"),
            CreateThemeOption(TheEyeThemes.Forest, "Forest", "#0D1713", "#6EE7B7"),
            CreateThemeOption(TheEyeThemes.Sunset, "Sunset", "#1A1210", "#FB923C")
        ];

        IntervalOptions =
        [
            new IntervalOption(10, "10 s"),
            new IntervalOption(15, "15 s"),
            new IntervalOption(30, "30 s"),
            new IntervalOption(60, "1 min"),
            new IntervalOption(120, "2 min")
        ];

        foreach (var option in ThemeOptions)
        {
            option.PropertyChanged += (_, _) =>
            {
                if (option.IsSelected)
                {
                    SelectTheme(option);
                }
            };
        }

        foreach (var option in IntervalOptions)
        {
            option.PropertyChanged += (_, _) =>
            {
                if (option.IsSelected)
                {
                    SelectInterval(option);
                }
            };
        }

        var theme = ThemeOptions.FirstOrDefault(option =>
            string.Equals(option.Name, TheEyeThemes.Normalize(Draft.Theme), StringComparison.Ordinal))
            ?? ThemeOptions[0];
        theme.IsSelected = true;

        var interval = IntervalOptions.FirstOrDefault(option => option.Seconds == Draft.RefreshSeconds)
            ?? IntervalOptions[2];
        interval.IsSelected = true;
    }

    public TheEyeSettings Draft { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<IntervalOption> IntervalOptions { get; }

    public string SettingsPath => string.Empty;

    public TheEyeSettings CreateSettings() => Draft.Clone();

    private ThemeOption CreateThemeOption(string name, string label, string background, string accent) =>
        new(name, label, Brush(background), Brush(accent));

    private void SelectTheme(ThemeOption selected)
    {
        Draft.Theme = selected.Name;
        foreach (var option in ThemeOptions)
        {
            if (!ReferenceEquals(option, selected))
            {
                option.IsSelected = false;
            }
        }

        OnPropertyChanged(nameof(Draft));
    }

    private void SelectInterval(IntervalOption selected)
    {
        Draft.RefreshSeconds = selected.Seconds;
        foreach (var option in IntervalOptions)
        {
            if (!ReferenceEquals(option, selected))
            {
                option.IsSelected = false;
            }
        }

        OnPropertyChanged(nameof(Draft));
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

public sealed class ThemeOption : ObservableObject
{
    private bool _isSelected;

    public ThemeOption(string name, string label, Brush background, Brush accent)
    {
        Name = name;
        Label = label;
        BackgroundPreview = background;
        AccentPreview = accent;
    }

    public string Name { get; }

    public string Label { get; }

    public Brush BackgroundPreview { get; }

    public Brush AccentPreview { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class IntervalOption : ObservableObject
{
    private bool _isSelected;

    public IntervalOption(int seconds, string label)
    {
        Seconds = seconds;
        Label = label;
    }

    public int Seconds { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
