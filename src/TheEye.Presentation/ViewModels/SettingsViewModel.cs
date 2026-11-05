using TheEye.Core.Api;
using TheEye.Core.Maps;
using TheEye.Core.Settings;
using TheEye.Presentation.Theming;

namespace TheEye.Presentation.ViewModels;

/// <summary>
/// Editable copy of the persisted settings. Nothing is written until the user
/// confirms, so cancel leaves the live configuration untouched.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private string _signInStatus = string.Empty;

    public SettingsViewModel(TheEyeSettings draft)
    {
        Draft = draft;
        Draft.Normalize();

        // The tiles are built from the same table the shells paint with, so a
        // preview can never drift from the palette it promises.
        ThemeOptions = ThemeCatalog.All
            .Select(palette => new ThemeOption(palette.Name, palette.Label, palette.WindowBackground, palette.Accent))
            .ToList();

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

        FontSizeOptions = UiFontSizes.All.Select(size => new UiFontSizeOption(size)).ToList();
        foreach (var option in FontSizeOptions)
        {
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(UiFontSizeOption.IsSelected) && option.IsSelected)
                {
                    SelectFontSize(option);
                }
            };
        }

        FontSizeOptions.First(option =>
            string.Equals(option.Name, UiFontSizes.Normalize(Draft.UiFontSize), StringComparison.Ordinal)).IsSelected = true;

        // The basemap lives beside the theme: the shell palette recolours the
        // traffic layers, the map style decides what is drawn underneath them.
        MapStyleOptions = MapBasemap.Styles
            .Select(style => new MapStyleOption(style.Id, style.Label))
            .ToList();

        foreach (var option in MapStyleOptions)
        {
            option.PropertyChanged += (_, _) =>
            {
                if (option.IsSelected)
                {
                    SelectMapStyle(option);
                }
            };
        }

        var mapStyle = MapStyleOptions.FirstOrDefault(option =>
            string.Equals(option.Id, MapBasemap.Normalize(Draft.MapStyle), StringComparison.Ordinal))
            ?? MapStyleOptions[0];
        mapStyle.IsSelected = true;

        OwnColorOptions = CreateColorOptions(Draft.OwnAircraftColor, color => Draft.OwnAircraftColor = color);
        FriendColorOptions = CreateColorOptions(Draft.FriendAircraftColor, color => Draft.FriendAircraftColor = color);
        UpdateColorAvailability();
    }

    public TheEyeSettings Draft { get; }

    public int AircraftGlyphSize
    {
        get => Draft.AircraftGlyphSize;
        set
        {
            var size = Math.Clamp(value, TheEyeSettings.MinAircraftGlyphSize, TheEyeSettings.MaxAircraftGlyphSize);
            if (Draft.AircraftGlyphSize == size) return;
            Draft.AircraftGlyphSize = size;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AircraftGlyphSizeLabel));
        }
    }

    public string AircraftGlyphSizeLabel => $"{AircraftGlyphSize} px";

    public double FontScale => UiFontSizes.Multiplier(Draft.UiFontSize);

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<UiFontSizeOption> FontSizeOptions { get; }

    /// <summary>The basemaps the map can be drawn with, offline first.</summary>
    public IReadOnlyList<MapStyleOption> MapStyleOptions { get; }

    public IReadOnlyList<IntervalOption> IntervalOptions { get; }

    /// <summary>Colours for the signed-in member's own aircraft.</summary>
    public IReadOnlyList<ColorOption> OwnColorOptions { get; }

    /// <summary>Colours for friends' aircraft; the one picked for the own aircraft is disabled.</summary>
    public IReadOnlyList<ColorOption> FriendColorOptions { get; }

    public string SettingsPath => string.Empty;

    /// <summary>The loopback address the sign-in comes back to, ready to paste on IVAO.</summary>
    public string RedirectUri => string.IsNullOrWhiteSpace(Draft.OAuthRedirectUri)
        ? IvaoOAuthOptions.DefaultRedirectUri
        : Draft.OAuthRedirectUri.Trim();

    public string RedirectUriHint =>
        $"Add {RedirectUri} to the redirect URLs of your IVAO application, otherwise IVAO refuses to send the browser back.";

    public bool IsSignedIn => Draft.HasOAuthTokens;

    /// <summary>Who is signed in, shown under the sign-in buttons.</summary>
    public string SignInSummary => IsSignedIn
        ? string.IsNullOrWhiteSpace(Draft.OAuthAccount)
            ? "Signed in to IVAO."
            : $"Signed in as {Draft.OAuthAccount}."
        : "Not signed in.";

    public string SignInStatus
    {
        get => _signInStatus;
        private set => SetProperty(ref _signInStatus, value);
    }

    public TheEyeSettings CreateSettings() => Draft.Clone();

    /// <summary>
    /// The credentials the sign-in must use, taken from the draft so a client id
    /// typed a moment ago is honoured without saving first.
    /// </summary>
    public IvaoOAuthOptions CreateOAuthOptions() => new()
    {
        ClientId = Draft.OAuthClientId,
        ClientSecret = Draft.OAuthClientSecret,
        RedirectUri = RedirectUri,
        Scopes = Draft.OAuthScopes
    };

    /// <summary>Adopts a completed sign-in into the draft; saving persists it.</summary>
    public void ApplySignIn(IvaoOAuthTokens tokens, IvaoOAuthAccount? account)
    {
        Draft.OAuthAccessToken = tokens.AccessToken;
        Draft.OAuthRefreshToken = tokens.RefreshToken ?? Draft.OAuthRefreshToken;
        Draft.SetOAuthExpiry(tokens.ExpiresAt);
        Draft.OAuthAccount = account?.DisplayName ?? Draft.OAuthAccount;
        RaiseSignInChanges();
    }

    public void ClearSignIn()
    {
        Draft.ClearOAuthTokens();
        RaiseSignInChanges();
    }

    public void ReportSignInStatus(string status) => SignInStatus = status;

    /// <summary>Refreshes the redirect hint after the URL itself was edited.</summary>
    public void RefreshRedirectHint() => RaiseSignInChanges();

    private void RaiseSignInChanges()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(SignInSummary));
        OnPropertyChanged(nameof(RedirectUri));
        OnPropertyChanged(nameof(RedirectUriHint));
        OnPropertyChanged(nameof(Draft));
    }

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

    private void SelectMapStyle(MapStyleOption selected)
    {
        Draft.MapStyle = selected.Id;
        foreach (var option in MapStyleOptions)
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

    private void SelectFontSize(UiFontSizeOption selected)
    {
        Draft.UiFontSize = selected.Name;
        foreach (var option in FontSizeOptions)
        {
            if (!ReferenceEquals(option, selected))
            {
                option.IsSelected = false;
            }
        }

        OnPropertyChanged(nameof(FontScale));
        OnPropertyChanged(nameof(Draft));
    }

    private List<ColorOption> CreateColorOptions(string current, Action<string> apply)
    {
        var options = AircraftHighlightColors.All
            .Select(color => new ColorOption(color.Name, color.Label, color.Hex))
            .ToList();

        foreach (var option in options)
        {
            option.IsSelected = string.Equals(option.Name, current, StringComparison.Ordinal);
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ColorOption.IsSelected) || !option.IsSelected) return;
                apply(option.Name);
                foreach (var other in options.Where(other => !ReferenceEquals(other, option)))
                {
                    other.IsSelected = false;
                }

                UpdateColorAvailability();
                OnPropertyChanged(nameof(Draft));
            };
        }

        return options;
    }

    private void UpdateColorAvailability()
    {
        // Called while the second list is still being built.
        if (OwnColorOptions is null || FriendColorOptions is null) return;

        foreach (var option in OwnColorOptions)
        {
            option.IsEnabled = !string.Equals(option.Name, Draft.FriendAircraftColor, StringComparison.Ordinal);
        }

        foreach (var option in FriendColorOptions)
        {
            option.IsEnabled = !string.Equals(option.Name, Draft.OwnAircraftColor, StringComparison.Ordinal);
        }
    }
}

