using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheEye.Core.Api;

/// <summary>
/// Everything the browser needs to start a sign-in, together with the secrets
/// the token exchange must present afterwards. The verifier never leaves the
/// app: only its SHA-256 challenge travels through the browser (PKCE).
/// </summary>
public sealed record IvaoAuthorizationRequest(
    Uri AuthorizeUrl,
    string State,
    string CodeVerifier,
    string RedirectUri,
    string ClientId);

/// <summary>
/// The token set IVAO hands back for an authorisation code (or a refresh).
/// <see cref="ExpiresAt"/> is null when the response carried no lifetime, in
/// which case the token is used until IVAO refuses it.
/// </summary>
public sealed record IvaoOAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now, TimeSpan margin = default) =>
        ExpiresAt is { } expiresAt && expiresAt - margin <= now;
}

/// <summary>The member behind a sign-in, as returned by <c>/v2/users/me</c>.</summary>
public sealed record IvaoOAuthAccount(string? Id, string? FirstName, string? LastName)
{
    public string DisplayName
    {
        get
        {
            var name = string.Join(
                ' ',
                new[] { FirstName, LastName }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

            if (string.IsNullOrEmpty(name))
            {
                return Id ?? string.Empty;
            }

            return string.IsNullOrEmpty(Id) ? name : $"{Id} ({name})";
        }
    }
}

/// <summary>
/// A refusal from the IVAO authorisation server. <see cref="Error"/> keeps the
/// OAuth error code (<c>invalid_grant</c>, <c>access_denied</c>, …) so callers
/// can tell a revoked grant from a network problem.
/// </summary>
public sealed class IvaoOAuthException : Exception
{
    public IvaoOAuthException(string error, string? description, int? statusCode = null)
        : base(string.IsNullOrWhiteSpace(description) ? error : $"{error}: {description}")
    {
        Error = error;
        Description = description;
        StatusCode = statusCode;
    }

    public string Error { get; }

    public string? Description { get; }

    public int? StatusCode { get; }

    /// <summary>True when IVAO no longer honours the stored grant: signing in again is required.</summary>
    public bool RequiresSignIn => Error is "invalid_grant" or "invalid_client" or "unauthorized_client";
}

/// <summary>
/// The authorisation-code flow of the IVAO SSO: builds the sign-in URL, turns
/// the returned code into tokens and renews them with the refresh token. The
/// endpoints and their capabilities are published at
/// <c>https://api.ivao.aero/.well-known/openid-configuration</c>, which
/// advertises PKCE (<c>S256</c>) and the refresh-token grant.
/// </summary>
public sealed class IvaoOAuthClient : IDisposable
{
    private readonly IvaoOAuthOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public IvaoOAuthClient(IvaoOAuthOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
    }

    public IvaoOAuthOptions Options => _options;

    /// <summary>
    /// Builds the URL the browser must open. The state ties the redirect back to
    /// this request, and the challenge means an intercepted code is useless
    /// without the verifier kept in <see cref="IvaoAuthorizationRequest"/>.
    /// </summary>
    public IvaoAuthorizationRequest CreateAuthorizationRequest(
        string? codeVerifier = null,
        string? state = null)
    {
        if (!_options.HasClientId)
        {
            throw new InvalidOperationException(
                "An IVAO application client id is required before signing in.");
        }

        var verifier = codeVerifier ?? CreateCodeVerifier();
        var requestState = state ?? CreateState();
        var redirectUri = string.IsNullOrWhiteSpace(_options.RedirectUri)
            ? IvaoOAuthOptions.DefaultRedirectUri
            : _options.RedirectUri.Trim();
        var scopes = string.IsNullOrWhiteSpace(_options.Scopes)
            ? IvaoOAuthOptions.DefaultScopes
            : _options.Scopes.Trim();

        var query = new StringBuilder("response_type=code")
            .Append("&client_id=").Append(Uri.EscapeDataString(_options.ClientId.Trim()))
            .Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri))
            .Append("&scope=").Append(Uri.EscapeDataString(scopes))
            .Append("&state=").Append(Uri.EscapeDataString(requestState))
            .Append("&code_challenge=").Append(Uri.EscapeDataString(CreateCodeChallenge(verifier)))
            .Append("&code_challenge_method=S256");

