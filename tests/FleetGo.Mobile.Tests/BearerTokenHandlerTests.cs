using System.Net;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public sealed class BearerTokenHandlerTests
{
    private sealed class FakeAccessTokenProvider : IAccessTokenProvider
    {
        private readonly string? _token;

        public FakeAccessTokenProvider(string? token) => _token = token;

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_token);
    }

    [Fact]
    public async Task AttachesTheBearerToken_WhenOneIsAvailable()
    {
        StubHttpMessageHandler inner = StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, "{}");
        BearerTokenHandler handler = new(new FakeAccessTokenProvider("my-access-token")) { InnerHandler = inner };
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost:5266") };

        await client.GetAsync("/api/v1/auth/me", TestContext.Current.CancellationToken);

        Assert.NotNull(inner.LastRequest?.Headers.Authorization);
        Assert.Equal("Bearer", inner.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("my-access-token", inner.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task DoesNotAttachAHeader_WhenNoTokenIsAvailable()
    {
        StubHttpMessageHandler inner = StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, "{}");
        BearerTokenHandler handler = new(new FakeAccessTokenProvider(null)) { InnerHandler = inner };
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost:5266") };

        await client.GetAsync("/api/v1/system/info", TestContext.Current.CancellationToken);

        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }

    [Fact]
    public async Task DoesNotAttachAHeader_WhenTheRequestOptsOutViaSkipAuthentication()
    {
        StubHttpMessageHandler inner = StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, "{}");
        BearerTokenHandler handler = new(new FakeAccessTokenProvider("my-access-token")) { InnerHandler = inner };
        using HttpClient client = new(handler) { BaseAddress = new Uri("http://localhost:5266") };

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/login");
        request.Options.Set(FleetGoHttpRequestOptions.SkipAuthentication, true);

        await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Null(inner.LastRequest?.Headers.Authorization);
    }
}

