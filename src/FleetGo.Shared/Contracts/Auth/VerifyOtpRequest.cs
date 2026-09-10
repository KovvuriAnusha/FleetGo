namespace FleetGo.Shared.Contracts.Auth;

/// <summary>Request body for <see cref="ApiRoutes.AuthOtpVerify"/>.</summary>
/// <param name="Email">The same email address passed to <see cref="ApiRoutes.AuthOtpRequest"/>.</param>
/// <param name="Code">The one-time code as delivered - digits only.</param>
public sealed record VerifyOtpRequest(string Email, string Code);
