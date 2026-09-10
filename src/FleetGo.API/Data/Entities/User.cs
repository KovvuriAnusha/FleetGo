namespace FleetGo.API.Data.Entities;

/// <summary>
/// An account that can sign in to FleetGo. A user is not necessarily a driver - later
/// phases add dispatch/back-office roles - which is why the driver profile lives on its
/// own <see cref="Driver"/> entity rather than being folded into this one.
/// </summary>
public sealed class User
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stored lower-cased so lookups and the unique index are case-insensitive without
    /// depending on the database collation.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// Produced by <see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/>.
    /// Never the plaintext password, and never returned to a client.
    /// </summary>
    public required string PasswordHash { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    /// <summary>
    /// An inactive account fails login even with the correct password. Used to disable an
    /// account (offboarding, a suspended driver) without deleting its history.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Present when this account also has a driver profile.</summary>
    public Driver? Driver { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}

