using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

/// <summary>
/// Test double for the fleet half of <see cref="IFleetGoApiClient"/>, following the same
/// configurable-handler shape as <see cref="FakeAuthenticationService"/>: each call has a
/// handler that defaults to "an empty result", so a test only sets up the one it cares about.
/// <para>
/// The authentication members throw: no driver screen calls them, and a fake that silently
/// returned a plausible token would hide a view model that started doing so.
/// </para>
/// </summary>
internal sealed class FakeFleetGoApiClient : IFleetGoApiClient
{
    public Func<int, int, RouteStatus?, DateOnly?, string?, CancellationToken, Task<PagedResponse<RouteResponse>>> GetRoutesHandler { get; set; } =
        (page, pageSize, _, _, _, _) => Task.FromResult(new PagedResponse<RouteResponse>([], page, pageSize, 0));

    public Func<Guid, CancellationToken, Task<RouteResponse>> GetRouteHandler { get; set; } =
        (_, _) => Task.FromResult(FleetTestData.Route());

    public Func<Guid?, int, int, StopStatus?, string?, string?, CancellationToken, Task<PagedResponse<StopResponse>>> GetStopsHandler { get; set; } =
        (_, page, pageSize, _, _, _, _) => Task.FromResult(new PagedResponse<StopResponse>([], page, pageSize, 0));

    public Func<Guid, CancellationToken, Task<StopResponse>> GetStopHandler { get; set; } =
        (_, _) => Task.FromResult(FleetTestData.Stop());

    public Func<Guid?, int, int, PackageStatus?, string?, CancellationToken, Task<PagedResponse<PackageResponse>>> GetPackagesHandler { get; set; } =
        (_, page, pageSize, _, _, _) => Task.FromResult(new PagedResponse<PackageResponse>([], page, pageSize, 0));

    public Func<Guid, CancellationToken, Task<VehicleResponse>> GetVehicleHandler { get; set; } =
        (_, _) => Task.FromResult(FleetTestData.Vehicle());

    public Func<Guid, CancellationToken, Task<CustomerResponse>> GetCustomerHandler { get; set; } =
        (_, _) => Task.FromResult(FleetTestData.Customer());

    public Func<Guid, CancellationToken, Task<PackageResponse>> GetPackageHandler { get; set; } =
        (_, _) => Task.FromResult(FleetTestData.Package());

    // Phase 5's mutations. Each defaults to echoing the request back as the server would,
    // with UpdatedAtUtc moved on - which is exactly what a real update does, and what the
    // sync engine's conflict detection depends on noticing.
    public Func<Guid, UpdateRouteRequest, CancellationToken, Task<RouteResponse>> UpdateRouteHandler { get; set; } =
        (id, request, _) => Task.FromResult(FleetTestData.Route(
            routeNumber: request.RouteNumber,
            status: request.Status,
            id: id,
            vehicleId: request.VehicleId,
            routeDate: request.RouteDate) with { UpdatedAtUtc = DateTime.UnixEpoch.AddMinutes(1) });

    public Func<Guid, UpdateStopRequest, CancellationToken, Task<StopResponse>> UpdateStopHandler { get; set; } =
        (id, request, _) => Task.FromResult(FleetTestData.Stop(
            sequence: request.Sequence,
            status: request.Status,
            id: id,
            customerId: request.CustomerId,
            deliveryNotes: request.DeliveryNotes) with { UpdatedAtUtc = DateTime.UnixEpoch.AddMinutes(1) });

    public Func<Guid, UpdatePackageRequest, CancellationToken, Task<PackageResponse>> UpdatePackageHandler { get; set; } =
        (id, request, _) => Task.FromResult(FleetTestData.Package(
            trackingNumber: request.TrackingNumber,
            status: request.Status,
            description: request.Description,
            id: id) with { UpdatedAtUtc = DateTime.UnixEpoch.AddMinutes(1) });

    /// <summary>Every route update sent, so a test can assert what the outbox actually put on the wire.</summary>
    public List<(Guid RouteId, UpdateRouteRequest Request)> RouteUpdates { get; } = [];

    public List<(Guid StopId, UpdateStopRequest Request)> StopUpdates { get; } = [];

    public List<(Guid PackageId, UpdatePackageRequest Request)> PackageUpdates { get; } = [];

    /// <summary>Every route-list query this client was asked to make, in order - so a test can assert on paging and filtering.</summary>
    public List<RouteQuery> RouteQueries { get; } = [];

    /// <summary>Every stop-list query, for asserting that route detail scoped its request to one route.</summary>
    public List<Guid?> StopQueryRouteIds { get; } = [];

