using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Contracts.Fleet;

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

    // ---------------------------------------------------------------------
    // Fleet operations (Phase 4A). All of these require an authenticated caller;
    // BearerTokenHandler attaches the access token, and the endpoints scope every
    // result to the caller's own driver profile server-side - there is deliberately
    // no driverId parameter here to pass (or to get wrong).
    // ---------------------------------------------------------------------

    /// <summary>
    /// Lists the authenticated driver's routes, newest page first.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page; the API caps this at 100.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="routeDate">Optional exact-date filter.</param>
    /// <param name="sort">Optional sort key: routeDate, status or routeNumber, prefixed with '-' for descending.</param>
    Task<PagedResponse<RouteResponse>> GetRoutesAsync(
        int page = 1,
        int pageSize = 20,
        RouteStatus? status = null,
        DateOnly? routeDate = null,
        string? sort = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one of the caller's own routes.</summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with status <c>404 Not Found</c> when the route does not exist <em>or</em>
    /// belongs to another driver - the API does not distinguish the two.
    /// </exception>
    Task<RouteResponse> GetRouteAsync(Guid routeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists stops across the caller's routes, or on one route when
    /// <paramref name="routeId"/> is supplied.
    /// </summary>
    Task<PagedResponse<StopResponse>> GetStopsAsync(
        Guid? routeId = null,
        int page = 1,
        int pageSize = 20,
        StopStatus? status = null,
        string? search = null,
        string? sort = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one stop on one of the caller's own routes.</summary>
    Task<StopResponse> GetStopAsync(Guid stopId, CancellationToken cancellationToken = default);

    /// <summary>Lists packages across the caller's stops, or on one stop when <paramref name="stopId"/> is supplied.</summary>
    Task<PagedResponse<PackageResponse>> GetPackagesAsync(
        Guid? stopId = null,
        int page = 1,
        int pageSize = 20,
        PackageStatus? status = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one package at one of the caller's own stops.</summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with status <c>404 Not Found</c> when the package does not exist or sits on
    /// another driver's route - the API does not distinguish the two.
    /// </exception>
    Task<PackageResponse> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default);

    /// <summary>Reads one fleet vehicle. Vehicles are shared fleet data, not per-driver.</summary>
    Task<VehicleResponse> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);

    /// <summary>Reads one customer. Customers are shared reference data, not per-driver.</summary>
    Task<CustomerResponse> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------------
    // Mutations (Phase 5). Added because the offline outbox has to have something real
    // to send: a queued change that no endpoint accepts is not offline support, it is a
    // queue that never drains.
    //
    // All three are full replacements, mirroring the endpoints the API already exposed
    // in Phase 4A - PUT semantics, every editable field supplied, no partial update and
    // no concurrency token. That matters to the sync engine: it cannot send "just the
    // status", so it has to compose the rest of the request from server state it has
    // just read. See OutboxSender for how that is done, and docs/architecture.md for
    // the concurrency limitation it leaves behind.
    // ---------------------------------------------------------------------

    /// <summary>Replaces the editable fields of one of the caller's own routes.</summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with status <c>404 Not Found</c> when the route does not exist or belongs to
    /// another driver, <c>400 Bad Request</c> when the request fails validation, and
    /// <c>409 Conflict</c> when the route number is already taken.
    /// </exception>
    Task<RouteResponse> UpdateRouteAsync(Guid routeId, UpdateRouteRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replaces the editable fields of one stop on one of the caller's own routes.</summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with status <c>404 Not Found</c>, <c>400 Bad Request</c>, or <c>409 Conflict</c>
    /// when another stop already holds the requested sequence number.
    /// </exception>
    Task<StopResponse> UpdateStopAsync(Guid stopId, UpdateStopRequest request, CancellationToken cancellationToken = default);

    /// <summary>Replaces the editable fields of one package at one of the caller's own stops.</summary>
    /// <exception cref="FleetGoApiException">
    /// Thrown with status <c>404 Not Found</c>, <c>400 Bad Request</c>, or <c>409 Conflict</c>
    /// when the tracking number is already taken.
    /// </exception>
    Task<PackageResponse> UpdatePackageAsync(Guid packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default);
}
