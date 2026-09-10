namespace FleetGo.API.Data.Entities;

/// <summary>
/// The driver-specific profile for a <see cref="User"/>. Kept separate from
/// <see cref="User"/> so a future dispatch/admin account is not forced to carry driver-only
/// columns, and so a driver's operational fields can grow (Phase 4+: vehicle assignment,
/// certifications) without widening the identity table.
/// </summary>
public sealed class Driver
{
    public Guid Id { get; set; }

    /// <summary>One-to-one owner. A user has at most one driver profile.</summary>
    public required Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Short, human-readable identifier used on paperwork and in conversation
    /// (e.g. spoken over radio), distinct from the database <see cref="Id"/>.
    /// </summary>
    public required string DriverCode { get; set; }

    public string? PhoneNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

