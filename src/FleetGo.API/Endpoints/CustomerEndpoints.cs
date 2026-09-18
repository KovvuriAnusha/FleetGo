using FleetGo.API.Data;
using FleetGo.API.Data.Entities;
using FleetGo.API.Pagination;
using FleetGo.API.Validation;
using FleetGo.Shared;
using FleetGo.Shared.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Contracts = FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.API.Endpoints;

/// <summary>
/// Customer endpoints. Customers are shared reference data - the same customer can appear
/// on routes for different drivers over time, so there is no ownership to enforce here: any
/// authenticated caller can list, view, add, or edit one.
/// </summary>
internal static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup(ApiRoutes.CustomersBase)
            .WithTags("Fleet")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListCustomers")
            .WithSummary("Lists customers.")
            .WithDescription("Shared reference data - every authenticated caller sees every customer. Filter by name; sorted by name.");

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetCustomer")
            .WithSummary("Gets a single customer by id.");

        group.MapPost("/", CreateAsync)
            .WithName("CreateCustomer")
            .WithSummary("Adds a customer.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateCustomer")
            .WithSummary("Replaces the editable fields of an existing customer.");

        return endpoints;
    }

    private static async Task<Results<Ok<PagedResponse<Contracts.CustomerResponse>>, ValidationProblem>> ListAsync(
        int? page,
        int? pageSize,
        string? search,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        if (!PageRequest.TryCreate(page, pageSize, out PageRequest? pageRequest, out string? pageError))
        {
            var errors = new ValidationErrors();
            errors.Add("page", pageError);
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        IQueryable<Customer> query = db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Name.Contains(search));
        }

        var projected = query.OrderBy(c => c.Name).Select(ToResponseProjection);

        var paged = await projected.ToPagedResponseAsync(pageRequest!, cancellationToken);

        return TypedResults.Ok(paged);
    }

    private static async Task<Results<Ok<Contracts.CustomerResponse>, NotFound>> GetByIdAsync(
        Guid id,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Contracts.CustomerResponse? customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(ToResponseProjection)
            .FirstOrDefaultAsync(cancellationToken);

        return customer is null ? TypedResults.NotFound() : TypedResults.Ok(customer);
    }

    private static async Task<Results<Created<Contracts.CustomerResponse>, ValidationProblem>> CreateAsync(
        [FromBody] Contracts.CreateCustomerRequest request,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = ValidateFields(
            request.Name, request.PhoneNumber, request.Email, request.AddressLine1, request.AddressLine2,
            request.City, request.State, request.PostalCode, request.Country);

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        DateTime now = DateTime.UtcNow;
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            Email = request.Email,
            AddressLine1 = request.AddressLine1,
            AddressLine2 = request.AddressLine2,
            City = request.City,
            State = request.State,
            PostalCode = request.PostalCode,
            Country = request.Country,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"{ApiRoutes.CustomersBase}/{customer.Id}", ToResponse(customer));
    }

    private static async Task<Results<Ok<Contracts.CustomerResponse>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id,
        [FromBody] Contracts.UpdateCustomerRequest request,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Customer? customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (customer is null)
        {
            return TypedResults.NotFound();
        }

        var errors = ValidateFields(
            request.Name, request.PhoneNumber, request.Email, request.AddressLine1, request.AddressLine2,
            request.City, request.State, request.PostalCode, request.Country);

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        customer.Name = request.Name;
        customer.PhoneNumber = request.PhoneNumber;
        customer.Email = request.Email;
        customer.AddressLine1 = request.AddressLine1;
        customer.AddressLine2 = request.AddressLine2;
        customer.City = request.City;
        customer.State = request.State;
        customer.PostalCode = request.PostalCode;
        customer.Country = request.Country;
        customer.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(ToResponse(customer));
    }

    private static ValidationErrors ValidateFields(
        string name, string? phoneNumber, string? email, string addressLine1, string? addressLine2,
        string city, string? state, string postalCode, string? country)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "name is required.");
        }

        errors.AddIfTooLong(name, 200, "name");
        errors.AddIfTooLong(phoneNumber, 30, "phoneNumber");
        errors.AddIfTooLong(email, 256, "email");

        if (string.IsNullOrWhiteSpace(addressLine1))
        {
            errors.Add("addressLine1", "addressLine1 is required.");
        }

        errors.AddIfTooLong(addressLine1, 200, "addressLine1");
        errors.AddIfTooLong(addressLine2, 200, "addressLine2");

        if (string.IsNullOrWhiteSpace(city))
        {
            errors.Add("city", "city is required.");
        }

        errors.AddIfTooLong(city, 100, "city");
        errors.AddIfTooLong(state, 100, "state");

        if (string.IsNullOrWhiteSpace(postalCode))
        {
            errors.Add("postalCode", "postalCode is required.");
        }

        errors.AddIfTooLong(postalCode, 20, "postalCode");
        errors.AddIfTooLong(country, 60, "country");

        return errors;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Customer, Contracts.CustomerResponse>> ToResponseProjection =
        c => new Contracts.CustomerResponse(
            c.Id, c.Name, c.PhoneNumber, c.Email, c.AddressLine1, c.AddressLine2,
            c.City, c.State, c.PostalCode, c.Country, c.CreatedAtUtc, c.UpdatedAtUtc);

    private static Contracts.CustomerResponse ToResponse(Customer c) => new(
        c.Id, c.Name, c.PhoneNumber, c.Email, c.AddressLine1, c.AddressLine2,
        c.City, c.State, c.PostalCode, c.Country, c.CreatedAtUtc, c.UpdatedAtUtc);
}
