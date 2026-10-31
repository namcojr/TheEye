using System.Diagnostics;
using System.Net;
using System.Net.Http;
using TheEye.Core.Api;

namespace TheEye.Windows.Services;

/// <summary>
/// Outcome of an interactive IVAO sign-in. <see cref="Error"/> is empty on
/// success and otherwise holds a sentence the settings window can show as is.
/// </summary>
public sealed record IvaoSignInResult(IvaoOAuthTokens? Tokens, IvaoOAuthAccount? Account, string Error)
{
    public bool IsSuccess => Tokens is not null && string.IsNullOrEmpty(Error);

    public static IvaoSignInResult Failed(string error) => new(null, null, error);
}

/// <summary>
/// Runs the authorisation-code sign-in: opens the IVAO page in the default
/// browser, waits on the loopback redirect, and redeems the code for tokens.
/// The browser keeps the password and the second factor to itself; the app only
/// ever sees the code and the tokens.
/// </summary>
public static class IvaoSignInService
{
    public static async Task<IvaoSignInResult> SignInAsync(
        IvaoOAuthOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.HasClientId)
        {
            return IvaoSignInResult.Failed(
                "Enter the client id of your IVAO application first. IVAO issues one for every " +
                "application on developers.ivao.aero.");
        }

        if (!Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out var redirectUri) ||
            !redirectUri.IsLoopback)
        {
            return IvaoSignInResult.Failed(
                "The redirect URL must be a loopback address such as " +
                IvaoOAuthOptions.DefaultRedirectUri + ".");
        }

        using var client = new IvaoOAuthClient(options);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        IvaoAuthorizationRequest request;
        try
        {
            request = client.CreateAuthorizationRequest();
        }
        catch (Exception ex)
        {
            return IvaoSignInResult.Failed(ex.Message);
        }

        // Listen before the browser opens, so a fast redirect cannot arrive
        // while nothing is watching the port.
        var callbackTask = IvaoOAuthCallbackListener.WaitAsync(
            redirectUri,
            request.State,
            TimeSpan.FromMinutes(5),
            lifetime.Token);
        Observe(callbackTask);

        if (callbackTask.IsFaulted)
        {
            // The port is taken (or the prefix is not usable): opening the
            // browser now would only strand the user on IVAO's login page.
            return IvaoSignInResult.Failed(DescribeListenerFailure(callbackTask.Exception, redirectUri));
        }

        try
        {
            Process.Start(new ProcessStartInfo(request.AuthorizeUrl.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            lifetime.Cancel();
            return IvaoSignInResult.Failed($"The browser could not be opened: {ex.Message}");
        }

        IvaoOAuthCallback callback;
        try
        {
            callback = await callbackTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return IvaoSignInResult.Failed("IVAO sign-in timed out. Close the browser tab and try again.");
        }
        catch (Exception ex) when (ex is HttpListenerException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            return IvaoSignInResult.Failed(DescribeListenerFailure(ex, redirectUri));
        }

        if (!callback.IsSuccess)
        {
            return IvaoSignInResult.Failed(Describe(callback));
        }

        try
        {
            var tokens = await client.ExchangeCodeAsync(request, callback.Code!, cancellationToken);
            var account = await client.GetAccountAsync(tokens.AccessToken, cancellationToken);
            return new IvaoSignInResult(tokens, account, string.Empty);
        }
        catch (IvaoOAuthException ex)
        {
            return IvaoSignInResult.Failed($"IVAO refused the sign-in: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            return IvaoSignInResult.Failed($"IVAO could not be reached: {ex.Message}");
        }
    }

    private static string DescribeListenerFailure(Exception? exception, Uri redirectUri)
    {
        var reason = (exception as AggregateException)?.GetBaseException() ?? exception;

        return reason is null
            ? $"Nothing could listen on {redirectUri}, so the browser answer could not be received."
            : $"Nothing could listen on {redirectUri} ({reason.Message}). Another program may be using " +
              "the port, or the redirect URL is not allowed by the IVAO application.";
    }

    private static string Describe(IvaoOAuthCallback callback) => callback.Error switch
    {
        "access_denied" => "IVAO sign-in was cancelled.",
        "state_mismatch" => "The redirect did not match the sign-in request. Try again from the app.",
        _ when !string.IsNullOrWhiteSpace(callback.ErrorDescription) =>
            $"IVAO did not complete the sign-in: {callback.ErrorDescription}",
        null or "" => "IVAO did not return an authorisation code.",
        var error => $"IVAO did not complete the sign-in ({error})."
    };

    /// <summary>
    /// Keeps an abandoned wait from surfacing as an unhandled background
    /// failure when the browser never comes back.
    /// </summary>
    private static void Observe(Task<IvaoOAuthCallback> task) =>
        _ = task.ContinueWith(
            finished => _ = finished.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
