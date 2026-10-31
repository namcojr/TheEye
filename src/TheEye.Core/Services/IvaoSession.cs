using TheEye.Core.Api;

namespace TheEye.Core.Services;

/// <summary>
/// Holds the IVAO sign-in of the current member and keeps the API client's
/// bearer token fresh. The token lives for hours, the refresh token keeps the
/// sign-in alive without asking for the password again, and every change is
/// persisted through the callback so the next start resumes signed in.
/// </summary>
public sealed class IvaoSession : IIvaoAccessTokenSource, IDisposable
{
    /// <summary>Renew slightly early, so a request never races the expiry.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly IvaoOAuthClient _client;
    private readonly Func<IvaoOAuthTokens?, Task>? _persist;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IvaoOAuthTokens? _tokens;

    public IvaoSession(
        IvaoOAuthClient client,
        IvaoOAuthTokens? tokens = null,
        Func<IvaoOAuthTokens?, Task>? persist = null)
    {
        _client = client;
        _tokens = tokens;
        _persist = persist;
    }

    public bool IsSignedIn => _tokens is not null;

    public IvaoOAuthTokens? Tokens => _tokens;

    public async Task<string?> GetAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        var tokens = _tokens;
        if (tokens is null)
        {
            return null;
        }

        if (!forceRefresh && !tokens.IsExpired(DateTimeOffset.UtcNow, RefreshMargin))
        {
            return tokens.AccessToken;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var observed = tokens.AccessToken;
            if (_tokens is not { } current)
            {
                return null;
            }

            // Another caller may have renewed the token while this one queued.
            if (current.AccessToken != observed)
            {
                return current.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(current.RefreshToken))
            {
                // Nothing to renew with: hand the token over and let IVAO decide.
                return current.AccessToken;
            }

            try
            {
                var renewed = await _client.RefreshAsync(current.RefreshToken, cancellationToken);
                await StoreAsync(renewed);
                return renewed.AccessToken;
            }
            catch (IvaoOAuthException ex) when (!ex.RequiresSignIn)
            {
                // A transient refusal of the token endpoint (server error, bad
                // answer): the old access token may still be accepted.
                return current.AccessToken;
            }
            catch (IvaoOAuthException)
            {
                // IVAO revoked the grant; keeping a dead token would only hide it.
                await StoreAsync(null);
                return null;
            }
            catch (HttpRequestException)
            {
                return current.AccessToken;
            }
            catch (ObjectDisposedException)
            {
                // The session was replaced (settings changed) while this call
                // was in flight; the old token is still the best answer here.
                return current.AccessToken;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Adopts the tokens of a completed sign-in.</summary>
    public Task SignInAsync(IvaoOAuthTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return StoreAsync(tokens);
    }

    public Task SignOutAsync() => StoreAsync(null);

    /// <summary>
    /// Releases the HTTP client behind the token endpoint. The gate is left
    /// alone (it holds no OS handle) so a renewal already under way finishes.
    /// </summary>
    public void Dispose() => _client.Dispose();

    private async Task StoreAsync(IvaoOAuthTokens? tokens)
    {
        _tokens = tokens;
        if (_persist is { } persist)
        {
            await persist(tokens);
        }
    }
}
