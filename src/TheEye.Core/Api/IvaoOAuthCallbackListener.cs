using System.Net;
using System.Text;

namespace TheEye.Core.Api;

/// <summary>
/// What the loopback redirect carried back: an authorisation code, or the
/// reason IVAO refused (the user cancelled, the state did not match, …).
/// </summary>
public sealed record IvaoOAuthCallback(string? Code, string? Error, string? ErrorDescription)
{
    public bool IsSuccess => !string.IsNullOrEmpty(Code) && string.IsNullOrEmpty(Error);
}

/// <summary>
/// Receives the redirect of the authorisation-code flow. IVAO sends the browser
/// back to the loopback URL the application registered, so the app listens on
/// that address itself instead of running a web server somewhere.
/// </summary>
public static class IvaoOAuthCallbackListener
{
    /// <summary>
    /// Reads the code out of a redirect. A state that does not match the one
    /// this app generated is rejected: the redirect then belongs to another
    /// request and its code must not be redeemed.
    /// </summary>
    public static IvaoOAuthCallback Parse(Uri callbackUri, string expectedState)
    {
        ArgumentNullException.ThrowIfNull(callbackUri);

        var values = ParseQuery(callbackUri.Query);
        values.TryGetValue("error", out var error);
        values.TryGetValue("error_description", out var description);

        if (!string.IsNullOrEmpty(error))
        {
            return new IvaoOAuthCallback(null, error, description);
        }

        values.TryGetValue("state", out var state);
        if (!string.Equals(state, expectedState, StringComparison.Ordinal))
        {
            return new IvaoOAuthCallback(
                null,
                "state_mismatch",
                "The redirect does not belong to this sign-in attempt.");
        }

        values.TryGetValue("code", out var code);
        return string.IsNullOrEmpty(code)
            ? new IvaoOAuthCallback(null, "missing_code", "IVAO did not return an authorisation code.")
            : new IvaoOAuthCallback(code, null, null);
    }

    /// <summary>
    /// Waits for the browser to reach the loopback redirect and answers it with
    /// a page that tells the user to go back to the app. The wait ends with the
    /// first request that hits the redirect path, so a favicon or another tab
    /// cannot end it early.
    /// </summary>
    public static async Task<IvaoOAuthCallback> WaitAsync(
        Uri redirectUri,
        string expectedState,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);

        // HttpListener serves loopback prefixes without elevation; the browser
        // always talks plain http to localhost.
        var prefix = $"http://{redirectUri.Host}:{redirectUri.Port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(timeout ?? TimeSpan.FromMinutes(5));

        try
        {
            while (true)
            {
                var contextTask = listener.GetContextAsync();

                // A cancelled wait leaves this request pending; observing the
                // abandoned task keeps it from surfacing as an unhandled failure.
                _ = contextTask.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                var context = await contextTask.WaitAsync(lifetime.Token);
                var callback = Matches(context.Request.Url, redirectUri)
                    ? Parse(context.Request.Url!, expectedState)
                    : null;

                Respond(context.Response, callback);

                if (callback is not null)
                {
                    return callback;
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static bool Matches(Uri? requestUri, Uri redirectUri) =>
        requestUri is not null &&
        string.Equals(
            requestUri.AbsolutePath.TrimEnd('/'),
            redirectUri.AbsolutePath.TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            values[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return values;
    }

    private static void Respond(HttpListenerResponse response, IvaoOAuthCallback? callback)
    {
        var message = callback switch
        {
            null => "Nothing to see here.",
            { IsSuccess: true } => "Signed in. You can close this tab and return to The Eye of IVAO.",
            _ => "IVAO sign-in did not complete. Close this tab and try again from The Eye of IVAO."
        };

        response.StatusCode = callback is null ? 404 : 200;
        response.ContentType = "text/html; charset=utf-8";

        var html =
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
            "<title>The Eye of IVAO</title></head>" +
            "<body style=\"font-family:Segoe UI,sans-serif;background:#12171C;color:#E6EDF3;padding:48px\">" +
            "<h1 style=\"font-size:20px;margin:0 0 8px\">" + WebUtility.HtmlEncode(message) + "</h1>" +
            "<p style=\"color:#8B98A5;font-size:13px;margin:0\">The Eye of IVAO</p>" +
            "</body></html>";

        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;

        try
        {
            using var stream = response.OutputStream;
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is HttpListenerException or IOException)
        {
            // The browser may close the tab before the answer is written.
        }
    }
}
