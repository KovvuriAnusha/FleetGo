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
/// Stop endpoints. A stop's ownership is derived entirely through its route: the caller
/// must own the route a stop belongs to (or is being added to) for every operation here.
/// Listing without a <c>routeId</c> filter returns stops across every route the caller
/// owns; listing with one is scoped to that single route (and 404s if the caller doesn't
/// own it) - a driver can never see or touch another driver's stop this way.
/// </summary>
internal static class StopEndpoints
{
    private static readonly string[] ValidSortValues = ["sequence", "-sequence"];

    public static IEndpointRouteBuilder MapStopEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup(ApiRoutes.StopsBase)
            .WithTags("Fleet")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListStops")
            .WithSummary("Lists stops on the authenticated driver's routes.")
            .WithDescription("Optionally scoped to one route via routeId. Filter by status or customer name; sort by sequence (prefix '-' for descending).");

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetStop")
            .WithSummary("Gets a single stop by id.")
            .WithDescription("404 for a stop that doesn't exist or whose route belongs to another driver.");

        group.MapPost("/", CreateAsync)
            .WithName("CreateStop")
            .WithSummary("Adds a stop to one of the authenticated driver's routes.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateStop")
            .WithSummary("Replaces the editable fields of an existing stop.");

        return endpoints;
    }

    private static async Task<Results<Ok<PagedResponse<Contracts.StopResponse>>, NotFound, ValidationProblem, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        Guid? routeId,
        string? status,
        string? search,
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

        if (!QueryParsing.TryParseEnum(status, out StopStatus? statusFilter, out string? statusError))
        {
            errors.Add("status", statusError);
        }

        if (!IsValidSort(sort, out string? sortError))
        {
            errors.Add("sort", sortError);
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        if (routeId is not null)
        {
            bool ownsRoute = await db.Routes.AsNoTracking()
                .AnyAsync(r => r.Id == routeId.Value && r.DriverId == driverId.Value, cancellationToken);

            if (!ownsRoute)
            {
                return TypedResults.NotFound();
            }
        }

        IQueryable<Stop> query = db.Stops.AsNoTracking().Where(s => s.Route!.DriverId == driverId.Value);

        if (routeId is not null)
        {
            query = query.Where(s => s.RouteId == routeId.Value);
        }

        if (statusFilter is not null)
        {
            query = query.Where(s => s.Status == statusFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Customer!.Name.Contains(search));
        }

        query = ApplySort(query, sort);

        var projected = query.Select(s => new
        {
            s.Id,
            s.RouteId,
            s.CustomerId,
            CustomerName = s.Customer!.Name,
            s.Sequence,
            s.Status,
            s.DeliveryNotes,
            PackageCount = s.Packages.Count,
            s.CreatedAtUtc,
            s.UpdatedAtUtc,
        });

        var paged = await projected.ToPagedResponseAsync(pageRequest!, cancellationToken);

        var response = new PagedResponse<Contracts.StopResponse>(
            paged.Items.Select(s => new Contracts.StopResponse(
                s.Id, s.RouteId, s.CustomerId, s.CustomerName, s.Sequence, MapStatus(s.Status),
                s.DeliveryNotes, s.PackageCount, s.CreatedAtUtc, s.UpdatedAtUtc)).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<Contracts.StopResponse>, NotFound, ProblemHttpResult>> GetByIdAsync(
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

        var stop = await db.Stops.AsNoTracking()
            .Where(s => s.Id == id && s.Route!.DriverId == driverId.Value)
            .Select(s => new
            {
                s.Id,
                s.RouteId,
                s.CustomerId,
                CustomerName = s.Customer!.Name,
                s.Sequence,
                s.Status,
                s.DeliveryNotes,
                PackageCount = s.Packages.Count,
                s.CreatedAtUtc,
                s.UpdatedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (stop is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new Contracts.StopResponse(
            stop.Id, stop.RouteId, stop.CustomerId, stop.CustomerName, stop.Sequence, MapStatus(stop.Status),
            stop.DeliveryNotes, stop.PackageCount, stop.CreatedAtUtc, stop.UpdatedAtUtc));
    }

    private static async Task<Results<Created<Contracts.StopResponse>, NotFound, ValidationProblem, ProblemHttpResult>> CreateAsync(
        [FromBody] Contracts.CreateStopRequest request,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Guid? driverId = user.GetDriverId();
        if (driverId is null)
        {
            return FleetProblems.Forbidden("This account has no driver profile.");
        }

        Route? route = await db.Routes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == request.RouteId, cancellationToken);

        if (route is null || route.DriverId != driverId.Value)
        {
            // A route that doesn't exist and a route owned by someone else both 404 - a
            // driver can never add a stop to another driver's route, and can't tell the
            // difference between "not yours" and "doesn't exist" by trying.
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();

        if (request.Sequence < 1)
        {
            errors.Add("sequence", "sequence must be 1 or greater.");
        }

        errors.AddIfTooLong(request.DeliveryNotes, 500, "deliveryNotes");

        bool customerExists = await db.Customers.AsNoTracking().AnyAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (!customerExists)
        {
            errors.Add("customerId", "customerId does not refer to an existing customer.");
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        bool sequenceInUse = await db.Stops.AsNoTracking()
            .AnyAsync(s => s.RouteId == request.RouteId && s.Sequence == request.Sequence, cancellationToken);

        if (sequenceInUse)
        {
            return FleetProblems.Conflict("This route already has a stop at that sequence position.");
        }

        DateTime now = DateTime.UtcNow;
        var stop = new Stop
        {
            Id = Guid.NewGuid(),
            RouteId = request.RouteId,
            CustomerId = request.CustomerId,
            Sequence = request.Sequence,
            Status = StopStatus.Pending,
            DeliveryNotes = request.DeliveryNotes,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Stops.Add(stop);
        await db.SaveChangesAsync(cancellationToken);

        string customerName = await db.Customers.AsNoTracking()
            .Where(c => c.Id == stop.CustomerId)
            .Select(c => c.Name)
            .FirstAsync(cancellationToken);

        var response = new Contracts.StopResponse(
            stop.Id, stop.RouteId, stop.CustomerId, customerName, stop.Sequence, MapStatus(stop.Status),
            stop.DeliveryNotes, 0, stop.CreatedAtUtc, stop.UpdatedAtUtc);

        return TypedResults.Created($"{ApiRoutes.StopsBase}/{stop.Id}", response);
    }

    private static async Task<Results<Ok<Contracts.StopResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        [FromBody] Contracts.UpdateStopRequest request,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Guid? driverId = user.GetDriverId();
        if (driverId is null)
        {
            return FleetProblems.Forbidden("This account has no driver profile.");
        }

        Stop? stop = await db.Stops
            .Include(s => s.Route)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (stop is null || stop.Route is null || stop.Route.DriverId != driverId.Value)
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();

        if (request.Sequence < 1)
        {
            errors.Add("sequence", "sequence must be 1 or greater.");
        }

        errors.AddIfTooLong(request.DeliveryNotes, 500, "deliveryNotes");

        if (!Enum.IsDefined(request.Status))
        {
            errors.Add("status", $"'{request.Status}' is not a valid stop status.");
        }

        bool customerExists = await db.Customers.AsNoTracking().AnyAsync(c => c.Id == request.CustomerId, cancellationToken);
        if (!customerExists)
        {
            errors.Add("customerId", "customerId does not refer to an existing customer.");
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        bool sequenceInUse = await db.Stops.AsNoTracking()
            .AnyAsync(s => s.Id != id && s.RouteId == stop.RouteId && s.Sequence == request.Sequence, cancellationToken);

        if (sequenceInUse)
        {
            return FleetProblems.Conflict("This route already has a stop at that sequence position.");
        }

        stop.CustomerId = request.CustomerId;
        stop.Sequence = request.Sequence;
        stop.Status = MapStatus(request.Status);
        stop.DeliveryNotes = request.DeliveryNotes;
        stop.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        string customerName = await db.Customers.AsNoTracking()
            .Where(c => c.Id == stop.CustomerId)
            .Select(c => c.Name)
            .FirstAsync(cancellationToken);

        int packageCount = await db.Packages.AsNoTracking().CountAsync(p => p.StopId == stop.Id, cancellationToken);

        var response = new Contracts.StopResponse(
            stop.Id, stop.RouteId, stop.CustomerId, customerName, stop.Sequence, MapStatus(stop.Status),
            stop.DeliveryNotes, packageCount, stop.CreatedAtUtc, stop.UpdatedAtUtc);

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

    private static IQueryable<Stop> ApplySort(IQueryable<Stop> query, string? sort) => sort switch
    {
        "-sequence" => query.OrderByDescending(s => s.Sequence),
        _ => query.OrderBy(s => s.Sequence),
    };

    private static Contracts.StopStatus MapStatus(StopStatus status) => status switch
    {
        StopStatus.Pending => Contracts.StopStatus.Pending,
        StopStatus.Arrived => Contracts.StopStatus.Arrived,
        StopStatus.Completed => Contracts.StopStatus.Completed,
        StopStatus.Failed => Contracts.StopStatus.Failed,
        StopStatus.Skipped => Contracts.StopStatus.Skipped,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown stop status."),
    };

    private static StopStatus MapStatus(Contracts.StopStatus status) => status switch
    {
        Contracts.StopStatus.Pending => StopStatus.Pending,
        Contracts.StopStatus.Arrived => StopStatus.Arrived,
        Contracts.StopStatus.Completed => StopStatus.Completed,
        Contracts.StopStatus.Failed => StopStatus.Failed,
        Contracts.StopStatus.Skipped => StopStatus.Skipped,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown stop status."),
    };
}
