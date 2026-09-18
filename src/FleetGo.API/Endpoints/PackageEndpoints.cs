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
/// Package endpoints. A package's ownership is derived through its stop and, in turn, that
/// stop's route: the caller must own the route behind a package's stop (or the stop it is
/// being added to) for every operation here. Listing without a <c>stopId</c> filter returns
/// packages across every stop on every route the caller owns; listing with one is scoped to
/// that single stop (and 404s if the caller doesn't own its route).
/// </summary>
internal static class PackageEndpoints
{
    public static IEndpointRouteBuilder MapPackageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup(ApiRoutes.PackagesBase)
            .WithTags("Fleet")
            .RequireAuthorization();

        group.MapGet("/", ListAsync)
            .WithName("ListPackages")
            .WithSummary("Lists packages on the authenticated driver's stops.")
            .WithDescription("Optionally scoped to one stop via stopId. Filter by status or tracking number; sorted by tracking number.");

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetPackage")
            .WithSummary("Gets a single package by id.")
            .WithDescription("404 for a package that doesn't exist or whose stop's route belongs to another driver.");

        group.MapPost("/", CreateAsync)
            .WithName("CreatePackage")
            .WithSummary("Adds a package to one of the authenticated driver's stops.");

        group.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdatePackage")
            .WithSummary("Replaces the editable fields of an existing package.");

