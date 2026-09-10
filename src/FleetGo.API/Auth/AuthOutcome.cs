namespace FleetGo.API.Auth;

/// <summary>
/// Result of a login or refresh attempt. A plain result object rather than an exception:
/// "wrong password" and "unknown refresh token" are expected, everyday outcomes for these
/// operations, not exceptional ones, so the endpoint checks <see cref="Succeeded"/> instead
/// of catching a control-flow exception.
/// </summary>
internal sealed record AuthOutcome(
    bool Succeeded,
    AccessToken? AccessToken,
    string? RefreshToken,
    DateTime? RefreshTokenExpiresAtUtc)
{
    public static AuthOutcome Failed { get; } = new(false, null, null, null);

    public static AuthOutcome Success(AccessToken accessToken, string refreshToken, DateTime refreshTokenExpiresAtUtc) =>
        new(true, accessToken, refreshToken, refreshTokenExpiresAtUtc);
}

