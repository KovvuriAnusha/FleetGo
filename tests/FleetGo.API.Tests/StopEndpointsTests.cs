using System.Net;
using System.Net.Http.Json;
using FleetGo.API.Data.Entities;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;
using Contracts = FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.API.Tests;

/// <summary>
/// Covers the stop endpoints: CRUD, authentication, and the data-isolation rule that a
/// stop's ownership is derived entirely through its route. A stop on another driver's route
/// is unreachable through any of these endpoints - not listable by routeId, not gettable,
/// not creatable against it, not editable - and every such attempt resolves to 404, exactly
/// as if the route did not exist at all.
/// </summary>
public sealed class StopEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private readonly FleetGoApiFactory _factory;

    public StopEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_ReturnsUnauthorized_WithoutAToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.StopsBase, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Forbidden_WhenCallerHasNoDriverProfile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = await FleetTestSupport.CreateAuthenticatedNonDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.StopsBase, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutRouteId_ReturnsOnlyStopsOnTheCallersOwnRoutes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, Guid driverA, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid routeA = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverA);
        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        Guid stopA = await FleetTestSupport.SeedStopAsync(_factory, ct, routeA, customer, 1);
        Guid stopB = await FleetTestSupport.SeedStopAsync(_factory, ct, routeB, customer, 1);

        using HttpResponseMessage response = await clientA.GetAsync($"{ApiRoutes.StopsBase}?pageSize=100", ct);
        PagedResponse<Contracts.StopResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseStopResponse, ct);

        Assert.NotNull(page);
        Assert.Contains(page.Items, s => s.Id == stopA);
        Assert.DoesNotContain(page.Items, s => s.Id == stopB);
    }

    [Fact]
    public async Task List_ScopedToRouteId_ReturnsNotFound_WhenTheRouteBelongsToAnotherDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);

        using HttpResponseMessage response = await clientA.GetAsync($"{ApiRoutes.StopsBase}?routeId={routeB}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByStatusAndCustomerSearch()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);

        string uniqueToken = Guid.NewGuid().ToString("N");
        Guid targetCustomer = await FleetTestSupport.SeedCustomerAsync(_factory, ct, $"Zephyr-{uniqueToken}");
        Guid otherCustomer = await FleetTestSupport.SeedCustomerAsync(_factory, ct, "Unrelated Co");

        Guid matching = await FleetTestSupport.SeedStopAsync(_factory, ct, route, targetCustomer, 1, StopStatus.Arrived);
        await FleetTestSupport.SeedStopAsync(_factory, ct, route, otherCustomer, 2, StopStatus.Pending);

        using HttpResponseMessage response = await client.GetAsync(
            $"{ApiRoutes.StopsBase}?routeId={route}&status=Arrived&search={uniqueToken}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResponse<Contracts.StopResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseStopResponse, ct);

        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(matching, page.Items[0].Id);
    }

    [Fact]
    public async Task List_SortsBySequenceDescending_WhenRequested()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        Guid first = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 1);
        Guid second = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 2);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.StopsBase}?routeId={route}&sort=-sequence", ct);
        PagedResponse<Contracts.StopResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseStopResponse, ct);

        Assert.NotNull(page);
        Assert.Equal(second, page.Items[0].Id);
        Assert.Equal(first, page.Items[1].Id);
    }

    [Fact]
    public async Task Create_NotFound_WhenTheRouteBelongsToAnotherDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        var request = new Contracts.CreateStopRequest(routeB, customer, 1, null);
        using HttpResponseMessage response = await clientA.PostAsJsonAsync(
            ApiRoutes.StopsBase, request, FleetGoJsonSerializerContext.Default.CreateStopRequest, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Succeeds_OnTheCallersOwnRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        Contracts.StopResponse stop = await CreateStopAsync(client, ct, route, customer, 1);

        Assert.Equal(route, stop.RouteId);
        Assert.Equal(Contracts.StopStatus.Pending, stop.Status);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForNonexistentCustomerId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);

        var request = new Contracts.CreateStopRequest(route, Guid.NewGuid(), 1, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.StopsBase, request, FleetGoJsonSerializerContext.Default.CreateStopRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForDeliveryNotesTooLong()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        var request = new Contracts.CreateStopRequest(route, customer, 1, new string('N', 501));
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.StopsBase, request, FleetGoJsonSerializerContext.Default.CreateStopRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Conflict_ForDuplicateSequenceOnTheSameRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        await CreateStopAsync(client, ct, route, customer, 1);

        var request = new Contracts.CreateStopRequest(route, customer, 1, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.StopsBase, request, FleetGoJsonSerializerContext.Default.CreateStopRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_ForAStopOnAnotherDriversRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid stopB = await FleetTestSupport.SeedStopAsync(_factory, ct, routeB, customer, 1);

        using HttpResponseMessage response = await clientA.GetAsync($"{ApiRoutes.StopsBase}/{stopB}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_ForAStopOnAnotherDriversRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid stopB = await FleetTestSupport.SeedStopAsync(_factory, ct, routeB, customer, 1);

        var update = new Contracts.UpdateStopRequest(customer, 1, Contracts.StopStatus.Completed, "notes");
        using HttpResponseMessage response = await clientA.PutAsJsonAsync(
            $"{ApiRoutes.StopsBase}/{stopB}", update, FleetGoJsonSerializerContext.Default.UpdateStopRequest, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_Succeeds_ForTheCallersOwnStop()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        Contracts.StopResponse created = await CreateStopAsync(client, ct, route, customer, 1);

        var update = new Contracts.UpdateStopRequest(customer, 1, Contracts.StopStatus.Completed, "Left at door");
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.StopsBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdateStopRequest, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Contracts.StopResponse? updated = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.StopResponse, ct);
        Assert.NotNull(updated);
        Assert.Equal(Contracts.StopStatus.Completed, updated.Status);
    }

    [Fact]
    public async Task Update_ValidationProblem_ForSequenceLessThanOne()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        Contracts.StopResponse created = await CreateStopAsync(client, ct, route, customer, 1);

        var update = new Contracts.UpdateStopRequest(customer, 0, Contracts.StopStatus.Pending, null);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.StopsBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdateStopRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Conflict_ForDuplicateSequenceOnTheSameRoute()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);

        await CreateStopAsync(client, ct, route, customer, 1);
        Contracts.StopResponse second = await CreateStopAsync(client, ct, route, customer, 2);

        var update = new Contracts.UpdateStopRequest(customer, 1, second.Status, second.DeliveryNotes);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.StopsBase}/{second.Id}", update, FleetGoJsonSerializerContext.Default.UpdateStopRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<Contracts.StopResponse> CreateStopAsync(
        HttpClient client, CancellationToken ct, Guid routeId, Guid customerId, int sequence)
    {
        var request = new Contracts.CreateStopRequest(routeId, customerId, sequence, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.StopsBase, request, FleetGoJsonSerializerContext.Default.CreateStopRequest, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(FleetGoJsonSerializerContext.Default.StopResponse, ct)
            ?? throw new InvalidOperationException("Create did not return a stop response.");
    }
}
