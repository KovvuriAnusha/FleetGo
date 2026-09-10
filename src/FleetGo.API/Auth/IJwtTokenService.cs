using FleetGo.API.Data.Entities;

namespace FleetGo.API.Auth;

/// <summary>Mints signed access tokens for an authenticated user.</summary>
public interface IJwtTokenService
{
    /// <summary>
    /// Creates a short-lived JWT carrying the user's identity and, when the account has a
    /// driver profile, the driver's identifier.
    /// </summary>
    AccessToken CreateAccessToken(User user);
}

