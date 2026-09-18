namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Replaces the editable fields of an existing package. The package's stop cannot be changed
/// through this request - a package always stays at the stop it was created for.
/// </summary>
/// <param name="TrackingNumber">Carrier/customer-facing tracking number. Must be unique.</param>
/// <param name="Description">Free-text description of the package's contents, if any.</param>
/// <param name="Status">Delivery status.</param>
public sealed record UpdatePackageRequest(
    string TrackingNumber,
    string? Description,
    PackageStatus Status);
