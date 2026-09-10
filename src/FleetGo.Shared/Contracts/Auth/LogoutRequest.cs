namespace FleetGo.Shared.Contracts.Auth;

/// <summary>Request body for <see cref="ApiRoutes.AuthLogout"/>.</summary>
/// <param name="RefreshToken">
/// The refresh token to revoke. Logout only needs to invalidate this one session; the
/// caller's other signed-in devices, each holding their own refresh token, are unaffected.
/// </param>
public sealed record LogoutRequest(string RefreshToken);

