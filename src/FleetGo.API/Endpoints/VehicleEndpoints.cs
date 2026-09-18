using System.Security.Claims;
using FleetGo.API.Auth;
using FleetGo.API.Data;
using FleetGo.API.Data.Entities;
using FleetGo.API.Filtering;
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
/// Fleet vehicle endpoints. Vehicles are shared fleet data, not owned by any one driver -
/// any authenticated caller can list, view, add, or edit one - but a driver can only assign
/// a vehicle to <em>themselves</em>, never to another driver, so an id in the request body
/// can't be used to reassign someone else's equipment.
/// </summary>
internal static class VehicleEndpoints
{
    public static IEndpointRouteBuilder MapVehicleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup(ApiRoutes.VehiclesBase)
            .WithTags("Fleet")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListVehicles")
            .WithSummary("Lists fleet vehicles.")
            .WithDescription("Shared fleet data - every authenticated caller sees every vehicle. Filter by status; sorted by registration number.");

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetVehicle")
            .WithSummary("Gets a single vehicle by id.");

        group.MapPost("/", CreateAsync)
            .WithName("CreateVehicle")
            .WithSummary("Adds a vehicle to the fleet.")
            .WithDescription("The new vehicle always starts Active. driverId, if given, must be null or the caller's own driver id.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateVehicle")
            .WithSummary("Replaces the editable fields of an existing vehicle.")
            .WithDescription("A vehicle already assigned to another driver cannot be edited at all; driverId, if given, must be null or the caller's own driver id - a vehicle can't be reassigned to someone else this way.");

