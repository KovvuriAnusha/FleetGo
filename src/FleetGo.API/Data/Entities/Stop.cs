namespace FleetGo.API.Data.Entities;

/// <summary>
/// One point on a <see cref="Route"/>: a visit to a <see cref="Customer"/> carrying zero or
/// more <see cref="Package"/>s. The delivery location comes from the linked
/// <see cref="Customer"/>'s address rather than being duplicated here -
/// <see cref="DeliveryNotes"/> is for anything that varies stop to stop (a gate code, "leave
/// at back door") without needing a second address on file.
/// </summary>
public sealed class Stop
{
    public Guid Id { get; set; }

    public required Guid RouteId { get; set; }

    public Route? Route { get; set; }

    public required Guid CustomerId { get; set; }

    public Customer? Customer { get; set; }

    /// <summary>Position of this stop within its route, starting at 1. Drives the order the driver visits stops in.</summary>
    public required int Sequence { get; set; }

    public StopStatus Status { get; set; } = StopStatus.Pending;

    /// <summary>Free-text delivery instructions specific to this stop (not the customer's general address).</summary>
    public string? DeliveryNotes { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<Package> Packages { get; set; } = [];
}
