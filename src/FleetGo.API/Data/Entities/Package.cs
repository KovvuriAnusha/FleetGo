namespace FleetGo.API.Data.Entities;

/// <summary>
/// A single parcel to be delivered at a <see cref="Stop"/>. Proof of delivery, barcode
/// scanning and photo capture are explicitly later-phase work (see docs/roadmap.md) - this
/// entity only carries the identifying and status information those phases will attach data to.
/// </summary>
public sealed class Package
{
    public Guid Id { get; set; }

    public required Guid StopId { get; set; }

    public Stop? Stop { get; set; }

    /// <summary>Carrier/customer-facing tracking number - globally unique, distinct from the database <see cref="Id"/>.</summary>
    public required string TrackingNumber { get; set; }

    public string? Description { get; set; }

    public PackageStatus Status { get; set; } = PackageStatus.Pending;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
