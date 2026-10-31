using System.Net;
using System.Net.Sockets;
using TheEye.Core.Api;

namespace TheEye.Core.Tests;

public sealed class IvaoOAuthCallbackTests
{
    private const string State = "state-1";

    [Fact]
    public void Parse_ReadsTheCodeWhenTheStateMatches()
    {
        var callback = IvaoOAuthCallbackListener.Parse(
            new Uri("http://localhost:48620/callback?code=the-code&state=state-1"),
            State);

        Assert.True(callback.IsSuccess);
        Assert.Equal("the-code", callback.Code);
        Assert.Null(callback.Error);
    }

    [Fact]
    public void Parse_RejectsARedirectFromAnotherRequest()
    {
        var callback = IvaoOAuthCallbackListener.Parse(
            new Uri("http://localhost:48620/callback?code=the-code&state=someone-else"),
            State);

        Assert.False(callback.IsSuccess);
        Assert.Null(callback.Code);
        Assert.Equal("state_mismatch", callback.Error);
    }

    [Fact]
    public void Parse_ReportsTheRefusalIvaoSent()
    {
        var callback = IvaoOAuthCallbackListener.Parse(
            new Uri("http://localhost:48620/callback?error=access_denied&error_description=The+user+cancelled&state=state-1"),
            State);

        Assert.False(callback.IsSuccess);
        Assert.Equal("access_denied", callback.Error);
        Assert.Equal("The user cancelled", callback.ErrorDescription);
    }

    [Fact]
    public void Parse_ReportsAMissingCode()
    {
        var callback = IvaoOAuthCallbackListener.Parse(
            new Uri("http://localhost:48620/callback?state=state-1"),
            State);

        Assert.False(callback.IsSuccess);
        Assert.Equal("missing_code", callback.Error);
    }

    [Fact]
    public void Parse_DecodesAnEscapedState()
    {
        // The state is base64url, but a redirect may still escape it.
        var callback = IvaoOAuthCallbackListener.Parse(
            new Uri("http://localhost:48620/callback?code=abc&state=a%2Db_c"),
            "a-b_c");

        Assert.True(callback.IsSuccess);
    }

    [Fact]
    public async Task WaitAsync_AnswersTheLoopbackRedirectWithTheCode()
    {
        var port = FindFreePort();
        var redirectUri = new Uri($"http://localhost:{port}/callback");
        var waiting = IvaoOAuthCallbackListener.WaitAsync(
            redirectUri,
            State,
            TimeSpan.FromSeconds(30));

        using var http = new HttpClient();
        var page = await http.GetAsync($"{redirectUri}?code=the-code&state={State}");
        var callback = await waiting;

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Signed in", await page.Content.ReadAsStringAsync());
        Assert.True(callback.IsSuccess);
        Assert.Equal("the-code", callback.Code);
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
