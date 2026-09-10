namespace FleetGo.API.Auth.Options;

/// <summary>
/// Bound from the <c>Otp</c> configuration section. Every OTP behaviour Phase 3 needs to
/// be able to tune - expiry, attempt limits, resend cadence, and how strictly the endpoints
/// are rate-limited - lives on this one type rather than scattered constants, so a
/// deployment can tighten or loosen the flow without a code change.
/// </summary>
public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>Digits in a generated code. 6 is the industry-standard balance of guessability vs. usability.</summary>
    public int CodeLength { get; set; } = 6;

    /// <summary>How long a code stays valid after it is issued. Kept short - a stolen code should go stale fast.</summary>
    public int ExpiryMinutes { get; set; } = 5;

    /// <summary>
    /// Wrong-code attempts allowed against a single OTP before it is locked out, even if the
    /// correct code is later supplied. Bounds how much of the code's small (10^CodeLength)
    /// search space a caller can probe.
    /// </summary>
    public int MaxVerificationAttempts { get; set; } = 5;

    /// <summary>
    /// Minimum time between two OTP requests for the same user/purpose, enforced by
    /// <see cref="OtpService"/> itself regardless of the ASP.NET Core rate limiter on the
    /// endpoint (see <see cref="RequestRateLimit"/>) - a defence against an attacker who
    /// rotates IP addresses to dodge the per-IP HTTP limiter but still can't out-run this
    /// per-account rule.
    /// </summary>
    public int ResendCooldownSeconds { get; set; } = 30;

    /// <summary>
    /// Secret key an OTP's digits are HMAC-SHA256'd with before storage (see
    /// <see cref="FleetGo.API.Data.Entities.OtpCode.CodeHash"/>). A code is only 10^CodeLength
    /// possibilities, small enough to brute-force offline in an instant from a hash alone -
    /// keying the hash with a secret never stored in the database means a leaked database on
    /// its own is not enough to recover any code, exactly as with a peppered password hash.
    /// Must never appear in a committed appsettings file - see docs/development-setup.md for
    /// the <c>dotnet user-secrets</c> command that sets it locally.
    /// </summary>
    public required string HashingKey { get; set; }

    /// <summary>Per-IP limiter policy applied to <see cref="FleetGo.Shared.ApiRoutes.AuthOtpRequest"/>.</summary>
    public OtpRateLimitOptions RequestRateLimit { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    /// <summary>Per-IP limiter policy applied to <see cref="FleetGo.Shared.ApiRoutes.AuthOtpVerify"/>.</summary>
    public OtpRateLimitOptions VerifyRateLimit { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
}

/// <summary>A fixed-window request-count limit: at most <see cref="PermitLimit"/> requests per <see cref="WindowSeconds"/>, per partition (see <c>Program.cs</c> for how the partition key is chosen).</summary>
public sealed class OtpRateLimitOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }
}
