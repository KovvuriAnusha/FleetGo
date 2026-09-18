namespace FleetGo.Shared.Contracts.Fleet;

/// <summary>
/// Adds a stop to a route. The caller must own <paramref name="RouteId"/>'s route - a stop
/// can never be added to another driver's route.
/// </summary>
/// <param name="RouteId">The route to add this stop to.</param>
/// <param name="CustomerId">The customer to visit.</param>
/// <param name="Sequence">Position of this stop within its route, starting at 1. Must be unique within the route.</param>
/// <param name="DeliveryNotes">Free-text delivery instructions specific to this stop, if any.</param>
public sealed record CreateStopRequest(
    Guid RouteId,
    Guid CustomerId,
    int Sequence,
    string? DeliveryNotes);
