using System.Net;
using System.Net.Http.Json;
using FleetGo.API.Data.Entities;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;
using Contracts = FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.API.Tests;

/// <summary>
/// Covers the package endpoints: CRUD, authentication, and the two-level data-isolation
/// rule that a package's ownership is derived through its stop and, in turn, that stop's
/// route. A package on another driver's stop is unreachable through any of these endpoints,
/// and every such attempt resolves to 404.
/// </summary>
public sealed class PackageEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private readonly FleetGoApiFactory _factory;

    public PackageEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_ReturnsUnauthorized_WithoutAToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.PackagesBase, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_Forbidden_WhenCallerHasNoDriverProfile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = await FleetTestSupport.CreateAuthenticatedNonDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.PackagesBase, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutStopId_ReturnsOnlyPackagesOnTheCallersOwnStops()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, Guid driverA, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid routeA = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverA);
        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid stopA = await FleetTestSupport.SeedStopAsync(_factory, ct, routeA, customer, 1);
        Guid stopB = await FleetTestSupport.SeedStopAsync(_factory, ct, routeB, customer, 1);

        Guid packageA = await FleetTestSupport.SeedPackageAsync(_factory, ct, stopA);
        Guid packageB = await FleetTestSupport.SeedPackageAsync(_factory, ct, stopB);

        using HttpResponseMessage response = await clientA.GetAsync($"{ApiRoutes.PackagesBase}?pageSize=100", ct);
        PagedResponse<Contracts.PackageResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponsePackageResponse, ct);

        Assert.NotNull(page);
        Assert.Contains(page.Items, p => p.Id == packageA);
        Assert.DoesNotContain(page.Items, p => p.Id == packageB);
    }

    [Fact]
    public async Task List_ScopedToStopId_ReturnsNotFound_WhenTheStopBelongsToAnotherDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid stopB = await FleetTestSupport.SeedStopAsync(_factory, ct, routeB, customer, 1);

        using HttpResponseMessage response = await clientA.GetAsync($"{ApiRoutes.PackagesBase}?stopId={stopB}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByStatusAndTrackingSearch()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid stop = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 1);

        string uniqueToken = Guid.NewGuid().ToString("N");
        Guid matching = await FleetTestSupport.SeedPackageAsync(_factory, ct, stop, $"TRK-{uniqueToken}", PackageStatus.OutForDelivery);
        await FleetTestSupport.SeedPackageAsync(_factory, ct, stop, status: PackageStatus.Pending);

        using HttpResponseMessage response = await client.GetAsync(
            $"{ApiRoutes.PackagesBase}?stopId={stop}&status=OutForDelivery&search={uniqueToken}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResponse<Contracts.PackageResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponsePackageResponse, ct);

        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(matching, page.Items[0].Id);
    }

    [Fact]
    public async Task Create_NotFound_WhenTheStopBelongsToAnotherDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid stopB = await FleetTestSupport.SeedStopAsync(_factory, ct, routeB, customer, 1);

        var request = new Contracts.CreatePackageRequest(stopB, $"TRK-{Guid.NewGuid():N}", null);
        using HttpResponseMessage response = await clientA.PostAsJsonAsync(
            ApiRoutes.PackagesBase, request, FleetGoJsonSerializerContext.Default.CreatePackageRequest, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Succeeds_OnTheCallersOwnStop()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid stop = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 1);

        Contracts.PackageResponse package = await CreatePackageAsync(client, ct, stop);

        Assert.Equal(stop, package.StopId);
        Assert.Equal(Contracts.PackageStatus.Pending, package.Status);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForMissingTrackingNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid stop = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 1);

        var request = new Contracts.CreatePackageRequest(stop, string.Empty, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.PackagesBase, request, FleetGoJsonSerializerContext.Default.CreatePackageRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForTrackingNumberTooLong()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid stop = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 1);

        var request = new Contracts.CreatePackageRequest(stop, new string('T', 41), null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.PackagesBase, request, FleetGoJsonSerializerContext.Default.CreatePackageRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Conflict_ForDuplicateTrackingNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid stop = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 1);

        string trackingNumber = $"DUP-{Guid.NewGuid():N}";
        await CreatePackageAsync(client, ct, stop, trackingNumber);

        var request = new Contracts.CreatePackageRequest(stop, trackingNumber, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.PackagesBase, request, FleetGoJsonSerializerContext.Default.CreatePackageRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_ForAPackageOnAnotherDriversStop()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid stopB = await FleetTestSupport.SeedStopAsync(_factory, ct, routeB, customer, 1);
        Guid packageB = await FleetTestSupport.SeedPackageAsync(_factory, ct, stopB);

        using HttpResponseMessage response = await clientA.GetAsync($"{ApiRoutes.PackagesBase}/{packageB}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_ForAPackageOnAnotherDriversStop()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid routeB = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverB);
        Guid stopB = await FleetTestSupport.SeedStopAsync(_factory, ct, routeB, customer, 1);
        Guid packageB = await FleetTestSupport.SeedPackageAsync(_factory, ct, stopB);

        var update = new Contracts.UpdatePackageRequest($"TRK-{Guid.NewGuid():N}", null, Contracts.PackageStatus.Delivered);
        using HttpResponseMessage response = await clientA.PutAsJsonAsync(
            $"{ApiRoutes.PackagesBase}/{packageB}", update, FleetGoJsonSerializerContext.Default.UpdatePackageRequest, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_Succeeds_ForTheCallersOwnPackage()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid stop = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 1);

        Contracts.PackageResponse created = await CreatePackageAsync(client, ct, stop);

        var update = new Contracts.UpdatePackageRequest(created.TrackingNumber, "Fragile", Contracts.PackageStatus.Delivered);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.PackagesBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdatePackageRequest, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Contracts.PackageResponse? updated = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PackageResponse, ct);
        Assert.NotNull(updated);
        Assert.Equal(Contracts.PackageStatus.Delivered, updated.Status);
    }

    [Fact]
    public async Task Update_Conflict_ForDuplicateTrackingNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        Guid customer = await FleetTestSupport.SeedCustomerAsync(_factory, ct);
        Guid route = await FleetTestSupport.SeedRouteAsync(_factory, ct, driverId);
        Guid stop = await FleetTestSupport.SeedStopAsync(_factory, ct, route, customer, 1);

        Contracts.PackageResponse first = await CreatePackageAsync(client, ct, stop);
        Contracts.PackageResponse second = await CreatePackageAsync(client, ct, stop);

        var update = new Contracts.UpdatePackageRequest(first.TrackingNumber, second.Description, second.Status);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.PackagesBase}/{second.Id}", update, FleetGoJsonSerializerContext.Default.UpdatePackageRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<Contracts.PackageResponse> CreatePackageAsync(
        HttpClient client, CancellationToken ct, Guid stopId, string? trackingNumber = null)
    {
        var request = new Contracts.CreatePackageRequest(stopId, trackingNumber ?? $"TRK-{Guid.NewGuid():N}", null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.PackagesBase, request, FleetGoJsonSerializerContext.Default.CreatePackageRequest, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(FleetGoJsonSerializerContext.Default.PackageResponse, ct)
            ?? throw new InvalidOperationException("Create did not return a package response.");
    }
}
