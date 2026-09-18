namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Adds a vehicle to the fleet. The new vehicle always starts <see cref="VehicleStatus.Active"/> -
/// use <see cref="UpdateVehicleRequest"/> to change its status later.
/// </summary>
/// <param name="RegistrationNumber">License plate / registration number. Must be unique across the fleet.</param>
/// <param name="Make">Vehicle manufacturer.</param>
/// <param name="Model">Vehicle model.</param>
/// <param name="Year">Model year, if known.</param>
/// <param name="DriverId">
/// Driver to assign the vehicle to, if any. Must be null or the caller's own driver profile -
/// a driver cannot assign a vehicle to someone else.
/// </param>
public sealed record CreateVehicleRequest(
    string RegistrationNumber,
    string Make,
    string Model,
    int? Year,
    Guid? DriverId);
