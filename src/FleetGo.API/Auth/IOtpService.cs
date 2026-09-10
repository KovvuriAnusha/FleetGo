namespace FleetGo.API.Auth;

/// <summary>Orchestrates requesting and verifying a one-time login code.</summary>
internal interface IOtpService
{
    /// <summary>
    /// Generates and delivers a new login OTP for the account identified by
    /// <paramref name="email"/>, if one can be issued. Always completes the same way -
    /// whether the email is unknown, the account is inactive, it has no phone number on
    /// file, or the caller is inside the resend cooldown - because none of those cases (nor
    /// success) should be distinguishable from the response (see <c>OtpEndpoints</c>).
    /// </summary>
    Task RequestOtpAsync(string email, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies <paramref name="code"/> against the active login OTP for
    /// <paramref name="email"/> and, on success, issues a normal access/refresh token pair -
    /// the same one a password login would produce.
    /// </summary>
    Task<AuthOutcome> VerifyOtpAsync(string email, string code, CancellationToken cancellationToken);
}
