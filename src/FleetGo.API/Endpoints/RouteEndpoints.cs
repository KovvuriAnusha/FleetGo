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
using Route = FleetGo.API.Data.Entities.Route;

namespace FleetGo.API.Endpoints;

/// <summary>
/// Route endpoints. A route is always owned by exactly one driver - the caller's own -
/// derived server-side from the access token, never from the request body or an id in the
/// URL. Listing, viewing, and editing are all scoped to the caller's own routes; another
/// driver's route id resolves to 404, the same as a route id that doesn't exist at all, so a
/// driver can never tell the two apart by probing ids.
/// </summary>
internal static class RouteEndpoints
{
    private static readonly string[] ValidSortValues =
        ["routeDate", "-routeDate", "status", "-status", "routeNumber", "-routeNumber"];

    public static IEndpointRouteBuilder MapRouteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup(ApiRoutes.RoutesBase)
            .WithTags("Fleet")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListRoutes")
            .WithSummary("Lists the authenticated driver's routes.")
            .WithDescription("Always scoped to the caller's own routes. Filter by status/routeDate; sort with routeDate, status, or routeNumber (prefix '-' for descending).");

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetRoute")
            .WithSummary("Gets a single route by id.")
            .WithDescription("404 for a route that doesn't exist or belongs to another driver - the two are indistinguishable from the outside.");

        group.MapPost("/", CreateAsync)
            .WithName("CreateRoute")
            .WithSummary("Creates a route for the authenticated driver.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateRoute")
            .WithSummary("Replaces the editable fields of an existing route.");

