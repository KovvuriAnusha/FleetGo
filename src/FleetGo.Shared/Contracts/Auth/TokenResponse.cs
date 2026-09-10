namespace FleetGo.Shared.Contracts.Auth;

/// <summary>
/// A fresh access/refresh token pair. Returned by both <see cref="ApiRoutes.AuthLogin"/>
/// and <see cref="ApiRoutes.AuthRefresh"/> - a refresh rotates the token, so the shape of
/// what comes back is identical either way.
/// </summary>
/// <param name="AccessToken">
/// Short-lived JWT sent as a <c>Bearer</c> token on subsequent authenticated requests.
/// </param>
/// <param name="AccessTokenExpiresAtUtc">When <paramref name="AccessToken"/> stops being valid.</param>
/// <param name="RefreshToken">
/// Long-lived, single-use opaque token. Exchange it at <see cref="ApiRoutes.AuthRefresh"/>
/// for a new pair before (or after) the access token expires. Store it as securely as a
/// password - anyone holding it can mint new access tokens for this account.
/// </param>
/// <param name="RefreshTokenExpiresAtUtc">When <paramref name="RefreshToken"/> stops being valid.</param>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