        return endpoints;
    }

    private static async Task<Results<Ok<PagedResponse<Contracts.VehicleResponse>>, ValidationProblem>> ListAsync(
        int? page,
        int? pageSize,
        string? status,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        if (!PageRequest.TryCreate(page, pageSize, out PageRequest? pageRequest, out string? pageError))
        {
            errors.Add("page", pageError);
        }

        if (!QueryParsing.TryParseEnum(status, out VehicleStatus? statusFilter, out string? statusError))
        {
            errors.Add("status", statusError);
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        IQueryable<Vehicle> query = db.Vehicles.AsNoTracking();

        if (statusFilter is not null)
        {
            query = query.Where(v => v.Status == statusFilter.Value);
        }

        var projected = query
            .OrderBy(v => v.RegistrationNumber)
            .Select(v => new
            {
                v.Id,
                v.RegistrationNumber,
                v.Make,
                v.Model,
                v.Year,
                v.Status,
                v.DriverId,
                DriverCode = v.Driver != null ? v.Driver.DriverCode : null,
                v.CreatedAtUtc,
                v.UpdatedAtUtc,
            });

        var paged = await projected.ToPagedResponseAsync(pageRequest!, cancellationToken);

        var response = new PagedResponse<Contracts.VehicleResponse>(
            paged.Items.Select(v => new Contracts.VehicleResponse(
                v.Id, v.RegistrationNumber, v.Make, v.Model, v.Year,
                MapStatus(v.Status), v.DriverId, v.DriverCode, v.CreatedAtUtc, v.UpdatedAtUtc)).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<Contracts.VehicleResponse>, NotFound>> GetByIdAsync(
        Guid id,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.AsNoTracking()
            .Where(v => v.Id == id)
            .Select(v => new
            {
                v.Id,
                v.RegistrationNumber,
                v.Make,
                v.Model,
                v.Year,
                v.Status,
                v.DriverId,
                DriverCode = v.Driver != null ? v.Driver.DriverCode : null,
                v.CreatedAtUtc,
                v.UpdatedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (vehicle is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new Contracts.VehicleResponse(
            vehicle.Id, vehicle.RegistrationNumber, vehicle.Make, vehicle.Model, vehicle.Year,
            MapStatus(vehicle.Status), vehicle.DriverId, vehicle.DriverCode, vehicle.CreatedAtUtc, vehicle.UpdatedAtUtc));
    }

    private static async Task<Results<Created<Contracts.VehicleResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        [FromBody] Contracts.CreateVehicleRequest request,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.RegistrationNumber))
        {
            errors.Add("registrationNumber", "registrationNumber is required.");
        }

        errors.AddIfTooLong(request.RegistrationNumber, 20, "registrationNumber");

        if (string.IsNullOrWhiteSpace(request.Make))
        {
            errors.Add("make", "make is required.");
        }

        errors.AddIfTooLong(request.Make, 50, "make");

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            errors.Add("model", "model is required.");
        }

        errors.AddIfTooLong(request.Model, 50, "model");

        if (request.Year is < 1900 or > 2100)
        {
            errors.Add("year", "year must be between 1900 and 2100.");
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        Guid? callerDriverId = user.GetDriverId();

        if (request.DriverId is not null && request.DriverId != callerDriverId)
        {
            return FleetProblems.Forbidden("You may only assign a vehicle to your own driver profile.");
        }

        bool registrationInUse = await db.Vehicles.AsNoTracking()
            .AnyAsync(v => v.RegistrationNumber == request.RegistrationNumber, cancellationToken);

        if (registrationInUse)
        {
            return FleetProblems.Conflict("A vehicle with this registration number already exists.");
        }

        DateTime now = DateTime.UtcNow;
        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            RegistrationNumber = request.RegistrationNumber,
            Make = request.Make,
            Model = request.Model,
            Year = request.Year,
            Status = VehicleStatus.Active,
            DriverId = request.DriverId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(cancellationToken);

        string? driverCode = await LookupDriverCodeAsync(db, vehicle.DriverId, cancellationToken);

        var response = new Contracts.VehicleResponse(
            vehicle.Id, vehicle.RegistrationNumber, vehicle.Make, vehicle.Model, vehicle.Year,
            MapStatus(vehicle.Status), vehicle.DriverId, driverCode, vehicle.CreatedAtUtc, vehicle.UpdatedAtUtc);

        return TypedResults.Created($"{ApiRoutes.VehiclesBase}/{vehicle.Id}", response);
    }

    private static async Task<Results<Ok<Contracts.VehicleResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        [FromBody] Contracts.UpdateVehicleRequest request,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Vehicle? vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

        if (vehicle is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.RegistrationNumber))
        {
            errors.Add("registrationNumber", "registrationNumber is required.");
        }

        errors.AddIfTooLong(request.RegistrationNumber, 20, "registrationNumber");

        if (string.IsNullOrWhiteSpace(request.Make))
        {
            errors.Add("make", "make is required.");
        }

        errors.AddIfTooLong(request.Make, 50, "make");

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            errors.Add("model", "model is required.");
        }

        errors.AddIfTooLong(request.Model, 50, "model");

        if (request.Year is < 1900 or > 2100)
        {
            errors.Add("year", "year must be between 1900 and 2100.");
        }

        if (!Enum.IsDefined(request.Status))
        {
            errors.Add("status", $"'{request.Status}' is not a valid vehicle status.");
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        Guid? callerDriverId = user.GetDriverId();

        // The vehicle's *current* assignment is as much a part of this rule as the requested
        // one. Checking only the incoming driverId leaves a two-step reassignment open: send
        // driverId: null to clear another driver's assignment (that passes the check below,
        // because null is never "another driver"), then claim the now-unassigned vehicle in a
        // second call. Whoever holds the vehicle now is the only one who may edit it.
        if (vehicle.DriverId is not null && vehicle.DriverId != callerDriverId)
        {
            return FleetProblems.Forbidden("This vehicle is assigned to another driver.");
        }

        if (request.DriverId is not null && request.DriverId != callerDriverId)
        {
            return FleetProblems.Forbidden("You may only assign a vehicle to your own driver profile.");
        }

        bool registrationInUse = await db.Vehicles.AsNoTracking()
            .AnyAsync(v => v.Id != id && v.RegistrationNumber == request.RegistrationNumber, cancellationToken);

        if (registrationInUse)
        {
            return FleetProblems.Conflict("A vehicle with this registration number already exists.");
        }

        vehicle.RegistrationNumber = request.RegistrationNumber;
        vehicle.Make = request.Make;
        vehicle.Model = request.Model;
        vehicle.Year = request.Year;
        vehicle.Status = MapStatus(request.Status);
        vehicle.DriverId = request.DriverId;
        vehicle.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        string? driverCode = await LookupDriverCodeAsync(db, vehicle.DriverId, cancellationToken);

        var response = new Contracts.VehicleResponse(
            vehicle.Id, vehicle.RegistrationNumber, vehicle.Make, vehicle.Model, vehicle.Year,
            MapStatus(vehicle.Status), vehicle.DriverId, driverCode, vehicle.CreatedAtUtc, vehicle.UpdatedAtUtc);

        return TypedResults.Ok(response);
    }

    private static Task<string?> LookupDriverCodeAsync(FleetGoDbContext db, Guid? driverId, CancellationToken cancellationToken) =>
        driverId is null
            ? Task.FromResult<string?>(null)
            : db.Drivers.AsNoTracking().Where(d => d.Id == driverId).Select(d => d.DriverCode).FirstOrDefaultAsync(cancellationToken);

    private static Contracts.VehicleStatus MapStatus(VehicleStatus status) => status switch
    {
        VehicleStatus.Active => Contracts.VehicleStatus.Active,
        VehicleStatus.InMaintenance => Contracts.VehicleStatus.InMaintenance,
        VehicleStatus.Retired => Contracts.VehicleStatus.Retired,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown vehicle status."),
    };

    private static VehicleStatus MapStatus(Contracts.VehicleStatus status) => status switch
    {
        Contracts.VehicleStatus.Active => VehicleStatus.Active,
        Contracts.VehicleStatus.InMaintenance => VehicleStatus.InMaintenance,
        Contracts.VehicleStatus.Retired => VehicleStatus.Retired,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown vehicle status."),
    };
}
