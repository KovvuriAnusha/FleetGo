namespace FleetGo.API.Data.Entities;

/// <summary>
/// A single issued refresh token, stored hashed. One row per token ever issued (rotation
/// creates a new row rather than mutating an old one), which is what makes "was this
/// specific token replaced, and by what" answerable later - useful for spotting a stolen
/// token replayed after rotation.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }

    public required Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// SHA-256 hash of the token. The raw token is a high-entropy random value handed to
    /// the client and never persisted - a slow, salted password hash (bcrypt/PBKDF2) would
    /// protect against nothing extra here and would make every refresh call measurably
    /// slower for no security benefit, so a fast cryptographic hash is the right tool.
    /// </summary>
    public required byte[] TokenHash { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Null while still valid. Set the moment the token is used, rotated, or logged out.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// The token that replaced this one during rotation, when that is what revoked it.
    /// Null for a token revoked by logout, or one that simply expired.
    /// </summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive => RevokedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}

