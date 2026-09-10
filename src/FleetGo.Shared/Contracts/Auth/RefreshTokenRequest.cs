namespace FleetGo.Shared.Contracts.Auth;

/// <summary>Request body for <see cref="ApiRoutes.AuthRefresh"/>.</summary>
/// <param name="RefreshToken">The refresh token issued by the most recent login or refresh.</param>
public sealed record RefreshTokenRequest(string RefreshToken);

