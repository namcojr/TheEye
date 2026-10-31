using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace TheEye.Core.Tests;

/// <summary>
/// Answers requests with a canned response and records the paths that were
/// asked for, so the API client can be tested without a network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        _responseFactory = responseFactory;
    }

    public ConcurrentQueue<string> Requests { get; } = new();

    public ConcurrentQueue<string> ApiKeys { get; } = new();

    public ConcurrentQueue<string> Methods { get; } = new();

    public ConcurrentQueue<string> Bodies { get; } = new();

    public ConcurrentQueue<string> Authorizations { get; } = new();

    public static HttpResponseMessage Json(string json) => Json(HttpStatusCode.OK, json);

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Enqueue(request.RequestUri!.AbsolutePath);
        ApiKeys.Enqueue(request.Headers.TryGetValues("apiKey", out var values)
            ? string.Join(',', values)
            : string.Empty);
        Methods.Enqueue(request.Method.Method);
        Authorizations.Enqueue(request.Headers.Authorization?.ToString() ?? string.Empty);
        Bodies.Enqueue(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responseFactory(request);
    }
}
