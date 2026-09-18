namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>A driver's route for a single day, as returned by the route endpoints.</summary>
/// <param name="Id">Stable identifier for the route.</param>
/// <param name="RouteNumber">Human-readable identifier used on paperwork, e.g. "R-2026-0912-01".</param>
/// <param name="DriverId">The driver this route belongs to. Always the authenticated caller's own driver profile.</param>
/// <param name="DriverCode">The owning driver's short code.</param>
/// <param name="VehicleId">Vehicle assigned for this route, if decided yet.</param>
/// <param name="VehicleRegistrationNumber">The assigned vehicle's registration number, when <paramref name="VehicleId"/> is set.</param>
/// <param name="RouteDate">The calendar day this route is/was worked.</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="StopCount">Number of stops currently on this route.</param>
/// <param name="CreatedAtUtc">When the route was created.</param>
/// <param name="UpdatedAtUtc">When the route was last changed.</param>
public sealed record RouteResponse(
    Guid Id,
    string RouteNumber,
    Guid DriverId,
    string? DriverCode,
    Guid? VehicleId,
    string? VehicleRegistrationNumber,
    DateOnly RouteDate,
    RouteStatus Status,
    int StopCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
