using System.Net;
using TheEye.Core.Api;

namespace TheEye.Core.Tests;

public sealed class IvaoOAuthClientTests
{
    private const string TokenPath = "/v2/oauth/token";
    private const string UserInfoPath = "/v2/users/me";

    private const string TokenPayload = """
        {
          "access_token": "access-1",
          "refresh_token": "refresh-1",
          "token_type": "Bearer",
          "expires_in": 3600,
          "scope": "openid profile tracker"
        }
        """;

    [Fact]
    public void CreateAuthorizationRequest_CarriesTheClientTheRedirectAndTheChallenge()
    {
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TokenPayload)));

        var request = client.CreateAuthorizationRequest();
        var query = ParseQuery(request.AuthorizeUrl.Query);

        Assert.Equal("https://test.local/authorize", request.AuthorizeUrl.GetLeftPart(UriPartial.Path));
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("http://localhost:48620/callback", query["redirect_uri"]);
        Assert.Equal("openid profile tracker", query["scope"]);
        Assert.Equal(request.State, query["state"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(IvaoOAuthClient.CreateCodeChallenge(request.CodeVerifier), query["code_challenge"]);
        Assert.Equal("client-id", request.ClientId);
    }

    // RFC 7636 appendix B: the challenge of this exact verifier.
    [Fact]
    public void CreateCodeChallenge_MatchesTheSpecification()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            IvaoOAuthClient.CreateCodeChallenge(verifier));
    }

    [Fact]
    public void CreateCodeVerifier_IsUrlSafeAndLongEnough()
    {
        var verifier = IvaoOAuthClient.CreateCodeVerifier();

        Assert.Equal(43, verifier.Length);
        Assert.All(verifier, character => Assert.True(
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '~',
            $"'{character}' is not allowed in a PKCE verifier"));
    }

    [Fact]
    public void CreateAuthorizationRequest_UsesAFreshStateAndVerifierEveryTime()
    {
        using var client = CreateClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TokenPayload)));

        var first = client.CreateAuthorizationRequest();
        var second = client.CreateAuthorizationRequest();

        Assert.NotEqual(first.State, second.State);
        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
    }

    [Fact]
    public void CreateAuthorizationRequest_RefusesWithoutAClientId()
    {
        var options = CreateOptions();
        options.ClientId = "  ";
        using var client = new IvaoOAuthClient(
            options,
            new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TokenPayload))));

        Assert.Throws<InvalidOperationException>(() => client.CreateAuthorizationRequest());
    }

    [Fact]
    public async Task ExchangeCodeAsync_PostsTheCodeTheRedirectAndTheVerifier()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TokenPayload));
        using var client = CreateClient(handler);
        var request = client.CreateAuthorizationRequest();

        await client.ExchangeCodeAsync(request, "the-code");

        Assert.Equal(TokenPath, Assert.Single(handler.Requests));
        Assert.Equal("POST", Assert.Single(handler.Methods));

        var body = ParseForm(Assert.Single(handler.Bodies));
        Assert.Equal("authorization_code", body["grant_type"]);
        Assert.Equal("the-code", body["code"]);
        Assert.Equal("http://localhost:48620/callback", body["redirect_uri"]);
        Assert.Equal("client-id", body["client_id"]);
        Assert.Equal("client-secret", body["client_secret"]);
        Assert.Equal(request.CodeVerifier, body["code_verifier"]);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ParsesTheTokenSet()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TokenPayload));
        using var client = CreateClient(handler);

        var tokens = await client.ExchangeCodeAsync(client.CreateAuthorizationRequest(), "the-code");

        Assert.Equal("access-1", tokens.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.NotNull(tokens.ExpiresAt);
        Assert.False(tokens.IsExpired(DateTimeOffset.UtcNow));
        Assert.True(tokens.IsExpired(DateTimeOffset.UtcNow.AddHours(2)));
    }

    [Fact]
    public async Task ExchangeCodeAsync_OmitsTheSecretWhenTheClientDoesNotHaveOne()
    {
        var options = CreateOptions();
        options.ClientSecret = string.Empty;
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(TokenPayload));
        using var client = new IvaoOAuthClient(options, new HttpClient(handler));

        await client.ExchangeCodeAsync(client.CreateAuthorizationRequest(), "the-code");

        Assert.False(ParseForm(Assert.Single(handler.Bodies)).ContainsKey("client_secret"));
    }

    [Fact]
    public async Task ExchangeCodeAsync_ReportsTheErrorIvaoSent()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Code expired"}"""));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<IvaoOAuthException>(() =>
            client.ExchangeCodeAsync(client.CreateAuthorizationRequest(), "the-code"));

        Assert.Equal("invalid_grant", exception.Error);
        Assert.Equal("Code expired", exception.Description);
        Assert.Equal(400, exception.StatusCode);
        Assert.True(exception.RequiresSignIn);
        Assert.Contains("Code expired", exception.Message);
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheRefreshTokenWhenNoneComesBack()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """{"access_token":"access-2","expires_in":"3600"}"""));
        using var client = CreateClient(handler);

        var tokens = await client.RefreshAsync("refresh-1");

        Assert.Equal("access-2", tokens.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        Assert.False(tokens.IsExpired(DateTimeOffset.UtcNow));

        var body = ParseForm(Assert.Single(handler.Bodies));
        Assert.Equal("refresh_token", body["grant_type"]);
        Assert.Equal("refresh-1", body["refresh_token"]);
        Assert.Equal("client-id", body["client_id"]);
    }

    [Fact]
    public async Task GetAccountAsync_ReadsTheSignedInMember()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """{"id":123456,"firstName":"Jane","lastName":"Doe"}"""));
        using var client = CreateClient(handler);

        var account = await client.GetAccountAsync("access-1");

        Assert.NotNull(account);
        Assert.Equal("123456", account.Id);
        Assert.Equal("123456 (Jane Doe)", account.DisplayName);
        Assert.Equal(UserInfoPath, Assert.Single(handler.Requests));
        Assert.Equal("Bearer access-1", Assert.Single(handler.Authorizations));
    }

    [Fact]
    public async Task GetAccountAsync_ReturnsNullInsteadOfFailingTheSignIn()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = CreateClient(handler);

        Assert.Null(await client.GetAccountAsync("access-1"));
    }

    private static IvaoOAuthClient CreateClient(HttpMessageHandler handler) =>
        new(CreateOptions(), new HttpClient(handler));

    private static IvaoOAuthOptions CreateOptions() => new()
    {
        ClientId = "client-id",
        ClientSecret = "client-secret",
        RedirectUri = "http://localhost:48620/callback",
        Scopes = "openid profile tracker",
        AuthorizationEndpoint = "https://test.local/authorize",
        TokenEndpoint = "https://test.local" + TokenPath,
        UserInfoEndpoint = "https://test.local" + UserInfoPath
    };

    private static Dictionary<string, string> ParseQuery(string query) => ParseForm(query.TrimStart('?'));

    private static Dictionary<string, string> ParseForm(string body) =>
        body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : string.Empty);
}
