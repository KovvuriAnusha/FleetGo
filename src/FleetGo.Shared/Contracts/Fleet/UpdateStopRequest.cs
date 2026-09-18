namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Replaces the editable fields of an existing stop. The stop's route cannot be changed
/// through this request - a stop always stays on the route it was created for.
/// </summary>
/// <param name="CustomerId">The customer to visit.</param>
/// <param name="Sequence">Position of this stop within its route, starting at 1. Must be unique within the route.</param>
/// <param name="Status">Delivery status.</param>
/// <param name="DeliveryNotes">Free-text delivery instructions specific to this stop, if any.</param>
public sealed record UpdateStopRequest(
    Guid CustomerId,
    int Sequence,
    StopStatus Status,
    string? DeliveryNotes);
