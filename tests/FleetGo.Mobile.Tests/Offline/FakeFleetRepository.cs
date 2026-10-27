using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// Test double for <see cref="IFleetRepository"/>, in the same configurable-handler shape as
/// <see cref="FakeFleetGoApiClient"/>.
/// <para>
/// The view model tests use this rather than a real store because what they are about is the
/// view model: which data lands on which property, what happens when nothing is cached, and
/// whether a refusal reaches the driver. Whether SQLite got the SQL right is a separate
/// question, answered separately by <see cref="SqliteLocalFleetStoreTests"/> and
/// <see cref="FleetRepositoryTests"/> against the real thing.
/// </para>
/// </summary>
internal sealed class FakeFleetRepository : IFleetRepository
{
    public Func<DateOnly, CancellationToken, Task<CachedData<IReadOnlyList<RouteResponse>>>> RoutesForDateHandler { get; set; } =
        (_, _) => Task.FromResult(new CachedData<IReadOnlyList<RouteResponse>>([], null, false));

    public Func<RouteStatus?, int, int, CancellationToken, Task<CachedData<RoutePage>>> RoutesHandler { get; set; } =
        (_, _, _, _) => Task.FromResult(new CachedData<RoutePage>(new RoutePage([], 0), null, false));

    public Func<Guid, CancellationToken, Task<CachedData<RouteDetail>>> RouteDetailHandler { get; set; } =
        (_, _) => Task.FromResult(new CachedData<RouteDetail>(new RouteDetail(null, [], null), null, false));

    public Func<Guid, CancellationToken, Task<CachedData<StopDetail>>> StopDetailHandler { get; set; } =
        (_, _) => Task.FromResult(new CachedData<StopDetail>(new StopDetail(null, [], null), null, false));

    public Func<Guid, StopStatus, CancellationToken, Task<QueuedChangeResult>> QueueStopStatusHandler { get; set; } =
        (_, _, _) => Task.FromResult(new QueuedChangeResult(true, Guid.NewGuid()));

    public List<OutboxOperation> UnresolvedChanges { get; } = [];

    /// <summary>Every queued stop status change, so a test can assert the driver's tap reached the queue.</summary>
    public List<(Guid StopId, StopStatus Status)> QueuedStopStatuses { get; } = [];

    public List<Guid> DiscardedOperationIds { get; } = [];

    public List<Guid> RefreshedRouteIds { get; } = [];

    public List<Guid> RefreshedStopIds { get; } = [];

    public int RouteWindowRefreshCount { get; private set; }

    public int ClearCount { get; private set; }

    public Guid? PreparedDriverId { get; private set; }

    /// <summary>Set to make a refresh fail the way a dead network does.</summary>
    public Exception? RefreshException { get; set; }

    public Task<bool> PrepareForDriverAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        PreparedDriverId = driverId;
        return Task.FromResult(false);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++;
        return Task.CompletedTask;
    }

    public Task<CachedData<IReadOnlyList<RouteResponse>>> GetRoutesForDateAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        RoutesForDateHandler(date, cancellationToken);

    public Task<CachedData<RoutePage>> GetRoutesAsync(RouteStatus? status, int skip, int take, CancellationToken cancellationToken = default) =>
        RoutesHandler(status, skip, take, cancellationToken);

    public Task<CachedData<RouteDetail>> GetRouteDetailAsync(Guid routeId, CancellationToken cancellationToken = default) =>
        RouteDetailHandler(routeId, cancellationToken);

    public Task<CachedData<StopDetail>> GetStopDetailAsync(Guid stopId, CancellationToken cancellationToken = default) =>
        StopDetailHandler(stopId, cancellationToken);

    public Task RefreshRouteWindowAsync(CancellationToken cancellationToken = default)
    {
        RouteWindowRefreshCount++;
        return RefreshException is null ? Task.CompletedTask : Task.FromException(RefreshException);
    }

    public Task RefreshRouteDetailAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        RefreshedRouteIds.Add(routeId);
        return RefreshException is null ? Task.CompletedTask : Task.FromException(RefreshException);
    }

    public Task RefreshStopDetailAsync(Guid stopId, CancellationToken cancellationToken = default)
    {
        RefreshedStopIds.Add(stopId);
        return RefreshException is null ? Task.CompletedTask : Task.FromException(RefreshException);
    }

    public Task<QueuedChangeResult> QueueStopStatusAsync(Guid stopId, StopStatus newStatus, CancellationToken cancellationToken = default)
    {
        QueuedStopStatuses.Add((stopId, newStatus));
        return QueueStopStatusHandler(stopId, newStatus, cancellationToken);
    }

    public Task<QueuedChangeResult> QueueRouteStatusAsync(Guid routeId, RouteStatus newStatus, CancellationToken cancellationToken = default) =>
        Task.FromResult(new QueuedChangeResult(true, Guid.NewGuid()));

    public Task<QueuedChangeResult> QueuePackageStatusAsync(Guid packageId, PackageStatus newStatus, CancellationToken cancellationToken = default) =>
        Task.FromResult(new QueuedChangeResult(true, Guid.NewGuid()));

    public Task DiscardQueuedChangeAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        DiscardedOperationIds.Add(operationId);
        UnresolvedChanges.RemoveAll(operation => operation.Id == operationId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxOperation>> GetUnresolvedChangesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OutboxOperation>>([.. UnresolvedChanges]);
}
