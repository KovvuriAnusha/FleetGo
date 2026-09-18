namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>Fleet vehicle, as returned by the vehicle endpoints.</summary>
/// <param name="Id">Stable identifier for the vehicle.</param>
/// <param name="RegistrationNumber">License plate / registration number.</param>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Vehicle model.</param>
/// <param name="Year">Model year, when recorded.</param>
/// <param name="Status">Current operational status.</param>
/// <param name="DriverId">Currently assigned driver, if any. Null means the vehicle is unassigned.</param>
/// <param name="DriverCode">The assigned driver's short code, when <paramref name="DriverId"/> is set.</param>
/// <param name="CreatedAtUtc">When the vehicle was added to the fleet.</param>
/// <param name="UpdatedAtUtc">When the vehicle record was last changed.</param>
public sealed record VehicleResponse(
    Guid Id,
    string RegistrationNumber,
    string Make,
    string Model,
    int? Year,
    VehicleStatus Status,
    Guid? DriverId,
    string? DriverCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
