using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetGo.API.Data.Entities;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;
using Contracts = FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.API.Tests;

/// <summary>
/// Covers the route endpoints: CRUD, authentication, and - most importantly - the
/// data-isolation rule that a route always belongs to exactly the driver who created it.
/// Every "another driver's route" case below asserts 404, not 403: this project deliberately
/// never confirms that another driver's route id exists.
/// </summary>
public sealed class RouteEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private readonly FleetGoApiFactory _factory;

    public RouteEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_ReturnsUnauthorized_WithoutAToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.RoutesBase, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Forbidden_WhenCallerHasNoDriverProfile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = await FleetTestSupport.CreateAuthenticatedNonDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.RoutesBase, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyTheCallersOwnRoutes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, Guid driverA, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid routeA = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverA);
        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);

        using HttpResponseMessage response = await clientA.GetAsync($"{ApiRoutes.RoutesBase}?pageSize=100", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResponse<Contracts.RouteResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseRouteResponse, ct);

        Assert.NotNull(page);
        Assert.Contains(page.Items, r => r.Id == routeA);
        Assert.DoesNotContain(page.Items, r => r.Id == routeB);
    }

    [Fact]
    public async Task List_FiltersByStatusAndRouteDate()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        DateOnly targetDate = new(2026, 6, 15);
        Guid matching = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId, RouteStatus.InProgress, targetDate);
        await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId, RouteStatus.Planned, targetDate.AddDays(1));

        using HttpResponseMessage response = await client.GetAsync(
            $"{ApiRoutes.RoutesBase}?status=InProgress&routeDate=2026-06-15", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResponse<Contracts.RouteResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseRouteResponse, ct);

        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(matching, page.Items[0].Id);
    }

    [Fact]
    public async Task List_ValidationProblem_ForInvalidRouteDate()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.RoutesBase}?routeDate=not-a-date", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ValidationProblem_ForInvalidSort()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.RoutesBase}?sort=bogus", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_SortsByRouteDateDescending_WhenRequested()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid earlier = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId, routeDate: new DateOnly(2026, 1, 1));
        Guid later = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId, routeDate: new DateOnly(2026, 12, 31));

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.RoutesBase}?sort=-routeDate&pageSize=100", ct);
        PagedResponse<Contracts.RouteResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseRouteResponse, ct);

        Assert.NotNull(page);
        int laterIndex = page.Items.ToList().FindIndex(r => r.Id == later);
        int earlierIndex = page.Items.ToList().FindIndex(r => r.Id == earlier);
        Assert.True(laterIndex < earlierIndex);
    }

    [Fact]
    public async Task Create_ForcesOwnershipToTheCallersOwnDriverProfile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.RouteResponse route = await CreateRouteAsync(client, ct);

        Assert.Equal(driverId, route.DriverId);
    }

    [Fact]
    public async Task Create_Forbidden_WhenCallerHasNoDriverProfile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = await FleetTestSupport.CreateAuthenticatedNonDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateRouteRequest($"R-{Guid.NewGuid():N}"[..12], null, DateOnly.FromDateTime(DateTime.UtcNow));
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.RoutesBase, request, FleetGoJsonSerializerContext.Default.CreateRouteRequest, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForMissingRouteNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateRouteRequest(string.Empty, null, DateOnly.FromDateTime(DateTime.UtcNow));
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.RoutesBase, request, FleetGoJsonSerializerContext.Default.CreateRouteRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForNonexistentVehicleId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateRouteRequest(
            $"R-{Guid.NewGuid():N}"[..12], Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.RoutesBase, request, FleetGoJsonSerializerContext.Default.CreateRouteRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForVehicleAssignedToAnotherDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (HttpClient clientB, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid vehicleB = await CreateVehicleAssignedToDriverAsync(clientB, driverB, ct);

        var request = new Contracts.CreateRouteRequest(
            $"R-{Guid.NewGuid():N}"[..12], vehicleB, DateOnly.FromDateTime(DateTime.UtcNow));

        using HttpResponseMessage response = await clientA.PostAsJsonAsync(
            ApiRoutes.RoutesBase, request, FleetGoJsonSerializerContext.Default.CreateRouteRequest, ct);

        await AssertValidationProblemForFieldAsync(response, "vehicleId", ct);
    }

    [Fact]
    public async Task Update_ValidationProblem_ForVehicleAssignedToAnotherDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (HttpClient clientB, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.RouteResponse created = await CreateRouteAsync(clientA, ct);
        Guid vehicleB = await CreateVehicleAssignedToDriverAsync(clientB, driverB, ct);

        var update = new Contracts.UpdateRouteRequest(
            created.RouteNumber, vehicleB, created.RouteDate, Contracts.RouteStatus.Planned);

        using HttpResponseMessage response = await clientA.PutAsJsonAsync(
            $"{ApiRoutes.RoutesBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdateRouteRequest, ct);

        await AssertValidationProblemForFieldAsync(response, "vehicleId", ct);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForRouteNumberTooLong()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateRouteRequest(
            new string('R', 31), null, DateOnly.FromDateTime(DateTime.UtcNow));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.RoutesBase, request, FleetGoJsonSerializerContext.Default.CreateRouteRequest, ct);

        await AssertValidationProblemForFieldAsync(response, "routeNumber", ct);
    }

    [Fact]
    public async Task Update_ValidationProblem_ForRouteNumberTooLong()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.RouteResponse created = await CreateRouteAsync(client, ct);

        // 31 characters against RouteNumber's HasMaxLength(30). The update path has to reject
        // this exactly like the create path does, rather than letting the database raise it.
        var update = new Contracts.UpdateRouteRequest(
            new string('R', 31), null, created.RouteDate, Contracts.RouteStatus.Planned);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.RoutesBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdateRouteRequest, ct);

        await AssertValidationProblemForFieldAsync(response, "routeNumber", ct);
    }

    [Fact]
    public async Task Create_Conflict_ForDuplicateRouteNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        string routeNumber = $"DUP-{Guid.NewGuid():N}"[..12];
        await CreateRouteAsync(client, ct, routeNumber);

        var request = new Contracts.CreateRouteRequest(routeNumber, null, DateOnly.FromDateTime(DateTime.UtcNow));
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.RoutesBase, request, FleetGoJsonSerializerContext.Default.CreateRouteRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_ForAnotherDriversRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);

        using HttpResponseMessage response = await clientA.GetAsync($"{ApiRoutes.RoutesBase}/{routeB}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsTheCallersOwnRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.RouteResponse created = await CreateRouteAsync(client, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.RoutesBase}/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_ForAnotherDriversRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);

        var update = new Contracts.UpdateRouteRequest($"RN-{Guid.NewGuid():N}"[..12], null, DateOnly.FromDateTime(DateTime.UtcNow), Contracts.RouteStatus.Cancelled);
        using HttpResponseMessage response = await clientA.PutAsJsonAsync(
            $"{ApiRoutes.RoutesBase}/{routeB}", update, FleetGoJsonSerializerContext.Default.UpdateRouteRequest, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_Succeeds_ForTheCallersOwnRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.RouteResponse created = await CreateRouteAsync(client, ct);

        var update = new Contracts.UpdateRouteRequest(
            created.RouteNumber, null, created.RouteDate, Contracts.RouteStatus.InProgress);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.RoutesBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdateRouteRequest, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Contracts.RouteResponse? updated = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.RouteResponse, ct);
        Assert.NotNull(updated);
        Assert.Equal(Contracts.RouteStatus.InProgress, updated.Status);
    }

    [Fact]
    public async Task Update_Conflict_ForDuplicateRouteNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.RouteResponse first = await CreateRouteAsync(client, ct);
        Contracts.RouteResponse second = await CreateRouteAsync(client, ct);

        var update = new Contracts.UpdateRouteRequest(first.RouteNumber, null, second.RouteDate, second.Status);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.RoutesBase}/{second.Id}", update, FleetGoJsonSerializerContext.Default.UpdateRouteRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// Asserts the response is a 400 validation problem whose error list actually names
    /// <paramref name="field"/>. Checking the field - not just the status code - is what keeps
    /// one of these tests from passing because some unrelated rule rejected the request.
    /// </summary>
    private static async Task AssertValidationProblemForFieldAsync(
        HttpResponseMessage response, string field, CancellationToken ct)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        JsonElement errors = problem.RootElement.GetProperty("errors");

        Assert.True(
            errors.TryGetProperty(field, out _),
            $"Expected a validation error for '{field}', got: {errors.GetRawText()}");
    }

    private static async Task<Guid> CreateVehicleAssignedToDriverAsync(HttpClient driverClient, Guid driverId, CancellationToken ct)
    {
        var request = new Contracts.CreateVehicleRequest($"VEH-{Guid.NewGuid():N}"[..16], "Ford", "Transit", 2022, driverId);
        using HttpResponseMessage response = await driverClient.PostAsJsonAsync(
            ApiRoutes.VehiclesBase, request, FleetGoJsonSerializerContext.Default.CreateVehicleRequest, ct);

        response.EnsureSuccessStatusCode();

        Contracts.VehicleResponse? vehicle = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.VehicleResponse, ct);

        return vehicle?.Id ?? throw new InvalidOperationException("Create did not return a vehicle response.");
    }

        private static async Task<Contracts.RouteResponse> CreateRouteAsync(HttpClient client, CancellationToken ct, string? routeNumber = null)
    {
        var request = new Contracts.CreateRouteRequest(
            routeNumber ?? $"R-{Guid.NewGuid():N}"[..12], null, DateOnly.FromDateTime(DateTime.UtcNow));

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.RoutesBase, request, FleetGoJsonSerializerContext.Default.CreateRouteRequest, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(FleetGoJsonSerializerContext.Default.RouteResponse, ct)
            ?? throw new InvalidOperationException("Create did not return a route response.");
    }
}
