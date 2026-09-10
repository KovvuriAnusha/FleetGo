using System.Net;

namespace FleetGo.Mobile.Tests;

/// <summary>
/// Test double for the bottom of the HttpClient stack. Substituting the handler
/// rather than the client keeps the real HttpClient behaviour (headers, base
/// address resolution, content negotiation) in the test.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    /// <summary>The last request the client sent, so tests can assert on the URL.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    public static StubHttpMessageHandler RespondWith(HttpStatusCode statusCode, string json) =>
        new(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(_respond(request));
    }
}
