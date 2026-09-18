namespace FleetGo.API.Data.Entities;

/// <summary>
/// A driver's planned or in-progress work for a single day: an ordered set of
/// <see cref="Stop"/>s. Always owned by exactly one <see cref="Driver"/> - there is no
/// unassigned route in this model, matching how a route is created (see RouteEndpoints).
/// <para>
/// Named <c>Route</c> deliberately, matching the domain vocabulary used throughout the rest
/// of this phase - confirmed safe against this project's own implicit global usings (which
/// include <c>Microsoft.AspNetCore.Routing</c>): modern ASP.NET Core no longer ships a type
/// named <c>Route</c> in that namespace (the legacy <c>IRouter</c>-based routing types were
/// removed), so there is no CS0104 ambiguity here.
/// </para>
/// </summary>
public sealed class Route
{
    public Guid Id { get; set; }

    /// <summary>Human-readable identifier used on paperwork and in conversation, e.g. "R-2026-0912-01".</summary>
    public required string RouteNumber { get; set; }

    public required Guid DriverId { get; set; }

    public Driver? Driver { get; set; }

    /// <summary>Vehicle assigned for this route, if decided yet. Optional so a route can be planned before a vehicle is picked.</summary>
    public Guid? VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    /// <summary>The calendar day this route is/was worked - a date, not a timestamp.</summary>
    public required DateOnly RouteDate { get; set; }

    public RouteStatus Status { get; set; } = RouteStatus.Planned;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<Stop> Stops { get; set; } = [];
}
