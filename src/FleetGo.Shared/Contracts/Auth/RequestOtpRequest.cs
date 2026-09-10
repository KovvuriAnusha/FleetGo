namespace FleetGo.Shared.Contracts.Auth;

/// <summary>Request body for <see cref="ApiRoutes.AuthOtpRequest"/>.</summary>
/// <param name="Email">
/// The account's email address - the same identifier a password login uses. The server
/// resolves this to the driver's phone number on file; the client never supplies a phone
/// number directly, so it cannot be used to send a code to a number not already on the account.
/// </param>
public sealed record RequestOtpRequest(string Email);
