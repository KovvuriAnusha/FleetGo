namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>Replaces the editable fields of an existing vehicle.</summary>
/// <param name="RegistrationNumber">License plate / registration number. Must be unique across the fleet.</param>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Vehicle model.</param>
/// <param name="Year">Model year, if known.</param>
/// <param name="Status">Operational status.</param>
/// <param name="DriverId">
/// Driver to assign the vehicle to, if any. Must be null or the caller's own driver profile -
/// a driver cannot assign a vehicle to someone else.
/// </param>
public sealed record UpdateVehicleRequest(
    string RegistrationNumber,
    string Make,
    string Model,
    int? Year,
    VehicleStatus Status,
    Guid? DriverId);
