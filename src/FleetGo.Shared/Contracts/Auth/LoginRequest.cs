namespace FleetGo.Shared.Contracts.Auth;

/// <summary>Credentials submitted to <see cref="ApiRoutes.AuthLogin"/>.</summary>
/// <param name="Email">The account's email address. Compared case-insensitively.</param>
/// <param name="Password">The account's plaintext password, sent once over HTTPS and never stored.</param>
public sealed record LoginRequest(string Email, string Password);

