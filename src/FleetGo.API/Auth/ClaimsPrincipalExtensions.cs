using System.Security.Claims;

namespace FleetGo.API.Auth;

/// <summary>
/// Helpers for pulling FleetGo-specific identity out of the authenticated caller's
/// <see cref="ClaimsPrincipal"/>, so every endpoint reads claims the same way instead of
/// re-deriving the same parsing/fallback logic per file.
/// </summary>
internal static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The authenticated user's id (the access token's "sub" claim). Every caller of this
    /// method sits behind <c>RequireAuthorization()</c>, so the claim is always present in
    /// practice; this throws rather than silently returning a default if that invariant is
    /// ever broken.
    /// </summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        // JwtBearer is configured with MapInboundClaims = false (see Program.cs), so the
        // "sub" claim survives under its original JWT name instead of being remapped to the
        // legacy ClaimTypes.NameIdentifier URI. The fallback keeps this working even if that
        // option is ever removed - mirrors AuthEndpoints.GetCurrentUserAsync.
        return Guid.Parse(user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The access token did not contain a subject claim."));
    }

    /// <summary>
    /// The authenticated caller's driver id, or null when the account has no driver profile
    /// (e.g. future dispatch/back-office staff). Fleet endpoints that require a driver
    /// identity should treat null as "forbidden", not "not found" - the caller is
    /// authenticated, just not authorized for driver-only operations.
    /// </summary>
    public static Guid? GetDriverId(this ClaimsPrincipal user)
    {
        string? value = user.FindFirstValue(FleetGoClaimTypes.DriverId);
        return value is null ? null : Guid.Parse(value);
    }
}
