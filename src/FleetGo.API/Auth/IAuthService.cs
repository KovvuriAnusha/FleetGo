namespace FleetGo.API.Auth;

/// <summary>Orchestrates login, refresh-token rotation and logout.</summary>
internal interface IAuthService
{
    /// <summary>
    /// Validates credentials and, on success, issues a new access/refresh token pair.
    /// Fails uniformly - same result, same timing budget as far as reasonably practical -
    /// whether the email is unknown, the password is wrong, or the account is inactive, so
    /// the API response never reveals which of those was the case.
    /// </summary>
    Task<AuthOutcome> LoginAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>
    /// Validates a refresh token and, on success, rotates it: the presented token is
    /// revoked and a new access/refresh pair is issued. Reuse of an already-revoked token -
    /// a sign the token may have been stolen and the legitimate client already rotated past
    /// it - revokes every other active refresh token on the account as a precaution.
    /// </summary>
    Task<AuthOutcome> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes a refresh token. Safe to call with an already-revoked, expired or unknown token.</summary>
    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Issues a fresh access/refresh token pair for an already-verified user, exactly as
    /// <see cref="LoginAsync"/> does after checking a password. Used by <see cref="OtpService"/>
    /// once an OTP has been verified, so a successful OTP check produces a normal session
    /// through the same code path a password login uses - not a second, parallel way of
    /// minting tokens that could drift from the real one.
    /// </summary>
    Task<AuthOutcome> IssueSessionForUserAsync(FleetGo.API.Data.Entities.User user, CancellationToken cancellationToken);
}