public sealed class ColorOption : ObservableObject
{
    private bool _isSelected;
    private bool _isEnabled = true;

    public ColorOption(string name, string label, string hex)
    {
        Name = name;
        Label = label;
        Hex = hex;
    }

    public string Name { get; }

    public string Label { get; }

    /// <summary>The colour as "#RRGGBB", painted by the view.</summary>
    public string Hex { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

public sealed class ThemeOption : ObservableObject
{
    private bool _isSelected;

    public ThemeOption(string name, string label, string backgroundPreview, string accentPreview)
    {
        Name = name;
        Label = label;
        BackgroundPreview = backgroundPreview;
        AccentPreview = accentPreview;
    }

    public string Name { get; }

    public string Label { get; }

    /// <summary>The theme's window colour as "#RRGGBB", painted by the view.</summary>
    public string BackgroundPreview { get; }

    /// <summary>The theme's accent colour as "#RRGGBB", painted by the view.</summary>
    public string AccentPreview { get; }

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

public sealed class UiFontSizeOption : ObservableObject
{
    private bool _isSelected;

    public UiFontSizeOption(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>One basemap in the map style list: the stored id and the label the shells show.</summary>
public sealed class MapStyleOption : ObservableObject
{
    private bool _isSelected;

    public MapStyleOption(string id, string label)
    {
        Id = id;
        Label = label;
    }

    /// <summary>The value persisted in <c>TheEyeSettings.MapStyle</c>.</summary>
    public string Id { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
