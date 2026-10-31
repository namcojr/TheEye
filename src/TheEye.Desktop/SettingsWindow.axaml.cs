using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TheEye.Core.Api;
using TheEye.Presentation.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Desktop;

/// <summary>
/// The Avalonia settings dialog. It edits a draft copy of the settings and hands
/// the result back through the dialog result, so opening and cancelling leaves
/// the running configuration untouched, exactly like the Windows shell.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Func<IvaoOAuthOptions, CancellationToken, Task<IvaoSignInResult>>? _signIn;
    private bool _signInBusy;

    /// <summary>
    /// The runtime XAML loader needs a parameterless constructor to reach the
    /// compiled markup; the shell always passes a prepared draft instead.
    /// </summary>
    public SettingsWindow()
        : this(new SettingsViewModel(new TheEye.Core.Settings.TheEyeSettings()))
    {
    }

    public SettingsWindow(
        SettingsViewModel viewModel,
        Func<IvaoOAuthOptions, CancellationToken, Task<IvaoSignInResult>>? signIn = null)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _signIn = signIn;
        DataContext = viewModel;
        UpdateSignInButtons();
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>The borderless caption row drags the dialog, like the main shell.</summary>
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || BelongsToButton(e.Source as Visual))
        {
            return;
        }

        BeginMoveDrag(e);
    }

    private static bool BelongsToButton(Visual? source)
    {
        for (var visual = source; visual is not null and not Window; visual = visual.GetVisualParent())
        {
            if (visual is Button or TextBox)
            {
                return true;
            }
        }

        return false;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnRedirectUriLostFocus(object? sender, RoutedEventArgs e) => ViewModel.RefreshRedirectHint();

    private async void OnSignInClick(object? sender, RoutedEventArgs e)
    {
        if (_signIn is null)
        {
            return;
        }

        ViewModel.ReportSignInStatus("Waiting for IVAO in your browser…");
        SetSignInBusy(true);

        try
        {
            var result = await _signIn(ViewModel.CreateOAuthOptions(), CancellationToken.None);
            if (result.IsSuccess)
            {
                ViewModel.ApplySignIn(result.Tokens!, result.Account);
                ViewModel.ReportSignInStatus(
                    "Signed in. Save the settings to start using the IVAO session for the trail.");
            }
            else
            {
                ViewModel.ReportSignInStatus(result.Error);
            }
        }
        catch (Exception ex)
        {
            ViewModel.ReportSignInStatus($"The sign-in failed: {ex.Message}");
        }
        finally
        {
            SetSignInBusy(false);
        }
    }

    private void OnSignOutClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.ClearSignIn();
        ViewModel.ReportSignInStatus("Signed out. Save the settings to drop the stored IVAO tokens.");
        UpdateSignInButtons();
    }

    private void SetSignInBusy(bool busy)
    {
        _signInBusy = busy;
        SignInButton.Content = busy ? "Waiting for IVAO…" : "Sign in with IVAO";
        UpdateSignInButtons();
    }

    /// <summary>
    /// Signing in twice would replace the tokens the running session is already
    /// using, so the button is disabled once IVAO is signed in: signing out is
    /// the way to switch accounts.
    /// </summary>
    private void UpdateSignInButtons()
    {
        var signedIn = ViewModel.IsSignedIn;
        SignInButton.IsEnabled = !_signInBusy && !signedIn;
        ToolTip.SetTip(SignInButton, signedIn
            ? "You are already signed in to IVAO. Sign out first to use another account."
            : "Sign in with your IVAO account");

        if (SignOutButton is not null)
        {
            SignOutButton.IsEnabled = signedIn && !_signInBusy;
        }
    }
}
