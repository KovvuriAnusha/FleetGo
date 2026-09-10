using System.Net;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public sealed class FleetGoApiClientTests
{
    private static readonly Uri BaseAddress = new("http://localhost:5266");

    [Fact]
    public async Task GetApiInfoAsync_ReadsCamelCasePayload_IntoTheSharedContract()
    {
        const string json = """
            {
              "name": "FleetGo API",
              "version": "0.1.0",
              "environment": "Development",
              "serverTimeUtc": "2026-09-09T08:30:00+00:00"
            }
            """;

        StubHttpMessageHandler handler = StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, json);
        FleetGoApiClient client = CreateClient(handler);

        ApiInfoResponse info = await client.GetApiInfoAsync(TestContext.Current.CancellationToken);

        Assert.Equal("FleetGo API", info.Name);
        Assert.Equal("Development", info.Environment);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 8, 30, 0, TimeSpan.Zero), info.ServerTimeUtc);
        Assert.Equal(ApiRoutes.SystemInfo, handler.LastRequest?.RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task GetApiInfoAsync_Throws_WhenTheApiReturnsAnError()
    {
        StubHttpMessageHandler handler = StubHttpMessageHandler.RespondWith(HttpStatusCode.InternalServerError, "{}");
        FleetGoApiClient client = CreateClient(handler);

        FleetGoApiException exception = await Assert.ThrowsAsync<FleetGoApiException>(
            () => client.GetApiInfoAsync(TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
    }

    [Fact]
    public async Task GetHealthAsync_ReadsTheReport_EvenWhenTheApiIsUnhealthy()
    {
        // A failing health check answers 503 with a perfectly valid report. The
        // client must surface that report rather than treat it as a failure.
        const string json = """
            {
              "status": "Unhealthy",
              "totalDurationMs": 12.5,
              "entries": [
                { "name": "self", "status": "Unhealthy", "durationMs": 12.1, "description": "database offline" }
              ]
            }
            """;

        FleetGoApiClient client = CreateClient(StubHttpMessageHandler.RespondWith(HttpStatusCode.ServiceUnavailable, json));

        HealthReportResponse report = await client.GetHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Unhealthy", report.Status);
        HealthCheckEntryResponse entry = Assert.Single(report.Entries);
        Assert.Equal("self", entry.Name);
        Assert.Equal("database offline", entry.Description);
    }

    [Fact]
    public async Task GetApiInfoAsync_Throws_WhenTheBodyIsNotTheExpectedShape()
    {
        FleetGoApiClient client = CreateClient(StubHttpMessageHandler.RespondWith(HttpStatusCode.OK, "not json at all"));

        await Assert.ThrowsAsync<FleetGoApiException>(
            () => client.GetApiInfoAsync(TestContext.Current.CancellationToken));
    }

    private static FleetGoApiClient CreateClient(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = BaseAddress });
}
