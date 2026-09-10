using System.Net;
using System.Net.Http.Json;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;

namespace FleetGo.API.Tests;

public sealed class SystemEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private readonly FleetGoApiFactory _factory;

    public SystemEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task GetSystemInfo_ReturnsOk_WithMetadataAboutTheRunningInstance()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.SystemInfo, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ApiInfoResponse? info = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.ApiInfoResponse,
            TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.Equal("FleetGo API", info.Name);
        Assert.Equal("Testing", info.Environment);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public async Task GetSystemInfo_SerialisesPropertiesAsCamelCase()
    {
        using HttpClient client = _factory.CreateClient();

        string json = await client.GetStringAsync(ApiRoutes.SystemInfo, TestContext.Current.CancellationToken);

        // The mobile client reads camelCase; a naming-policy regression here would
        // break every consumer silently.
        Assert.Contains("\"serverTimeUtc\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ServerTimeUtc\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails()
    {
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/v1/does-not-exist", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
