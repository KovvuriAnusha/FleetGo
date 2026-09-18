namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>A single parcel to be delivered at a stop, as returned by the package endpoints.</summary>
/// <param name="Id">Stable identifier for the package.</param>
/// <param name="StopId">The stop this package is being delivered at.</param>
/// <param name="TrackingNumber">Carrier/customer-facing tracking number.</param>
/// <param name="Description">Free-text description of the package's contents, when recorded.</param>
/// <param name="Status">Current delivery status.</param>
/// <param name="CreatedAtUtc">When the package record was created.</param>
/// <param name="UpdatedAtUtc">When the package record was last changed.</param>
public sealed record PackageResponse(
    Guid Id,
    Guid StopId,
    string TrackingNumber,
    string? Description,
    PackageStatus Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