        return endpoints;
    }

    private static async Task<Results<Ok<PagedResponse<Contracts.RouteResponse>>, ValidationProblem, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        string? status,
        string? routeDate,
        string? sort,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Guid? driverId = user.GetDriverId();
        if (driverId is null)
        {
            return FleetProblems.Forbidden("This account has no driver profile.");
        }

        var errors = new ValidationErrors();

        if (!PageRequest.TryCreate(page, pageSize, out PageRequest? pageRequest, out string? pageError))
        {
            errors.Add("page", pageError);
        }

        if (!QueryParsing.TryParseEnum(status, out RouteStatus? statusFilter, out string? statusError))
        {
            errors.Add("status", statusError);
        }

        if (!QueryParsing.TryParseDateOnly(routeDate, out DateOnly? dateFilter, out string? dateError))
        {
            errors.Add("routeDate", dateError);
        }

        if (!IsValidSort(sort, out string? sortError))
        {
            errors.Add("sort", sortError);
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        IQueryable<Route> query = db.Routes.AsNoTracking().Where(r => r.DriverId == driverId.Value);

        if (statusFilter is not null)
        {
            query = query.Where(r => r.Status == statusFilter.Value);
        }

        if (dateFilter is not null)
        {
            query = query.Where(r => r.RouteDate == dateFilter.Value);
        }

        query = ApplySort(query, sort);

        var projected = query.Select(r => new
        {
            r.Id,
            r.RouteNumber,
            r.DriverId,
            DriverCode = r.Driver != null ? r.Driver.DriverCode : null,
            r.VehicleId,
            VehicleRegistrationNumber = r.Vehicle != null ? r.Vehicle.RegistrationNumber : null,
            r.RouteDate,
            r.Status,
            StopCount = r.Stops.Count,
            r.CreatedAtUtc,
            r.UpdatedAtUtc,
        });

        var paged = await projected.ToPagedResponseAsync(pageRequest!, cancellationToken);

        var response = new PagedResponse<Contracts.RouteResponse>(
            paged.Items.Select(r => new Contracts.RouteResponse(
                r.Id, r.RouteNumber, r.DriverId, r.DriverCode, r.VehicleId, r.VehicleRegistrationNumber,
                r.RouteDate, MapStatus(r.Status), r.StopCount, r.CreatedAtUtc, r.UpdatedAtUtc)).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<Contracts.RouteResponse>, NotFound, ProblemHttpResult>> GetByIdAsync(
        Guid id,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Guid? driverId = user.GetDriverId();
        if (driverId is null)
        {
            return FleetProblems.Forbidden("This account has no driver profile.");
        }

        var route = await db.Routes.AsNoTracking()
            .Where(r => r.Id == id && r.DriverId == driverId.Value)
            .Select(r => new
            {
                r.Id,
                r.RouteNumber,
                r.DriverId,
                DriverCode = r.Driver != null ? r.Driver.DriverCode : null,
                r.VehicleId,
                VehicleRegistrationNumber = r.Vehicle != null ? r.Vehicle.RegistrationNumber : null,
                r.RouteDate,
                r.Status,
                StopCount = r.Stops.Count,
                r.CreatedAtUtc,
                r.UpdatedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (route is null)
        {
            // Either the route doesn't exist, or it belongs to another driver - both look
            // identical from the outside, so a driver can never probe for another's route ids.
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new Contracts.RouteResponse(
            route.Id, route.RouteNumber, route.DriverId, route.DriverCode, route.VehicleId,
            route.VehicleRegistrationNumber, route.RouteDate, MapStatus(route.Status), route.StopCount,
            route.CreatedAtUtc, route.UpdatedAtUtc));
    }

    private static async Task<Results<Created<Contracts.RouteResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        [FromBody] Contracts.CreateRouteRequest request,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Guid? driverId = user.GetDriverId();
        if (driverId is null)
        {
            return FleetProblems.Forbidden("This account has no driver profile.");
        }

        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.RouteNumber))
        {
            errors.Add("routeNumber", "routeNumber is required.");
        }

        errors.AddIfTooLong(request.RouteNumber, 30, "routeNumber");

        if (request.RouteDate == default)
        {
            errors.Add("routeDate", "routeDate is required.");
        }

        Vehicle? vehicle = null;
        if (request.VehicleId is not null)
        {
            vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);
            if (vehicle is null)
            {
                errors.Add("vehicleId", "vehicleId does not refer to an existing vehicle.");
            }
            else if (vehicle.DriverId is not null && vehicle.DriverId != driverId.Value)
            {
                // The vehicle exists but is currently assigned to a different driver - a
                // route can only reference a vehicle that's unassigned or already the
                // caller's own, mirroring the self-assignment-only rule VehicleEndpoints
                // enforces for direct vehicle assignment.
                errors.Add("vehicleId", "vehicleId refers to a vehicle assigned to another driver.");
            }
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        bool routeNumberInUse = await db.Routes.AsNoTracking()
            .AnyAsync(r => r.RouteNumber == request.RouteNumber, cancellationToken);

        if (routeNumberInUse)
        {
            return FleetProblems.Conflict("A route with this route number already exists.");
        }

        DateTime now = DateTime.UtcNow;
        var route = new Route
        {
            Id = Guid.NewGuid(),
            RouteNumber = request.RouteNumber,
            DriverId = driverId.Value,
            VehicleId = request.VehicleId,
            RouteDate = request.RouteDate,
            Status = RouteStatus.Planned,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Routes.Add(route);
        await db.SaveChangesAsync(cancellationToken);

        string? driverCode = await db.Drivers.AsNoTracking()
            .Where(d => d.Id == driverId.Value)
            .Select(d => d.DriverCode)
            .FirstOrDefaultAsync(cancellationToken);

        var response = new Contracts.RouteResponse(
            route.Id, route.RouteNumber, route.DriverId, driverCode, route.VehicleId,
            vehicle?.RegistrationNumber, route.RouteDate, MapStatus(route.Status), 0, route.CreatedAtUtc, route.UpdatedAtUtc);

        return TypedResults.Created($"{ApiRoutes.RoutesBase}/{route.Id}", response);
    }

    private static async Task<Results<Ok<Contracts.RouteResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        [FromBody] Contracts.UpdateRouteRequest request,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Guid? driverId = user.GetDriverId();
        if (driverId is null)
        {
            return FleetProblems.Forbidden("This account has no driver profile.");
        }

        Route? route = await db.Routes.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (route is null || route.DriverId != driverId.Value)
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.RouteNumber))
        {
            errors.Add("routeNumber", "routeNumber is required.");
        }

        errors.AddIfTooLong(request.RouteNumber, 30, "routeNumber");

        if (request.RouteDate == default)
        {
            errors.Add("routeDate", "routeDate is required.");
        }

        if (!Enum.IsDefined(request.Status))
        {
            errors.Add("status", $"'{request.Status}' is not a valid route status.");
        }

        Vehicle? vehicle = null;
        if (request.VehicleId is not null)
        {
            vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);
            if (vehicle is null)
            {
                errors.Add("vehicleId", "vehicleId does not refer to an existing vehicle.");
            }
            else if (vehicle.DriverId is not null && vehicle.DriverId != driverId.Value)
            {
                errors.Add("vehicleId", "vehicleId refers to a vehicle assigned to another driver.");
            }
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        bool routeNumberInUse = await db.Routes.AsNoTracking()
            .AnyAsync(r => r.Id != id && r.RouteNumber == request.RouteNumber, cancellationToken);

        if (routeNumberInUse)
        {
            return FleetProblems.Conflict("A route with this route number already exists.");
        }

        route.RouteNumber = request.RouteNumber;
        route.VehicleId = request.VehicleId;
        route.RouteDate = request.RouteDate;
        route.Status = MapStatus(request.Status);
        route.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        string? driverCode = await db.Drivers.AsNoTracking()
            .Where(d => d.Id == driverId.Value)
            .Select(d => d.DriverCode)
            .FirstOrDefaultAsync(cancellationToken);

        int stopCount = await db.Stops.AsNoTracking().CountAsync(s => s.RouteId == route.Id, cancellationToken);

        var response = new Contracts.RouteResponse(
            route.Id, route.RouteNumber, route.DriverId, driverCode, route.VehicleId,
            vehicle?.RegistrationNumber, route.RouteDate, MapStatus(route.Status), stopCount,
            route.CreatedAtUtc, route.UpdatedAtUtc);

        return TypedResults.Ok(response);
    }

    private static bool IsValidSort(string? sort, out string? error)
    {
        if (string.IsNullOrWhiteSpace(sort) || ValidSortValues.Contains(sort))
        {
            error = null;
            return true;
        }

        error = $"'{sort}' is not a valid sort value. Expected one of: {string.Join(", ", ValidSortValues)}.";
        return false;
    }

    private static IQueryable<Route> ApplySort(IQueryable<Route> query, string? sort) => sort switch
    {
        "-routeDate" => query.OrderByDescending(r => r.RouteDate).ThenBy(r => r.RouteNumber),
        "status" => query.OrderBy(r => r.Status).ThenBy(r => r.RouteDate),
        "-status" => query.OrderByDescending(r => r.Status).ThenBy(r => r.RouteDate),
        "routeNumber" => query.OrderBy(r => r.RouteNumber),
        "-routeNumber" => query.OrderByDescending(r => r.RouteNumber),
        _ => query.OrderBy(r => r.RouteDate).ThenBy(r => r.RouteNumber),
    };

    private static Contracts.RouteStatus MapStatus(RouteStatus status) => status switch
    {
        RouteStatus.Planned => Contracts.RouteStatus.Planned,
        RouteStatus.InProgress => Contracts.RouteStatus.InProgress,
        RouteStatus.Completed => Contracts.RouteStatus.Completed,
        RouteStatus.Cancelled => Contracts.RouteStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown route status."),
    };

    private static RouteStatus MapStatus(Contracts.RouteStatus status) => status switch
    {
        Contracts.RouteStatus.Planned => RouteStatus.Planned,
        Contracts.RouteStatus.InProgress => RouteStatus.InProgress,
        Contracts.RouteStatus.Completed => RouteStatus.Completed,
        Contracts.RouteStatus.Cancelled => RouteStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown route status."),
    };
}
