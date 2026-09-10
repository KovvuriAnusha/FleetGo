namespace FleetGo.Shared.Contracts.Auth;

/// <summary>
/// Safe-to-display profile of the authenticated caller, returned by <see cref="ApiRoutes.AuthMe"/>.
/// Deliberately excludes the password hash and anything else that should never leave the server -
/// this is a view built for the client, not the <c>User</c> entity serialised as-is.
/// </summary>
/// <param name="UserId">Stable identifier for the account.</param>
/// <param name="Email">The account's email address.</param>
/// <param name="FirstName">The account holder's first name.</param>
/// <param name="LastName">The account holder's last name.</param>
/// <param name="DriverId">
/// Identifier of the linked driver profile, when this account is a driver. Null for an
/// account with no driver profile (e.g. dispatch/back-office staff in a later phase).
/// </param>
/// <param name="DriverCode">The driver's short code, when <paramref name="DriverId"/> is set.</param>
public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    Guid? DriverId,
    string? DriverCode);

