using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Core.Offline;

/// <summary>A value read out of the local cache, together with how current it is.</summary>
/// <param name="Value">The data itself.</param>
/// <param name="SyncedAtUtc">
/// When this was last heard from the server. Null when the cache had nothing - which is not
/// the same as "the server has nothing", and the screens say so differently.
/// </param>
/// <param name="HasPendingLocalChange">True when something here is still waiting to be sent.</param>
public sealed record CachedData<T>(T Value, DateTime? SyncedAtUtc, bool HasPendingLocalChange);

/// <summary>One page of routes out of the cache.</summary>
/// <param name="Items">The routes on this page.</param>
/// <param name="TotalCount">
/// How many routes the cache holds for this filter - not how many exist on the server. The
/// list is paging over what has been synced, so this is the honest ceiling.
/// </param>
public sealed record RoutePage(IReadOnlyList<RouteResponse> Items, int TotalCount);

/// <summary>Everything the route detail screen shows.</summary>
/// <param name="Route">Null when this route is not in the cache and could not be fetched.</param>
public sealed record RouteDetail(RouteResponse? Route, IReadOnlyList<StopResponse> Stops, VehicleResponse? Vehicle);

/// <summary>Everything the stop detail screen shows.</summary>
/// <param name="Stop">Null when this stop is not in the cache and could not be fetched.</param>
public sealed record StopDetail(StopResponse? Stop, IReadOnlyList<PackageResponse> Packages, CustomerResponse? Customer);

/// <summary>The result of asking to change something while possibly offline.</summary>
/// <param name="Queued">False when the change could not be accepted at all.</param>
/// <param name="OperationId">The queued operation, when one was created or updated.</param>
/// <param name="Message">Why it was refused, when it was. Null on success.</param>
public sealed record QueuedChangeResult(bool Queued, Guid OperationId, string? Message = null)
{
    public static QueuedChangeResult Refused(string message) => new(false, Guid.Empty, message);
}

/// <summary>
/// The fleet data the driver screens read and write, offline or not.
/// <para>
/// The rule this interface exists to enforce is a single sentence: <b>screens read from the
/// device, never from the network</b>. Every read below answers from SQLite. The network is
/// touched in exactly two places - the Refresh methods, which the sync engine and
/// pull-to-refresh drive, and a one-off hydration when a screen asks for something the cache
/// has never held and a network happens to be available.
/// </para>
/// <para>
/// That is what makes the app usable in a basement car park: there is no "loading from the
/// API" path to fail, because there is no such path. It also means the freshness question is
/// explicit rather than implied - hence <see cref="CachedData{T}"/> carrying the timestamp
/// rather than the screens quietly assuming what they have is current.
/// </para>
/// </summary>
public interface IFleetRepository
{
    /// <summary>
    /// Binds the cache to the signed-in driver, wiping it first if it belongs to somebody
    /// else. Called when a session starts; see <c>SyncCoordinator</c>.
    /// </summary>
    /// <returns>True when another driver's data was cleared.</returns>
    Task<bool> PrepareForDriverAsync(Guid driverId, CancellationToken cancellationToken = default);

    /// <summary>Wipes every cached record and queued change. Called on sign-out.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    // --- reads (device only) ----------------------------------------------------------

    /// <summary>The driver's routes for one day, for the dashboard.</summary>
    Task<CachedData<IReadOnlyList<RouteResponse>>> GetRoutesForDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>A page of the driver's cached routes, newest day first.</summary>
    Task<CachedData<RoutePage>> GetRoutesAsync(RouteStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>One route with its stops and assigned vehicle.</summary>
    Task<CachedData<RouteDetail>> GetRouteDetailAsync(Guid routeId, CancellationToken cancellationToken = default);

    /// <summary>One stop with its packages and customer.</summary>
    Task<CachedData<StopDetail>> GetStopDetailAsync(Guid stopId, CancellationToken cancellationToken = default);

    // --- refresh (network) ------------------------------------------------------------

    /// <summary>
    /// Pulls the driver's recent routes into the cache, replacing what was there, then puts
    /// any unsent local status changes back on top.
    /// </summary>
    Task RefreshRouteWindowAsync(CancellationToken cancellationToken = default);

    /// <summary>Pulls one route, its stops and its vehicle into the cache.</summary>
    Task RefreshRouteDetailAsync(Guid routeId, CancellationToken cancellationToken = default);

    /// <summary>Pulls one stop, its packages and its customer into the cache.</summary>
    Task RefreshStopDetailAsync(Guid stopId, CancellationToken cancellationToken = default);

    // --- writes -----------------------------------------------------------------------

    /// <summary>
    /// Records a stop status change: applied to the cache immediately so the driver sees it,
    /// and queued for the server.
    /// </summary>
    Task<QueuedChangeResult> QueueStopStatusAsync(Guid stopId, StopStatus newStatus, CancellationToken cancellationToken = default);

    /// <summary>Records a route status change. See <see cref="QueueStopStatusAsync"/>.</summary>
    Task<QueuedChangeResult> QueueRouteStatusAsync(Guid routeId, RouteStatus newStatus, CancellationToken cancellationToken = default);

    /// <summary>Records a package status change. See <see cref="QueueStopStatusAsync"/>.</summary>
    Task<QueuedChangeResult> QueuePackageStatusAsync(Guid packageId, PackageStatus newStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops a queued change the driver has decided not to keep - the usual way out of a
    /// conflict. The record goes back to whatever the server says at the next refresh.
    /// </summary>
    Task DiscardQueuedChangeAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>Everything still waiting, stuck, or in conflict, for the UI to show.</summary>
    Task<IReadOnlyList<OutboxOperation>> GetUnresolvedChangesAsync(CancellationToken cancellationToken = default);
}
