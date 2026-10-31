using TheEye.Core.Api;
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
    }

    public TheEyeSettings Draft { get; }

    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public IReadOnlyList<IntervalOption> IntervalOptions { get; }

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
