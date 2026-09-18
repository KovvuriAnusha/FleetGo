namespace FleetGo.API.Data.Entities;

/// <summary>
/// A fleet vehicle. Assignment to a <see cref="Driver"/> is optional and tracks only the
/// current assignee - a full assignment-history table is not modelled yet; later phases can
/// add one without touching this shape if that becomes a real requirement.
/// </summary>
public sealed class Vehicle
{
    public Guid Id { get; set; }

    /// <summary>License plate / registration number - the identifier a driver actually reads off the vehicle.</summary>
    public required string RegistrationNumber { get; set; }

    public required string Make { get; set; }

    public required string Model { get; set; }

    /// <summary>Model year. Optional - not every seed/demo vehicle needs one.</summary>
    public int? Year { get; set; }

    public VehicleStatus Status { get; set; } = VehicleStatus.Active;

    /// <summary>Currently assigned driver, if any. Null means the vehicle is unassigned.</summary>
    public Guid? DriverId { get; set; }

    public Driver? Driver { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
