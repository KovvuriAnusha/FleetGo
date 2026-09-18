using System.Net;
using System.Net.Http.Json;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Serialization;
using Contracts = FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.API.Tests;

/// <summary>
/// Covers the customer endpoints: CRUD, authentication, pagination, name search, and
/// validation. Customers are shared reference data, so unlike vehicles/routes there is no
/// ownership rule to test here - any authenticated caller can manage any customer.
/// </summary>
public sealed class CustomerEndpointsTests : IClassFixture<FleetGoApiFactory>
{
    private readonly FleetGoApiFactory _factory;

    public CustomerEndpointsTests(FleetGoApiFactory factory) => _factory = factory;

    [Fact]
    public async Task List_ReturnsUnauthorized_WithoutAToken()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using HttpClient client = _factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(ApiRoutes.CustomersBase, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsPagedCustomers()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        await CreateCustomerAsync(client, ct, $"Acme {Guid.NewGuid():N}");
        await CreateCustomerAsync(client, ct, $"Acme {Guid.NewGuid():N}");

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.CustomersBase}?pageSize=100", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResponse<Contracts.CustomerResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseCustomerResponse, ct);

        Assert.NotNull(page);
        Assert.True(page.TotalCount >= 2);
    }

    [Fact]
    public async Task List_FiltersByNameSearch()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        string uniqueToken = Guid.NewGuid().ToString("N");
        Contracts.CustomerResponse match = await CreateCustomerAsync(client, ct, $"Zephyr-{uniqueToken}-Logistics");
        await CreateCustomerAsync(client, ct, "Totally Unrelated Co");

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.CustomersBase}?search={uniqueToken}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PagedResponse<Contracts.CustomerResponse>? page = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.PagedResponseCustomerResponse, ct);

        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(match.Id, page.Items[0].Id);
    }

    [Fact]
    public async Task List_ValidationProblem_ForInvalidPaging()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.CustomersBase}?pageSize=0", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_Succeeds()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.CustomerResponse customer = await CreateCustomerAsync(client, ct, $"New Customer {Guid.NewGuid():N}");

        Assert.NotEqual(Guid.Empty, customer.Id);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForMissingRequiredFields()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateCustomerRequest(string.Empty, null, null, string.Empty, null, string.Empty, null, string.Empty, null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.CustomersBase, request, FleetGoJsonSerializerContext.Default.CreateCustomerRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidationProblem_ForNameTooLong()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var request = new Contracts.CreateCustomerRequest(
            new string('N', 201), null, null, "1 Test Street", null, "Testville", null, "00000", null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.CustomersBase, request, FleetGoJsonSerializerContext.Default.CreateCustomerRequest, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_ForAnUnknownId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        using HttpResponseMessage response = await client.GetAsync($"{ApiRoutes.CustomersBase}/{Guid.NewGuid()}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_Succeeds_AndPersistsChanges()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        Contracts.CustomerResponse created = await CreateCustomerAsync(client, ct, $"Old Name {Guid.NewGuid():N}");

        var update = new Contracts.UpdateCustomerRequest(
            "Updated Name", created.PhoneNumber, created.Email, created.AddressLine1, created.AddressLine2,
            created.City, created.State, created.PostalCode, created.Country);

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.CustomersBase}/{created.Id}", update, FleetGoJsonSerializerContext.Default.UpdateCustomerRequest, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Contracts.CustomerResponse? updated = await response.Content.ReadFromJsonAsync(
            FleetGoJsonSerializerContext.Default.CustomerResponse, ct);

        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated.Name);
    }

    [Fact]
    public async Task Update_NotFound_ForAnUnknownId()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        (HttpClient client, _, _) = await FleetTestSupport.CreateAuthenticatedDriverClientAsync(_factory, ct);

        var update = new Contracts.UpdateCustomerRequest("Name", null, null, "1 St", null, "City", null, "00000", null);
        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"{ApiRoutes.CustomersBase}/{Guid.NewGuid()}", update, FleetGoJsonSerializerContext.Default.UpdateCustomerRequest, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<Contracts.CustomerResponse> CreateCustomerAsync(HttpClient client, CancellationToken ct, string name)
    {
        var request = new Contracts.CreateCustomerRequest(name, null, null, "1 Test Street", null, "Testville", null, "00000", null);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            ApiRoutes.CustomersBase, request, FleetGoJsonSerializerContext.Default.CreateCustomerRequest, ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync(FleetGoJsonSerializerContext.Default.CustomerResponse, ct)
            ?? throw new InvalidOperationException("Create did not return a customer response.");
    }
}
