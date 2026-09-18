namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Creates a route for the authenticated driver. There is no driver field here - ownership
/// is always the caller's own driver profile, derived server-side from the access token,
/// never taken from the request body.
/// </summary>
/// <param name="RouteNumber">Human-readable identifier used on paperwork. Must be unique.</param>
/// <param name="VehicleId">Vehicle to assign, if decided yet. Optional - a route can be planned before a vehicle is picked.</param>
/// <param name="RouteDate">The calendar day this route is worked.</param>
public sealed record CreateRouteRequest(
    string RouteNumber,
    Guid? VehicleId,
    DateOnly RouteDate);
