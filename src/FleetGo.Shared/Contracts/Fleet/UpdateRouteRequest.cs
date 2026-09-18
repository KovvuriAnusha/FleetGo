namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Replaces the editable fields of an existing route. Ownership cannot be changed through
/// this request - a route always stays with the driver who created it.
/// </summary>
/// <param name="RouteNumber">Human-readable identifier used on paperwork. Must be unique.</param>
/// <param name="VehicleId">Vehicle assigned to the route, if any.</param>
/// <param name="RouteDate">The calendar day this route is worked.</param>
/// <param name="Status">Lifecycle status.</param>
public sealed record UpdateRouteRequest(
    string RouteNumber,
    Guid? VehicleId,
    DateOnly RouteDate,
    RouteStatus Status);
