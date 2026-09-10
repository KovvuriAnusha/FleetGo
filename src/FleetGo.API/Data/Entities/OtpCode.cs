namespace FleetGo.API.Data.Entities;

/// <summary>
/// A single issued one-time code, stored hashed - never in plaintext. Mirrors
/// <see cref="RefreshToken"/>'s shape deliberately (one row per issuance, hash rather than
/// raw value, an explicit revoked/invalidated marker) since the two share the same threat
/// model: a leaked database row must not be enough to recover something a caller could use.
/// </summary>
public sealed class OtpCode
{
    public Guid Id { get; set; }

    public required Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>What this code is for - see <c>FleetGo.API.Auth.OtpPurposes</c>. Scopes "one active code at a time" and the attempt/lockout counters.</summary>
    public required string Purpose { get; set; }

    /// <summary>
    /// Random per-row salt mixed into <see cref="CodeHash"/>'s HMAC input. A 6-digit code
    /// has only a million possible values, so without a salt every OTP with the same digits
    /// would hash identically; the salt means no two rows ever collide even when two users
    /// happen to be issued the same code.
    /// </summary>
    public required byte[] Salt { get; set; }

    /// <summary>
    /// HMAC-SHA256(key: Otp:HashingKey, message: Salt || code). Keyed with a server-side
    /// secret that never lives in the database (see <c>OtpOptions.HashingKey</c>) rather than
    /// a plain hash, because a plain SHA-256 of a 6-digit code would let anyone who reads this
    /// table brute-force it offline in well under a second.
    /// </summary>
    public required byte[] CodeHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Null until a correct code is verified against this row. Set once - a code is single-use.</summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>
    /// Set when this code stopped being eligible for verification for a reason other than
    /// being consumed: superseded by a newer request for the same user/purpose, or its
    /// <see cref="AttemptCount"/> reached the configured maximum.
    /// </summary>
    public DateTime? InvalidatedAtUtc { get; set; }

    /// <summary>Wrong-code attempts made against this specific row. Compared against <c>OtpOptions.MaxVerificationAttempts</c>.</summary>
    public int AttemptCount { get; set; }

    public bool IsActive => ConsumedAtUtc is null && InvalidatedAtUtc is null && DateTime.UtcNow < ExpiresAtUtc;
}
