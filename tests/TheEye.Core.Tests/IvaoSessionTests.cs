using System.Net;
using TheEye.Core.Api;
using TheEye.Core.Services;

namespace TheEye.Core.Tests;

public sealed class IvaoSessionTests
{
    private const string TokenPath = "/v2/oauth/token";

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsTheStoredTokenWithoutCallingIvao()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Token("unused")));
        using var client = CreateClient(handler);
        using var session = new IvaoSession(client, Tokens("access-1", expiresIn: TimeSpan.FromHours(1)));

        var token = await session.GetAccessTokenAsync();

        Assert.Equal("access-1", token);
        Assert.True(session.IsSignedIn);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RenewsAnExpiredTokenAndPersistsIt()
    {
        IvaoOAuthTokens? persisted = null;
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """{"access_token":"access-2","refresh_token":"refresh-2","expires_in":3600}"""));
        using var client = CreateClient(handler);
        using var session = new IvaoSession(
            client,
            Tokens("access-1", expiresIn: TimeSpan.FromMinutes(-5)),
            tokens =>
            {
                persisted = tokens;
                return Task.CompletedTask;
            });

        var token = await session.GetAccessTokenAsync();

        Assert.Equal("access-2", token);
        Assert.Equal("access-2", persisted?.AccessToken);
        Assert.Equal("refresh-2", persisted?.RefreshToken);
        Assert.Equal("access-2", session.Tokens?.AccessToken);
        Assert.Equal(TokenPath, Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task GetAccessTokenAsync_RenewsEvenAFreshTokenWhenIvaoJustRefusedIt()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Token("access-2")));
        using var client = CreateClient(handler);
        using var session = new IvaoSession(client, Tokens("access-1", expiresIn: TimeSpan.FromHours(1)));

        var token = await session.GetAccessTokenAsync(forceRefresh: true);

        Assert.Equal("access-2", token);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAccessTokenAsync_DropsTheSignInWhenIvaoRevokedTheGrant()
    {
        IvaoOAuthTokens? persisted = Tokens("access-1", expiresIn: TimeSpan.FromHours(1));
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Refresh token revoked"}"""));
        using var client = CreateClient(handler);
        using var session = new IvaoSession(
            client,
            Tokens("access-1", expiresIn: TimeSpan.FromMinutes(-5)),
            tokens =>
            {
                persisted = tokens;
                return Task.CompletedTask;
            });

        var token = await session.GetAccessTokenAsync();

        Assert.Null(token);
        Assert.False(session.IsSignedIn);
        Assert.Null(persisted);
    }

    [Fact]
    public async Task GetAccessTokenAsync_KeepsTheTokenWhenTheTokenEndpointFails()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            HttpStatusCode.ServiceUnavailable,
            """{"message":"maintenance"}"""));
        using var client = CreateClient(handler);
        using var session = new IvaoSession(client, Tokens("access-1", expiresIn: TimeSpan.FromMinutes(-5)));

        Assert.Equal("access-1", await session.GetAccessTokenAsync());
        Assert.True(session.IsSignedIn);
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNullWithoutASignIn()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Token("unused")));
        using var client = CreateClient(handler);
        using var session = new IvaoSession(client);

        Assert.Null(await session.GetAccessTokenAsync());
        Assert.False(session.IsSignedIn);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SignInAndSignOut_KeepThePersistedTokensInStep()
    {
        IvaoOAuthTokens? persisted = null;
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Token("unused")));
        using var client = CreateClient(handler);
        using var session = new IvaoSession(
            client,
            persist: tokens =>
            {
                persisted = tokens;
                return Task.CompletedTask;
            });

        await session.SignInAsync(Tokens("access-1", expiresIn: TimeSpan.FromHours(1)));

        Assert.True(session.IsSignedIn);
        Assert.Equal("access-1", persisted?.AccessToken);
        Assert.Equal("access-1", await session.GetAccessTokenAsync());

        await session.SignOutAsync();

        Assert.False(session.IsSignedIn);
        Assert.Null(persisted);
        Assert.Null(await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_KeepsAWorkingTokenWhenTheRefreshTokenIsMissing()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(Token("unused")));
        using var client = CreateClient(handler);
        using var session = new IvaoSession(
            client,
            new IvaoOAuthTokens("access-1", null, DateTimeOffset.UtcNow.AddMinutes(-5)));

        Assert.Equal("access-1", await session.GetAccessTokenAsync());
        Assert.Empty(handler.Requests);
    }

    private static IvaoOAuthTokens Tokens(string accessToken, TimeSpan expiresIn) =>
        new(accessToken, "refresh-1", DateTimeOffset.UtcNow.Add(expiresIn));

    private static string Token(string accessToken) =>
        $$"""{"access_token":"{{accessToken}}","refresh_token":"refresh-1","expires_in":3600}""";

    private static IvaoOAuthClient CreateClient(HttpMessageHandler handler) =>
        new(
            new IvaoOAuthOptions
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                TokenEndpoint = "https://test.local" + TokenPath
            },
            new HttpClient(handler));
}