    /// <summary>Every package-list query, for asserting that stop detail scoped its request to one stop.</summary>
    public List<Guid?> PackageQueryStopIds { get; } = [];

    public static FakeFleetGoApiClient WithRoutes(params RouteResponse[] routes)
    {
        var client = new FakeFleetGoApiClient();
        client.GetRoutesHandler = (page, pageSize, _, _, _, _) =>
            Task.FromResult(new PagedResponse<RouteResponse>(routes, page, pageSize, routes.Length));
        return client;
    }

    public static FakeFleetGoApiClient ThatFailsRouteListWith(Exception exception)
    {
        var client = new FakeFleetGoApiClient();
        client.GetRoutesHandler = (_, _, _, _, _, _) => Task.FromException<PagedResponse<RouteResponse>>(exception);
        return client;
    }

    public Task<PagedResponse<RouteResponse>> GetRoutesAsync(
        int page = 1,
        int pageSize = 20,
        RouteStatus? status = null,
        DateOnly? routeDate = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        RouteQueries.Add(new RouteQuery(page, pageSize, status, routeDate, sort));
        return GetRoutesHandler(page, pageSize, status, routeDate, sort, cancellationToken);
    }

    public Task<RouteResponse> GetRouteAsync(Guid routeId, CancellationToken cancellationToken = default) =>
        GetRouteHandler(routeId, cancellationToken);

    public Task<PagedResponse<StopResponse>> GetStopsAsync(
        Guid? routeId = null,
        int page = 1,
        int pageSize = 20,
        StopStatus? status = null,
        string? search = null,
        string? sort = null,
        CancellationToken cancellationToken = default)
    {
        StopQueryRouteIds.Add(routeId);
        return GetStopsHandler(routeId, page, pageSize, status, search, sort, cancellationToken);
    }

    public Task<StopResponse> GetStopAsync(Guid stopId, CancellationToken cancellationToken = default) =>
        GetStopHandler(stopId, cancellationToken);

    public Task<PagedResponse<PackageResponse>> GetPackagesAsync(
        Guid? stopId = null,
        int page = 1,
        int pageSize = 20,
        PackageStatus? status = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        PackageQueryStopIds.Add(stopId);
        return GetPackagesHandler(stopId, page, pageSize, status, search, cancellationToken);
    }

    public Task<PackageResponse> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default) =>
        GetPackageHandler(packageId, cancellationToken);

    public Task<RouteResponse> UpdateRouteAsync(Guid routeId, UpdateRouteRequest request, CancellationToken cancellationToken = default)
    {
        RouteUpdates.Add((routeId, request));
        return UpdateRouteHandler(routeId, request, cancellationToken);
    }

    public Task<StopResponse> UpdateStopAsync(Guid stopId, UpdateStopRequest request, CancellationToken cancellationToken = default)
    {
        StopUpdates.Add((stopId, request));
        return UpdateStopHandler(stopId, request, cancellationToken);
    }

    public Task<PackageResponse> UpdatePackageAsync(Guid packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default)
    {
        PackageUpdates.Add((packageId, request));
        return UpdatePackageHandler(packageId, request, cancellationToken);
    }

    public Task<VehicleResponse> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default) =>
        GetVehicleHandler(vehicleId, cancellationToken);

    public Task<CustomerResponse> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        GetCustomerHandler(customerId, cancellationToken);

    // --- Not used by any driver screen -----------------------------------------------
    public Task<ApiInfoResponse> GetApiInfoAsync(CancellationToken cancellationToken = default) => throw NotUsed();

    public Task<HealthReportResponse> GetHealthAsync(CancellationToken cancellationToken = default) => throw NotUsed();

    public Task<TokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) => throw NotUsed();

    public Task<TokenResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default) => throw NotUsed();

    public Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default) => throw NotUsed();

    public Task<CurrentUserResponse> GetCurrentUserAsync(CancellationToken cancellationToken = default) => throw NotUsed();

    public Task RequestOtpAsync(RequestOtpRequest request, CancellationToken cancellationToken = default) => throw NotUsed();

    public Task<TokenResponse> VerifyOtpAsync(VerifyOtpRequest request, CancellationToken cancellationToken = default) => throw NotUsed();

    private static NotSupportedException NotUsed([System.Runtime.CompilerServices.CallerMemberName] string? member = null) =>
        new($"{member} is not part of the driver screens and should not have been called.");

    /// <summary>One recorded route-list request.</summary>
    internal sealed record RouteQuery(int Page, int PageSize, RouteStatus? Status, DateOnly? RouteDate, string? Sort);
}
