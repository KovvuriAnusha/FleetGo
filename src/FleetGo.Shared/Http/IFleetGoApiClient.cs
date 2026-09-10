using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Auth;

namespace FleetGo.Shared.Http;

/// <summary>
/// Typed client for the FleetGo API. Consumers depend on this interface rather
/// than on <see cref="HttpClient"/>, which keeps view models unit-testable.
/// </summary>
public interface IFleetGoApiClient
{
    /// <summary>Reads metadata about the API instance the app is pointed at.</summary>
    Task<ApiInfoResponse> GetApiInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the API health report. A degraded or unhealthy API still returns a
    /// report (HTTP 503), so this only throws when the response is unusable.
    /// </summary>
    Task<HealthReportResponse> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges credentials for a token pair.
    /// </summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with <see cref="FleetGoApiException.StatusCode"/> of <c>401 Unauthorized</c>
    /// when the email/password combination is not valid, or the account is not active.
    /// The message is intentionally generic on the server side so this exception alone
    /// never reveals which of those was the case.
    /// </exception>
    Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for a new token pair. The server rotates the refresh
    /// token on every call: the one returned here replaces <paramref name="request"/>'s
    /// token, which becomes unusable the moment this call succeeds.
    /// </summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with status <c>401 Unauthorized</c> when the refresh token is unknown,
    /// expired, or already revoked.
    /// </exception>
    Task<TokenResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a refresh token. Idempotent: revoking a token that is already revoked,
    /// expired, or unknown still succeeds, since the caller's goal - "this token must not
    /// work anymore" - is already true.
    /// </summary>
    Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the profile of the currently authenticated user. Requires a valid access
    /// token; <see cref="BearerTokenHandler"/> attaches it automatically when one is
    /// available.
    /// </summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with status <c>401 Unauthorized</c> when there is no valid access token.
    /// </exception>
    Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a one-time login code. Always completes successfully - the server responds
    /// the same way whether or not the email is real (see <c>ApiRoutes.AuthOtpRequest</c>),
    /// so there is no "unknown account" exception to catch here.
    /// </summary>
    Task RequestOtpAsync(RequestOtpRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a one-time code for a token pair, exactly as <see cref="LoginAsync"/> does
    /// for a password.
    /// </summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with status <c>401 Unauthorized</c> when the code is wrong, expired,
    /// already used, or the account is not eligible.
    /// </exception>
    Task<TokenResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default);
}

