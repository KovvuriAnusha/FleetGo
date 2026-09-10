using System.Security.Claims;
using FleetGo.API.Auth;
using FleetGo.API.Data;
using FleetGo.Shared;
using FleetGo.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FleetGo.API.Endpoints;

/// <summary>
/// Authentication endpoints: login, refresh-token rotation, logout, and the current
/// user's profile. Grouped the same way as <see cref="SystemEndpoints"/> - one static
/// class per feature area, mapped from <c>Program.cs</c>.
/// </summary>
internal static class AuthEndpoints
{
    /// <summary>Generic failure message for login and refresh. Deliberately never says which check failed.</summary>
    private const string InvalidCredentialsMessage = "The email or password is incorrect, or the account is not active.";

    private const string InvalidRefreshTokenMessage = "The refresh token is invalid, expired, or has already been used.";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup(ApiRoutes.AuthBase).WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Exchanges an email/password pair for an access and refresh token.")
            .WithDescription("Fails uniformly for an unknown email, a wrong password, or an inactive account.")
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshToken")
            .WithSummary("Exchanges a refresh token for a new access and refresh token pair.")
            .WithDescription("Rotates the refresh token: the presented token is revoked and cannot be reused.")
            .AllowAnonymous();

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Revokes a refresh token.")
            .WithDescription("Idempotent - revoking an already-revoked, expired or unknown token still succeeds.")
            .AllowAnonymous();

        group.MapGet("/me", GetCurrentUserAsync)
            .WithName("GetCurrentUser")
            .WithSummary("Returns the profile of the currently authenticated user.")
            .RequireAuthorization();

        return endpoints;
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        [FromBody] LoginRequest request,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        AuthOutcome outcome = await authService.LoginAsync(request.Email, request.Password, cancellationToken);

        return outcome.Succeeded
            ? TypedResults.Ok(ToTokenResponse(outcome))
            : Unauthorized(InvalidCredentialsMessage);
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> RefreshAsync(
        [FromBody] RefreshTokenRequest request,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        AuthOutcome outcome = await authService.RefreshAsync(request.RefreshToken, cancellationToken);

        return outcome.Succeeded
            ? TypedResults.Ok(ToTokenResponse(outcome))
            : Unauthorized(InvalidRefreshTokenMessage);
    }

    private static async Task<NoContent> LogoutAsync(
        [FromBody] LogoutRequest request,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        await authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CurrentUserResponse>, NotFound>> GetCurrentUserAsync(
        ClaimsPrincipal user,
        FleetGoDbContext db,
        CancellationToken cancellationToken)
    {
        // JwtBearer is configured with MapInboundClaims = false (see Program.cs), so the
        // "sub" claim survives under its original JWT name instead of being remapped to
        // the legacy ClaimTypes.NameIdentifier URI. The fallback keeps this working even
        // if that option is ever removed.
        Guid userId = Guid.Parse(user.FindFirstValue("sub")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The access token did not contain a subject claim."));

        var projection = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FirstName,
                u.LastName,
                DriverId = u.Driver != null ? u.Driver.Id : (Guid?)null,
                DriverCode = u.Driver != null ? u.Driver.DriverCode : null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (projection is null)
        {
            // The token was valid but the account behind it is gone - a very narrow race
            // (deleted between token issue and this call), surfaced honestly rather than
            // as a misleading 401.
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new CurrentUserResponse(
            projection.Id,
            projection.Email,
            projection.FirstName,
            projection.LastName,
            projection.DriverId,
            projection.DriverCode));
    }

    private static ProblemHttpResult Unauthorized(string detail) =>
        TypedResults.Problem(
            detail: detail,
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication failed");

    private static TokenResponse ToTokenResponse(AuthOutcome outcome) =>
        new(
            outcome.AccessToken!.Value,
            outcome.AccessToken.ExpiresAtUtc,
            outcome.RefreshToken!,
            outcome.RefreshTokenExpiresAtUtc!.Value);
}

