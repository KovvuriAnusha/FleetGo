using System.Net;
using System.Net.Http.Json;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;

namespace FleetGo.API.Tests;

public sealed class HealthEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private readonly FleetGoApiFactory _factory;

    public HealthEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData(ApiRoutes.Health)]
    [InlineData(ApiRoutes.HealthLive)]
    [InlineData(ApiRoutes.HealthReady)]
    public async Task HealthEndpoints_ReportHealthy_UsingTheSharedContract(string route)
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        HealthReportResponse? report = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.HealthReportResponse,
            TestContext.Current.CancellationToken);

        Assert.NotNull(report);
        Assert.Equal("Healthy", report.Status);
        Assert.Contains(report.Entries, entry => entry.Name == "self");
    }
}