        return endpoints;
    }

    private static async Task<Results<Ok<PagedResponse<Contracts.PackageResponse>>, NotFound, ValidationProblem, ProblemHttpResult>> ListAsync(
        int? page,
        int? pageSize,
        Guid? stopId,
        string? status,
        string? search,
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

        if (!QueryParsing.TryParseEnum(status, out PackageStatus? statusFilter, out string? statusError))
        {
            errors.Add("status", statusError);
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        if (stopId is not null)
        {
            bool ownsStop = await db.Stops.AsNoTracking()
                .AnyAsync(s => s.Id == stopId.Value && s.Route!.DriverId == driverId.Value, cancellationToken);

            if (!ownsStop)
            {
                return TypedResults.NotFound();
            }
        }

        IQueryable<Package> query = db.Packages.AsNoTracking()
            .Where(p => p.Stop!.Route!.DriverId == driverId.Value);

        if (stopId is not null)
        {
            query = query.Where(p => p.StopId == stopId.Value);
        }

        if (statusFilter is not null)
        {
            query = query.Where(p => p.Status == statusFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.TrackingNumber.Contains(search));
        }

        var projected = query
            .OrderBy(p => p.TrackingNumber)
            .Select(p => new
            {
                p.Id,
                p.StopId,
                p.TrackingNumber,
                p.Description,
                p.Status,
                p.CreatedAtUtc,
                p.UpdatedAtUtc,
            });

        var paged = await projected.ToPagedResponseAsync(pageRequest!, cancellationToken);

        var response = new PagedResponse<Contracts.PackageResponse>(
            paged.Items.Select(p => new Contracts.PackageResponse(
                p.Id, p.StopId, p.TrackingNumber, p.Description, MapStatus(p.Status), p.CreatedAtUtc, p.UpdatedAtUtc)).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalCount);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<Contracts.PackageResponse>, NotFound, ProblemHttpResult>> GetByIdAsync(
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

        var package = await db.Packages.AsNoTracking()
            .Where(p => p.Id == id && p.Stop!.Route!.DriverId == driverId.Value)
            .Select(p => new
            {
                p.Id,
                p.StopId,
                p.TrackingNumber,
                p.Description,
                p.Status,
                p.CreatedAtUtc,
                p.UpdatedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (package is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new Contracts.PackageResponse(
            package.Id, package.StopId, package.TrackingNumber, package.Description,
            MapStatus(package.Status), package.CreatedAtUtc, package.UpdatedAtUtc));
    }

    private static async Task<Results<Created<Contracts.PackageResponse>, NotFound, ValidationProblem, ProblemHttpResult>> CreateAsync(
        [FromBody] Contracts.CreatePackageRequest request,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Guid? driverId = user.GetDriverId();
        if (driverId is null)
        {
            return FleetProblems.Forbidden("This account has no driver profile.");
        }

        bool ownsStop = await db.Stops.AsNoTracking()
            .AnyAsync(s => s.Id == request.StopId && s.Route!.DriverId == driverId.Value, cancellationToken);

        if (!ownsStop)
        {
            // A stop that doesn't exist and a stop on someone else's route both 404 - a
            // driver can never add a package to another driver's stop, and can't tell the
            // difference between "not yours" and "doesn't exist" by trying.
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            errors.Add("trackingNumber", "trackingNumber is required.");
        }

        errors.AddIfTooLong(request.TrackingNumber, 40, "trackingNumber");
        errors.AddIfTooLong(request.Description, 300, "description");

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        bool trackingNumberInUse = await db.Packages.AsNoTracking()
            .AnyAsync(p => p.TrackingNumber == request.TrackingNumber, cancellationToken);

        if (trackingNumberInUse)
        {
            return FleetProblems.Conflict("A package with this tracking number already exists.");
        }

        DateTime now = DateTime.UtcNow;
        var package = new Package
        {
            Id = Guid.NewGuid(),
            StopId = request.StopId,
            TrackingNumber = request.TrackingNumber,
            Description = request.Description,
            Status = PackageStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.Packages.Add(package);
        await db.SaveChangesAsync(cancellationToken);

        var response = new Contracts.PackageResponse(
            package.Id, package.StopId, package.TrackingNumber, package.Description,
            MapStatus(package.Status), package.CreatedAtUtc, package.UpdatedAtUtc);

        return TypedResults.Created($"{ApiRoutes.PackagesBase}/{package.Id}", response);
    }

    private static async Task<Results<Ok<Contracts.PackageResponse>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        [FromBody] Contracts.UpdatePackageRequest request,
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        Guid? driverId = user.GetDriverId();
        if (driverId is null)
        {
            return FleetProblems.Forbidden("This account has no driver profile.");
        }

        Package? package = await db.Packages
            .Include(p => p.Stop)
            .ThenInclude(s => s!.Route)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (package is null || package.Stop?.Route is null || package.Stop.Route.DriverId != driverId.Value)
        {
            return TypedResults.NotFound();
        }

        var errors = new ValidationErrors();

        if (string.IsNullOrWhiteSpace(request.TrackingNumber))
        {
            errors.Add("trackingNumber", "trackingNumber is required.");
        }

        errors.AddIfTooLong(request.TrackingNumber, 40, "trackingNumber");
        errors.AddIfTooLong(request.Description, 300, "description");

        if (!Enum.IsDefined(request.Status))
        {
            errors.Add("status", $"'{request.Status}' is not a valid package status.");
        }

        if (errors.HasErrors)
        {
            return TypedResults.ValidationProblem(errors.ToDictionary());
        }

        bool trackingNumberInUse = await db.Packages.AsNoTracking()
            .AnyAsync(p => p.Id != id && p.TrackingNumber == request.TrackingNumber, cancellationToken);

        if (trackingNumberInUse)
        {
            return FleetProblems.Conflict("A package with this tracking number already exists.");
        }

        package.TrackingNumber = request.TrackingNumber;
        package.Description = request.Description;
        package.Status = MapStatus(request.Status);
        package.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        var response = new Contracts.PackageResponse(
            package.Id, package.StopId, package.TrackingNumber, package.Description,
            MapStatus(package.Status), package.CreatedAtUtc, package.UpdatedAtUtc);

        return TypedResults.Ok(response);
    }

    private static Contracts.PackageStatus MapStatus(PackageStatus status) => status switch
    {
        PackageStatus.Pending => Contracts.PackageStatus.Pending,
        PackageStatus.OutForDelivery => Contracts.PackageStatus.OutForDelivery,
        PackageStatus.Delivered => Contracts.PackageStatus.Delivered,
        PackageStatus.Failed => Contracts.PackageStatus.Failed,
        PackageStatus.Returned => Contracts.PackageStatus.Returned,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown package status."),
    };

    private static PackageStatus MapStatus(Contracts.PackageStatus status) => status switch
    {
        Contracts.PackageStatus.Pending => PackageStatus.Pending,
        Contracts.PackageStatus.OutForDelivery => PackageStatus.OutForDelivery,
        Contracts.PackageStatus.Delivered => PackageStatus.Delivered,
        Contracts.PackageStatus.Failed => PackageStatus.Failed,
        Contracts.PackageStatus.Returned => PackageStatus.Returned,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown package status."),
    };
}