        var endpoint = string.IsNullOrWhiteSpace(_options.AuthorizationEndpoint)
            ? IvaoOAuthOptions.DefaultAuthorizationEndpoint
            : _options.AuthorizationEndpoint.Trim();
        var separator = endpoint.Contains('?') ? '&' : '?';

        return new IvaoAuthorizationRequest(
            new Uri($"{endpoint}{separator}{query}"),
            requestState,
            verifier,
            redirectUri,
            _options.ClientId.Trim());
    }

    /// <summary>Trades the code from the redirect for an access and a refresh token.</summary>
    public Task<IvaoOAuthTokens> ExchangeCodeAsync(
        IvaoAuthorizationRequest request,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("The authorisation code is missing.", nameof(code));
        }

        return RequestTokensAsync(
            [
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", request.RedirectUri),
                new KeyValuePair<string, string>("code_verifier", request.CodeVerifier)
            ],
            cancellationToken);
    }

    /// <summary>
    /// Renews the access token. IVAO may rotate the refresh token; when the
    /// response carries none, the one that was sent stays valid.
    /// </summary>
    public async Task<IvaoOAuthTokens> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("A refresh token is required.", nameof(refreshToken));
        }

        var tokens = await RequestTokensAsync(
            [
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            ],
            cancellationToken);

        return string.IsNullOrWhiteSpace(tokens.RefreshToken)
            ? tokens with { RefreshToken = refreshToken }
            : tokens;
    }

    /// <summary>
    /// Reads <c>/v2/users/me</c> with the new token, purely to show who is
    /// signed in. A failure here is never fatal: the account label is
    /// decoration, the token is what the app needs.
    /// </summary>
    public async Task<IvaoOAuthAccount?> GetAccountAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            string.IsNullOrWhiteSpace(_options.UserInfoEndpoint)
                ? IvaoOAuthOptions.DefaultUserInfoEndpoint
                : _options.UserInfoEndpoint.Trim());
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("user", out var nested) &&
                nested.ValueKind == JsonValueKind.Object)
            {
                root = nested;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new IvaoOAuthAccount(
                ReadId(root),
                ReadString(root, "firstName"),
                ReadString(root, "lastName"));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>A fresh PKCE verifier: 32 random bytes, base64url, 43 characters.</summary>
    public static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 challenge: base64url(SHA-256(verifier)), as RFC 7636 defines it.</summary>
    public static string CreateCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeVerifier);
        return Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    /// <summary>
    /// The state that ties a redirect to the request that started it. IVAO
    /// echoes it back untouched.
    /// </summary>
    public static string CreateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    private async Task<IvaoOAuthTokens> RequestTokensAsync(
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            payload[field.Key] = field.Value;
        }

        payload["client_id"] = _options.ClientId.Trim();
        if (_options.HasClientSecret)
        {
            payload["client_secret"] = _options.ClientSecret.Trim();
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            string.IsNullOrWhiteSpace(_options.TokenEndpoint)
                ? IvaoOAuthOptions.DefaultTokenEndpoint
                : _options.TokenEndpoint.Trim())
        {
            Content = new FormUrlEncodedContent(payload)
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;

        using var document = ParseJson(body, statusCode);
        var root = document.RootElement;

        var accessToken = ReadString(root, "access_token");
        if (!response.IsSuccessStatusCode || string.IsNullOrEmpty(accessToken))
        {
            throw CreateError(root, statusCode, body);
        }

        return new IvaoOAuthTokens(accessToken, ReadString(root, "refresh_token"), ReadExpiry(root));
    }

    private static JsonDocument ParseJson(string body, int statusCode)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new IvaoOAuthException("invalid_response", Truncate(body, 400), statusCode);
        }
    }

    private static IvaoOAuthException CreateError(JsonElement root, int statusCode, string body)
    {
        string? error = null;
        string? description = null;

        if (root.ValueKind == JsonValueKind.Object)
        {
            error = ReadString(root, "error");
            description = ReadString(root, "error_description") ?? ReadString(root, "message");
        }

        return new IvaoOAuthException(
            error ?? $"http_{statusCode}",
            description ?? Truncate(body, 400),
            statusCode);
    }

    private static DateTimeOffset? ReadExpiry(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("expires_in", out var value))
        {
            return null;
        }

        double? seconds = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => null
        };

        return seconds is > 0 ? DateTimeOffset.UtcNow.AddSeconds(seconds.Value) : null;
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static string? ReadId(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString()?.Trim(),
            _ => null
        };
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
