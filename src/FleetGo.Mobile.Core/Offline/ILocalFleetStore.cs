using FleetGo.Mobile.Core.Offline.Models;

namespace FleetGo.Mobile.Core.Offline;

/// <summary>
/// The device's copy of the driver's working day, and the durable queue of changes waiting to
/// reach the server.
/// <para>
/// View models never see SQLite through this: they see records and operations. That is what
/// lets the read path be tested without a device and, if the storage engine ever changes,
/// keeps the change inside one class.
/// </para>
/// </summary>
public interface ILocalFleetStore
{
    /// <summary>
    /// Opens the database and creates anything missing. Safe to call repeatedly and from
    /// several callers at once - the first call does the work and the rest wait for it.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds the cache to one driver. If it currently holds another driver's data - a second
    /// driver signing in on a shared device - everything is deleted first. This is the
    /// data-isolation guard; see <see cref="SyncMetadata.OwnerDriverId"/>.
    /// </summary>
    /// <returns>True when existing data was cleared because the owner changed.</returns>
    Task<bool> EnsureOwnedByAsync(Guid driverId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every cached record and queued operation. Used on sign-out.</summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);

    // --- writes from a sync ----------------------------------------------------------

    /// <summary>
    /// Replaces the cached routes wholesale, so routes the server no longer returns stop
    /// being shown. Runs in one transaction: a sync interrupted halfway leaves the previous
    /// cache intact rather than a half-empty one.
    /// </summary>
    Task ReplaceRoutesAsync(IReadOnlyList<LocalRoute> routes, CancellationToken cancellationToken = default);

    /// <summary>Replaces the cached stops for one route, in one transaction.</summary>
    Task ReplaceStopsForRouteAsync(Guid routeId, IReadOnlyList<LocalStop> stops, CancellationToken cancellationToken = default);

    /// <summary>Replaces the cached packages for one stop, in one transaction.</summary>
    Task ReplacePackagesForStopAsync(Guid stopId, IReadOnlyList<LocalPackage> packages, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates one route. Used when a single record is refreshed on its own -
    /// opening a route the cache has never seen, or writing back what the server returned
    /// from an update - where replacing the whole table would throw away everything else.
    /// </summary>
    Task UpsertRouteAsync(LocalRoute route, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates one stop. See <see cref="UpsertRouteAsync"/>.</summary>
    Task UpsertStopAsync(LocalStop stop, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates one package. See <see cref="UpsertRouteAsync"/>.</summary>
    Task UpsertPackageAsync(LocalPackage package, CancellationToken cancellationToken = default);

    Task UpsertVehicleAsync(LocalVehicle vehicle, CancellationToken cancellationToken = default);

    Task UpsertCustomerAsync(LocalCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>Applies a status change locally so the UI reflects it immediately, before the server has seen it.</summary>
    Task ApplyLocalStatusAsync(OutboxEntityType entityType, Guid entityId, string status, CancellationToken cancellationToken = default);

    // --- reads -----------------------------------------------------------------------

    /// <summary>The driver's routes, newest date first, optionally filtered by status name.</summary>
    Task<IReadOnlyList<LocalRoute>> GetRoutesAsync(string? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>How many routes match <paramref name="status"/>, so the list can page without loading everything.</summary>
    Task<int> CountRoutesAsync(string? status, CancellationToken cancellationToken = default);

    /// <summary>Routes for one calendar day (ISO yyyy-MM-dd), for the dashboard.</summary>
    Task<IReadOnlyList<LocalRoute>> GetRoutesForDateAsync(string routeDate, CancellationToken cancellationToken = default);

    Task<LocalRoute?> GetRouteAsync(Guid routeId, CancellationToken cancellationToken = default);

    /// <summary>One route's stops, in running order.</summary>
    Task<IReadOnlyList<LocalStop>> GetStopsForRouteAsync(Guid routeId, CancellationToken cancellationToken = default);

    Task<LocalStop?> GetStopAsync(Guid stopId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalPackage>> GetPackagesForStopAsync(Guid stopId, CancellationToken cancellationToken = default);

    Task<LocalPackage?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default);

    Task<LocalVehicle?> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default);

    Task<LocalCustomer?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);

    // --- outbox ----------------------------------------------------------------------

    /// <summary>Queues an operation and marks the entity as having an unsent change.</summary>
    Task EnqueueAsync(OutboxOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// The operation still waiting to be sent for this exact record and kind of change, if
    /// there is one.
    /// <para>
    /// This is what lets a second status change on the same stop replace the first rather
    /// than stack behind it: a driver who taps Arrived and then Completed before any signal
    /// returns owes the server one update, not two, and the queue should say so. It also
    /// keeps the queue bounded by how many records the driver touched rather than by how
    /// many times they tapped.
    /// </para>
    /// </summary>
    Task<OutboxOperation?> FindPendingOperationAsync(
        OutboxEntityType entityType,
        Guid entityId,
        OutboxOperationType operationType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one operation outright. Used when a driver discards a change that ended in
    /// conflict - the record then simply reverts to whatever the server says on the next sync.
    /// </summary>
    Task DeleteOperationAsync(Guid operationId, CancellationToken cancellationToken = default);

    /// <summary>How many operations are in one state. The caller uses this to refuse to queue past a sane ceiling.</summary>
    Task<int> CountOperationsAsync(OutboxOperationStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Operations ready to attempt now: still pending, and past any backoff delay. Ordered by
    /// <see cref="OutboxOperation.Sequence"/>, so they are sent in the order they were made.
    /// </summary>
    Task<IReadOnlyList<OutboxOperation>> GetSendableOperationsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>Everything not yet settled - pending, failed or in conflict - for the UI to count and show.</summary>
    Task<IReadOnlyList<OutboxOperation>> GetUnresolvedOperationsAsync(CancellationToken cancellationToken = default);

    Task UpdateOperationAsync(OutboxOperation operation, CancellationToken cancellationToken = default);

    /// <summary>Drops settled operations older than <paramref name="completedBeforeUtc"/> so the queue cannot grow without bound.</summary>
    Task<int> PruneCompletedOperationsAsync(DateTime completedBeforeUtc, CancellationToken cancellationToken = default);

    // --- metadata --------------------------------------------------------------------

    Task<SyncMetadata> GetMetadataAsync(CancellationToken cancellationToken = default);

    Task SaveMetadataAsync(SyncMetadata metadata, CancellationToken cancellationToken = default);
}
