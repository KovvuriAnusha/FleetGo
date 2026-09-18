namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Adds a package to a stop. The caller must own <paramref name="StopId"/>'s route (via the
/// stop) - a package can never be added to a stop on another driver's route.
/// </summary>
/// <param name="StopId">The stop this package is being delivered at.</param>
/// <param name="TrackingNumber">Carrier/customer-facing tracking number. Must be unique.</param>
/// <param name="Description">Free-text description of the package's contents, if any.</param>
public sealed record CreatePackageRequest(
    Guid StopId,
    string TrackingNumber,
    string? Description);
