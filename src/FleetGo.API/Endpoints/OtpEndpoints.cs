using FleetGo.API.Auth;
using FleetGo.API.RateLimiting;
using FleetGo.Shared;
using FleetGo.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FleetGo.API.Endpoints;

/// <summary>
/// One-time-code login: request a code, then verify it for a normal token pair. Grouped
/// and mapped the same way as <see cref="AuthEndpoints"/> - a static class per feature area -
/// and deliberately kept a thin HTTP wrapper around <see cref="IOtpService"/>, which owns
/// every actual decision (whether to send a code, whether a code is correct).
/// </summary>
internal static class OtpEndpoints
{
    private const string InvalidCodeMessage = "The code is incorrect, expired, or has already been used.";

    public static IEndpointRouteBuilder MapOtpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup(ApiRoutes.AuthOtpBase).WithTags("Auth");

        group.MapPost("/request", RequestAsync)
            .WithName("RequestOtp")
            .WithSummary("Requests a one-time login code be sent to the account's phone number on file.")
            .WithDescription(
                "Always responds the same way regardless of whether the email is known, active, " +
                "has a phone number on file, or is inside its resend cooldown - the response " +
                "never reveals which of those was the case.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiterPolicies.OtpRequest);

        group.MapPost("/verify", VerifyAsync)
            .WithName("VerifyOtp")
            .WithSummary("Exchanges a one-time code for an access and refresh token.")
            .WithDescription("Fails uniformly for a wrong code, an expired or already-used code, or an unknown/inactive account.")
            .AllowAnonymous()
            .RequireRateLimiting(RateLimiterPolicies.OtpVerify);

        return endpoints;
    }

    private static async Task<NoContent> RequestAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] RequestOtpRequest request,
        IOtpService otpService,
        CancellationToken cancellationToken)
    {
        await otpService.RequestOtpAsync(request.Email, cancellationToken);

        // Always 204, whatever IOtpService actually did - see its own documentation for why.
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> VerifyAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] VerifyOtpRequest request,
        IOtpService otpService,
        CancellationToken cancellationToken)
    {
        AuthOutcome outcome = await otpService.VerifyOtpAsync(request.Email, request.Code, cancellationToken);

        if (!outcome.Succeeded)
        {
            return TypedResults.Problem(
                detail: InvalidCodeMessage,
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed");
        }

        return TypedResults.Ok(new TokenResponse(
            outcome.AccessToken!.Value,
            outcome.AccessToken.ExpiresAtUtc,
            outcome.RefreshToken!,
            outcome.RefreshTokenExpiresAtUtc!.Value));
    }
}
