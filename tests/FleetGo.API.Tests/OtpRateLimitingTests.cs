using System.Net;
using System.Net.Http.Json;
using FleetGo.Shared;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FleetGo.API.Tests;

/// <summary>
/// Rate limiting on the OTP endpoints, in its own class rather than folded into
/// <see cref="OtpEndpointsTests"/>: <see cref="FleetGoApiFactory"/> deliberately configures a
/// very generous limit by default (see its own comments) so that class's many functional
/// tests, which all share one host and therefore one limiter partition, never trip each
/// other's counters. Each test here builds its own host via <c>WithWebHostBuilder</c> with a
/// small, deliberately-triggerable limit layered on top, so it cannot affect - or be affected
/// by - any other test.
/// </summary>
public sealed class OtpRateLimitingTests : IClassFixture<FleetGoApiFactory>
{
    private readonly FleetGoApiFactory _factory;

    public OtpRateLimitingTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Request_ReturnsTooManyRequests_OnceThePerIpLimitIsExceeded()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using WebApplicationFactory<Program> strictFactory = WithStrictLimit(
            "Otp:RequestRateLimit:PermitLimit", "Otp:RequestRateLimit:WindowSeconds");
        using HttpClient client = strictFactory.CreateClient();

        // The limiter runs in the pipeline before the endpoint touches the database, so the
        // email does not need to belong to a real account for this to prove the limiter works.
        string email = $"rate-limit-{Guid.NewGuid():N}@example.com";

        using HttpResponseMessage first = await PostRequestOtpAsync(client, email, ct);
        using HttpResponseMessage second = await PostRequestOtpAsync(client, email, ct);
        using HttpResponseMessage third = await PostRequestOtpAsync(client, email, ct);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task Verify_ReturnsTooManyRequests_OnceThePerIpLimitIsExceeded()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;

        using WebApplicationFactory<Program> strictFactory = WithStrictLimit(
            "Otp:VerifyRateLimit:PermitLimit", "Otp:VerifyRateLimit:WindowSeconds");
        using HttpClient client = strictFactory.CreateClient();

        string email = $"rate-limit-{Guid.NewGuid():N}@example.com";

        using HttpResponseMessage first = await PostVerifyOtpAsync(client, email, "000000", ct);
        using HttpResponseMessage second = await PostVerifyOtpAsync(client, email, "000000", ct);
        using HttpResponseMessage third = await PostVerifyOtpAsync(client, email, "000000", ct);

        // The first two are still "unauthorized" (no such account/code) - the limiter and
        // the business-logic result are independent of one another.
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    private WebApplicationFactory<Program> WithStrictLimit(string permitLimitKey, string windowSecondsKey) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [permitLimitKey] = "2",
                    [windowSecondsKey] = "60",
                })));

    private static Task<HttpResponseMessage> PostRequestOtpAsync(HttpClient client, string email, CancellationToken ct) =>
        client.PostAsJsonAsync(
            ApiRoutes.AuthOtpRequest,
            new RequestOtpRequest(email),
            FleetGoJsonSerializerContext.Default.RequestOtpRequest,
            ct);

    private static Task<HttpResponseMessage> PostVerifyOtpAsync(HttpClient client, string email, string code, CancellationToken ct) =>
        client.PostAsJsonAsync(
            ApiRoutes.AuthOtpVerify,
            new VerifyOtpRequest(email, code),
            FleetGoJsonSerializerContext.Default.VerifyOtpRequest,
            ct);
}
