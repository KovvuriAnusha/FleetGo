namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>One point on a route, as returned by the stop endpoints.</summary>
/// <param name="Id">Stable identifier for the stop.</param>
/// <param name="RouteId">The route this stop belongs to.</param>
/// <param name="CustomerId">The customer being visited.</param>
/// <param name="CustomerName">The customer's name, for display without a second lookup.</param>
/// <param name="Sequence">Position of this stop within its route, starting at 1.</param>
/// <param name="Status">Current delivery status.</param>
/// <param name="DeliveryNotes">Free-text delivery instructions specific to this stop.</param>
/// <param name="PackageCount">Number of packages currently at this stop.</param>
/// <param name="CreatedAtUtc">When the stop was added to its route.</param>
/// <param name="UpdatedAtUtc">When the stop was last changed.</param>
public sealed record StopResponse(
    Guid Id,
    Guid RouteId,
    Guid CustomerId,
    string CustomerName,
    int Sequence,
    StopStatus Status,
    string? DeliveryNotes,
    int PackageCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
