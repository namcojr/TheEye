using System.Windows;
using TheEye.Core.Api;
using TheEye.Presentation.Services;
using TheEye.Windows.Services;
using TheEye.Presentation.ViewModels;

namespace TheEye.Windows;

public partial class SettingsWindow : Window
{
    private readonly Func<IvaoOAuthOptions, CancellationToken, Task<IvaoSignInResult>>? _signIn;
    private bool _signInBusy;

    public SettingsWindow(
        SettingsViewModel viewModel,
        Func<IvaoOAuthOptions, CancellationToken, Task<IvaoSignInResult>>? signIn = null)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _signIn = signIn;
        DataContext = viewModel;

        // A PasswordBox cannot bind its value, so the draft is filled in by hand.
        ClientSecretBox.Password = viewModel.Draft.OAuthClientSecret;
        UpdateSignInButtons();
    }

    public SettingsViewModel ViewModel { get; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowFrame.Apply(this);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnClientSecretChanged(object sender, RoutedEventArgs e) =>
        ViewModel.Draft.OAuthClientSecret = ClientSecretBox.Password;

    private void OnRedirectUriLostFocus(object sender, RoutedEventArgs e) => ViewModel.RefreshRedirectHint();

    private async void OnSignInClick(object sender, RoutedEventArgs e)
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

    private void OnSignOutClick(object sender, RoutedEventArgs e)
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
        SignInButton.ToolTip = signedIn
            ? "You are already signed in to IVAO. Sign out first to use another account."
            : "Sign in with your IVAO account";

        if (SignOutButton is not null)
        {
            SignOutButton.IsEnabled = signedIn && !_signInBusy;
        }
    }
}
