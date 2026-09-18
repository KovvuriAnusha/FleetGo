using System.Net;
using System.Net.Http.Json;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;
using Contracts = FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.API.Tests;

/// <summary>
/// Covers the vehicle endpoints: CRUD, authentication, the "assign only to yourself"
/// authorization rule, pagination, status filtering, and validation. Vehicles are shared
/// fleet data - every authenticated caller can list/view every vehicle - so there is no
/// "another driver's vehicle is invisible" case the way there is for routes.
/// </summary>
public sealed class VehicleEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private readonly FleetGoApiFactory _factory;

    public VehicleEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_ReturnsUnauthorized_WithoutAToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.VehiclesBase, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsPagedVehicles_WithDefaultPaging()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        await CreateVehicleAsync(client, ct, registrationNumber: $"REG-{Guid.NewGuid():N}"[..16], driverId: null);
        await CreateVehicleAsync(client, ct, registrationNumber: $"REG-{Guid.NewGuid():N}"[..16], driverId: driverId);

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.VehiclesBase, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResponse<Contracts.VehicleResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseVehicleResponse, ct);

        Assert.NotNull(page);
        Assert.Equal(1, page.Page);
        Assert.Equal(20, page.PageSize);
        Assert.True(page.TotalCount >= 2);
        Assert.True(page.Items.Count >= 2);
    }

    [Fact]
    public async Task List_ValidationProblem_ForPageLessThanOne()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.VehiclesBase}?page=0", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ValidationProblem_ForPageSizeTooLarge()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.VehiclesBase}?pageSize=101", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_ValidationProblem_ForInvalidStatus()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.VehiclesBase}?status=NotAStatus", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse retired = await CreateVehicleAsync(client, ct, registrationNumber: $"REG-{Guid.NewGuid():N}"[..16]);
        await UpdateVehicleStatusAsync(client, ct, retired, Contracts.VehicleStatus.Retired);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.VehiclesBase}?status=Retired&pageSize=100", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResponse<Contracts.VehicleResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseVehicleResponse, ct);

        Assert.NotNull(page);
        Assert.Contains(page.Items, v => v.Id == retired.Id);
        Assert.All(page.Items, v => Assert.Equal(Contracts.VehicleStatus.Retired, v.Status));
    }

    [Fact]
    public async Task List_Pagination_IsEmptyBeyondTheLastPage_ButTotalCountStaysAccurate()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        for (int i = 0; i < 3; i++)
        {
            await CreateVehicleAsync(client, ct, registrationNumber: $"PG-{Guid.NewGuid():N}"[..16]);
        }

        using HttpResponseMessage firstPage = await client.GetAsync($"{ApiRoutes.VehiclesBase}?page=1&pageSize=2", ct);
        PagedResponse<Contracts.VehicleResponse>? first = await firstPage.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseVehicleResponse, ct);
        Assert.NotNull(first);
        Assert.Equal(2, first.Items.Count);

        using HttpResponseMessage farPage = await client.GetAsync($"{ApiRoutes.VehiclesBase}?page=999&pageSize=2", ct);
        PagedResponse<Contracts.VehicleResponse>? far = await farPage.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseVehicleResponse, ct);

        Assert.NotNull(far);
        Assert.Empty(far.Items);
        Assert.Equal(first.TotalCount, far.TotalCount);
    }

    [Fact]
    public async Task Create_Succeeds_AndDefaultsToActive()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse vehicle = await CreateVehicleAsync(client, ct, registrationNumber: $"NEW-{Guid.NewGuid():N}"[..16]);

        Assert.Equal(Contracts.VehicleStatus.Active, vehicle.Status);
        Assert.Null(vehicle.DriverId);
    }

    [Fact]
    public async Task Create_Succeeds_WhenAssigningToOwnDriverProfile()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, Guid driverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse vehicle = await CreateVehicleAsync(
            client, ct, registrationNumber: $"OWN-{Guid.NewGuid():N}"[..16], driverId: driverId);

        Assert.Equal(driverId, vehicle.DriverId);
    }

    [Fact]
    public async Task Create_Forbidden_WhenAssigningToAnotherDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid otherDriverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateVehicleRequest($"FRB-{Guid.NewGuid():N}"[..16], "Ford", "Transit", 2024, otherDriverId);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.VehiclesBase, request, FleetGoJsonSerializerContext.Default.CreateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForMissingRequiredFields()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateVehicleRequest(string.Empty, string.Empty, string.Empty, null, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.VehiclesBase, request, FleetGoJsonSerializerContext.Default.CreateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForRegistrationNumberTooLong()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateVehicleRequest(new string('X', 21), "Ford", "Transit", 2022, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.VehiclesBase, request, FleetGoJsonSerializerContext.Default.CreateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Conflict_ForDuplicateRegistrationNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        string registration = $"DUP-{Guid.NewGuid():N}"[..16];
        await CreateVehicleAsync(client, ct, registrationNumber: registration);

        var request = new Contracts.CreateVehicleRequest(registration, "Honda", "Civic", 2020, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.VehiclesBase, request, FleetGoJsonSerializerContext.Default.CreateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_ForAnUnknownId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.VehiclesBase}/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsTheVehicle_WhenItExists()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse created = await CreateVehicleAsync(client, ct, registrationNumber: $"GET-{Guid.NewGuid():N}"[..16]);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.VehiclesBase}/{created.Id}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Contracts.VehicleResponse? fetched = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.VehicleResponse, ct);

        Assert.NotNull(fetched);
        Assert.Equal(created.Id, fetched.Id);
    }

    [Fact]
    public async Task Update_Succeeds_AndPersistsChanges()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse created = await CreateVehicleAsync(client, ct, registrationNumber: $"UPD-{Guid.NewGuid():N}"[..16]);

        var update = new Contracts.UpdateVehicleRequest(
            created.RegistrationNumber, "Mercedes", "Sprinter", 2025, Contracts.VehicleStatus.InMaintenance, null);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.VehiclesBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Contracts.VehicleResponse? updated = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.VehicleResponse, ct);

        Assert.NotNull(updated);
        Assert.Equal("Mercedes", updated.Make);
        Assert.Equal(Contracts.VehicleStatus.InMaintenance, updated.Status);
    }

    [Fact]
    public async Task Update_NotFound_ForAnUnknownId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var update = new Contracts.UpdateVehicleRequest($"X-{Guid.NewGuid():N}"[..16], "Ford", "Focus", 2019, Contracts.VehicleStatus.Active, null);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.VehiclesBase}/{Guid.NewGuid()}", update, FleetGoJsonSerializerContext.Default.UpdateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ValidationProblem_ForInvalidStatus()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse created = await CreateVehicleAsync(client, ct, registrationNumber: $"INV-{Guid.NewGuid():N}"[..16]);

        using var content = System.Net.Http.Json.JsonContent.Create(new
        {
            registrationNumber = created.RegistrationNumber,
            make = "Ford",
            model = "Focus",
            year = 2019,
            status = "NotAStatus",
            driverId = (Guid?)null,
        });

        using HttpResponseMessage response = await client.PutAsync($"{ApiRoutes.VehiclesBase}/{created.Id}", content, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_Forbidden_WhenReassigningToAnotherDriver()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (_, Guid otherDriverId, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse created = await CreateVehicleAsync(client, ct, registrationNumber: $"REA-{Guid.NewGuid():N}"[..16]);

        var update = new Contracts.UpdateVehicleRequest(
            created.RegistrationNumber, created.Make, created.Model, created.Year, Contracts.VehicleStatus.Active, otherDriverId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.VehiclesBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Update_Forbidden_WhenVehicleIsAssignedToAnotherDriver()
    {
        // Regression: the assignment rule has to look at who holds the vehicle *now*, not
        // only at the driverId being sent. Clearing another driver's assignment was the first
        // half of a two-step takeover - unassign it, then claim the free vehicle.
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient clientA, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);
        (HttpClient clientB, Guid driverB, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse vehicleB = await CreateVehicleAsync(
            clientB, ct, registrationNumber: $"ASN-{Guid.NewGuid():N}"[..16], driverId: driverB);

        // Driver A sends driverId: null - not "assign to someone else", just "release it".
        var update = new Contracts.UpdateVehicleRequest(
            vehicleB.RegistrationNumber, vehicleB.Make, vehicleB.Model, vehicleB.Year,
            Contracts.VehicleStatus.Active, null);

        using HttpResponseMessage response = await clientA.PutAsJsonAsync(
            $"{ApiRoutes.VehiclesBase}/{vehicleB.Id}", update, FleetGoJsonSerializerContext.Default.UpdateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // The vehicle must still belong to driver B - a 403 that left the row modified would
        // be no better than a 200.
        using HttpResponseMessage reread = await clientB.GetAsync($"{ApiRoutes.VehiclesBase}/{vehicleB.Id}", ct);
        reread.EnsureSuccessStatusCode();

        Contracts.VehicleResponse? unchanged = await reread.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.VehicleResponse, ct);

        Assert.NotNull(unchanged);
        Assert.Equal(driverB, unchanged.DriverId);
    }

    [Fact]
    public async Task Update_Conflict_ForDuplicateRegistrationNumber()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.VehicleResponse first = await CreateVehicleAsync(client, ct, registrationNumber: $"C1-{Guid.NewGuid():N}"[..16]);
        Contracts.VehicleResponse second = await CreateVehicleAsync(client, ct, registrationNumber: $"C2-{Guid.NewGuid():N}"[..16]);

        var update = new Contracts.UpdateVehicleRequest(
            first.RegistrationNumber, second.Make, second.Model, second.Year, Contracts.VehicleStatus.Active, null);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.VehiclesBase}/{second.Id}", update, FleetGoJsonSerializerContext.Default.UpdateVehicleRequest, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<Contracts.VehicleResponse> CreateVehicleAsync(
        HttpClient client, CancellationToken ct, string registrationNumber, Guid? driverId = null)
    {
        var request = new Contracts.CreateVehicleRequest(registrationNumber, "Ford", "Transit", 2022, driverId);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.VehiclesBase, request, FleetGoJsonSerializerContext.Default.CreateVehicleRequest, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(FleetGoJsonSerializerContext.Default.VehicleResponse, ct)
            ?? throw new InvalidOperationException("Create did not return a vehicle response.");
    }

    private static async Task UpdateVehicleStatusAsync(
        HttpClient client, CancellationToken ct, Contracts.VehicleResponse vehicle, Contracts.VehicleStatus status)
    {
        var update = new Contracts.UpdateVehicleRequest(
            vehicle.RegistrationNumber, vehicle.Make, vehicle.Model, vehicle.Year, status, vehicle.DriverId);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.VehiclesBase}/{vehicle.Id}", update, FleetGoJsonSerializerContext.Default.UpdateVehicleRequest, ct);

        response.EnsureSuccessStatusCode();
    }
}
