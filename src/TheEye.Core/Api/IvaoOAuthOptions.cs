namespace TheEye.Core.Api;

/// <summary>
/// Credentials of an application registered with IVAO, used for the
/// authorisation-code sign-in that unlocks the authenticated feeds (recorded
/// tracks, session details) without asking for a consumer key. Application
/// credentials are issued on https://developers.ivao.aero, and every
/// application carries its own list of allowed redirect URLs, so
/// <see cref="RedirectUri"/> has to be one of them.
/// </summary>
public sealed class IvaoOAuthOptions
{
    // The endpoints come from https://api.ivao.aero/.well-known/openid-configuration.
    public const string DefaultAuthorizationEndpoint = "https://sso.ivao.aero/authorize";

    public const string DefaultTokenEndpoint = "https://api.ivao.aero/v2/oauth/token";

    public const string DefaultUserInfoEndpoint = "https://api.ivao.aero/v2/users/me";

    /// <summary>
    /// The scopes WebEye asks for, plus the tracker scope that covers the
    /// recorded sessions. IVAO grants scopes per application: an application
    /// that was only approved for a subset fails here and the trail notice says
    /// so, which is why the value is editable in the settings.
    /// </summary>
    public const string DefaultScopes = "openid profile tracker";

    /// <summary>
    /// A loopback redirect keeps the sign-in inside the machine: the browser
    /// answers http://localhost and nothing is exposed to the network. The port
    /// is fixed so the URL can be registered on IVAO once and reused afterwards.
    /// </summary>
    public const string DefaultRedirectUri = "http://localhost:48620/callback";

    /// <summary>Client id of the IVAO application. Empty means "not configured".</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Optional: IVAO issues a secret with every application, and a desktop
    /// client can send it along with the code. PKCE protects the exchange
    /// either way.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = DefaultRedirectUri;

    public string Scopes { get; set; } = DefaultScopes;

    public string AuthorizationEndpoint { get; set; } = DefaultAuthorizationEndpoint;

    public string TokenEndpoint { get; set; } = DefaultTokenEndpoint;

    public string UserInfoEndpoint { get; set; } = DefaultUserInfoEndpoint;

    public bool HasClientId => !string.IsNullOrWhiteSpace(ClientId);

    public bool HasClientSecret => !string.IsNullOrWhiteSpace(ClientSecret);

    public IvaoOAuthOptions Clone() => new()
    {
        ClientId = ClientId,
        ClientSecret = ClientSecret,
        RedirectUri = RedirectUri,
        Scopes = Scopes,
        AuthorizationEndpoint = AuthorizationEndpoint,
        TokenEndpoint = TokenEndpoint,
        UserInfoEndpoint = UserInfoEndpoint
    };
}
