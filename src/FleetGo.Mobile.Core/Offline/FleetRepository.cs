using FleetGo.Mobile.Core.Offline.Connectivity;
using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.Session;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;
using Microsoft.Extensions.Logging;

namespace FleetGo.Mobile.Core.Offline;

/// <summary>
/// Default <see cref="IFleetRepository"/>: SQLite for reads, the API for refreshes, and the
/// outbox for anything the driver changes.
/// </summary>
/// <remarks>
/// <para>
/// One invariant holds this together and is worth stating before reading any of the code
/// below: <b>a cached row carries the server's value, unless a still-pending outbox operation
/// is overlaying it.</b> Refreshes write server state and then re-apply pending changes on
/// top; a conflict or a permanent failure causes the sync engine to write server state back
/// over the driver's rejected value. That is why <see cref="EntitySnapshot.FromLocalStop"/>
/// and friends can be trusted as a baseline whenever there is no pending operation, and why
/// a second change to the same record reuses the first one's baseline instead of taking a new
/// one from a row it has already edited.
/// </para>
/// </remarks>
public sealed class FleetRepository : IFleetRepository
{
    /// <summary>
    /// How much history a sync keeps on the device: three pages of twenty, newest first.
    /// <para>
    /// A bounded window rather than "everything" because the API has no delta endpoint - no
    /// <c>?modifiedSince=</c>, no change feed - so a sync is a full re-read of whatever it
    /// covers. Sixty routes is comfortably more than a driver's recent history and small
    /// enough to pull over a bad connection. Extending it is a number change here; making it
    /// incremental needs the API to grow a delta parameter first (see docs/architecture.md).
    /// </para>
    /// </summary>
    private const int RouteWindowPages = 3;

    private const int RoutePageSize = 20;

    /// <summary>A route's stops and a stop's packages each fit one page many times over.</summary>
    private const int ChildPageSize = 100;

    /// <summary>
    /// Ceiling on unsent changes. This is what stops the outbox growing without bound: a
    /// device that has been offline for a fortnight refuses the next change with an
    /// explanation rather than quietly accumulating work nobody will ever reconcile.
    /// </summary>
    private const int MaxQueuedChanges = 500;

    private readonly ILocalFleetStore _store;
    private readonly IFleetGoApiClient _apiClient;
    private readonly IConnectivityService _connectivity;
    private readonly IAuthenticationService _authentication;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FleetRepository> _logger;

    public FleetRepository(
        ILocalFleetStore store,
        IFleetGoApiClient apiClient,
        IConnectivityService connectivity,
        IAuthenticationService authentication,
        TimeProvider timeProvider,
        ILogger<FleetRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(connectivity);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _apiClient = apiClient;
        _connectivity = connectivity;
        _authentication = authentication;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> PrepareForDriverAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        bool wiped = await _store.EnsureOwnedByAsync(driverId, cancellationToken).ConfigureAwait(false);

        if (wiped)
        {
            // Worth a log line: it means a different driver used this device, and their
            // cached fleet data has just been destroyed rather than handed over.
            _logger.LogInformation("Local cache cleared: it belonged to a different driver.");
        }

        return wiped;
    }

    /// <inheritdoc />
    public Task ClearAsync(CancellationToken cancellationToken = default) => _store.ClearAllAsync(cancellationToken);

    // --- reads -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<CachedData<IReadOnlyList<RouteResponse>>> GetRoutesForDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return new CachedData<IReadOnlyList<RouteResponse>>([], null, false);
        }

        string storageDate = FleetRecordMapper.ToStorageDate(date);

        IReadOnlyList<LocalRoute> rows = await _store.GetRoutesForDateAsync(storageDate, cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0 && await TryHydrateAsync(RefreshRouteWindowAsync, cancellationToken).ConfigureAwait(false))
        {
            rows = await _store.GetRoutesForDateAsync(storageDate, cancellationToken).ConfigureAwait(false);
        }

        return new CachedData<IReadOnlyList<RouteResponse>>(
            [.. rows.Select(row => FleetRecordMapper.ToContract(row))],
            OldestSync(rows.Select(row => row.SyncedAtUtc)),
            rows.Any(row => row.HasPendingLocalChange));
    }

    /// <inheritdoc />
    public async Task<CachedData<RoutePage>> GetRoutesAsync(
        RouteStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return new CachedData<RoutePage>(new RoutePage([], 0), null, false);
        }

        string? statusName = status?.ToString();

        int total = await _store.CountRoutesAsync(statusName, cancellationToken).ConfigureAwait(false);

        // Nothing cached at all is the one case worth a network round trip on a read: a fresh
        // install, or a driver opening the list before the first background sync has run.
        // A filter that simply matches nothing is not - going to the network every time a
        // driver selects "Cancelled" on a clean week would be a request per tap.
        if (total == 0 &&
            await _store.CountRoutesAsync(null, cancellationToken).ConfigureAwait(false) == 0 &&
            await TryHydrateAsync(RefreshRouteWindowAsync, cancellationToken).ConfigureAwait(false))
        {
            total = await _store.CountRoutesAsync(statusName, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<LocalRoute> rows = await _store.GetRoutesAsync(statusName, skip, take, cancellationToken).ConfigureAwait(false);

        return new CachedData<RoutePage>(
            new RoutePage([.. rows.Select(row => FleetRecordMapper.ToContract(row))], total),
            OldestSync(rows.Select(row => row.SyncedAtUtc)),
            rows.Any(row => row.HasPendingLocalChange));
    }

    /// <inheritdoc />
    public async Task<CachedData<RouteDetail>> GetRouteDetailAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return Empty(new RouteDetail(null, [], null));
        }

        LocalRoute? route = await _store.GetRouteAsync(routeId, cancellationToken).ConfigureAwait(false);

        if (route is null)
        {
            if (!await TryHydrateAsync(token => RefreshRouteDetailAsync(routeId, token), cancellationToken).ConfigureAwait(false))
            {
                return Empty(new RouteDetail(null, [], null));
            }

            route = await _store.GetRouteAsync(routeId, cancellationToken).ConfigureAwait(false);

            if (route is null)
            {
                return Empty(new RouteDetail(null, [], null));
            }
        }

        IReadOnlyList<LocalStop> stops = await _store.GetStopsForRouteAsync(routeId, cancellationToken).ConfigureAwait(false);

        // The route says it has stops but none are cached: the route came from the list
        // window, which does not carry stops. Fetch them once, here, rather than showing an
        // empty route that the driver has no way to fill in.
        if (stops.Count == 0 && route.StopCount > 0 &&
            await TryHydrateAsync(token => RefreshRouteDetailAsync(routeId, token), cancellationToken).ConfigureAwait(false))
        {
            route = await _store.GetRouteAsync(routeId, cancellationToken).ConfigureAwait(false) ?? route;
            stops = await _store.GetStopsForRouteAsync(routeId, cancellationToken).ConfigureAwait(false);
        }

        LocalVehicle? vehicle = route.VehicleId is { } vehicleId
            ? await _store.GetVehicleAsync(vehicleId, cancellationToken).ConfigureAwait(false)
            : null;

        RouteDetail detail = new(
            FleetRecordMapper.ToContract(route),
            [.. stops.Select(stop => FleetRecordMapper.ToContract(stop))],
            vehicle is null ? null : FleetRecordMapper.ToContract(vehicle));

        return new CachedData<RouteDetail>(
            detail,
            OldestSync([route.SyncedAtUtc, .. stops.Select(stop => stop.SyncedAtUtc)]),
            route.HasPendingLocalChange || stops.Any(stop => stop.HasPendingLocalChange));
    }

    /// <inheritdoc />
    public async Task<CachedData<StopDetail>> GetStopDetailAsync(Guid stopId, CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return Empty(new StopDetail(null, [], null));
        }

        LocalStop? stop = await _store.GetStopAsync(stopId, cancellationToken).ConfigureAwait(false);

        if (stop is null)
        {
            if (!await TryHydrateAsync(token => RefreshStopDetailAsync(stopId, token), cancellationToken).ConfigureAwait(false))
            {
                return Empty(new StopDetail(null, [], null));
            }

            stop = await _store.GetStopAsync(stopId, cancellationToken).ConfigureAwait(false);

            if (stop is null)
            {
                return Empty(new StopDetail(null, [], null));
            }
        }

        IReadOnlyList<LocalPackage> packages = await _store.GetPackagesForStopAsync(stopId, cancellationToken).ConfigureAwait(false);
        LocalCustomer? customer = await _store.GetCustomerAsync(stop.CustomerId, cancellationToken).ConfigureAwait(false);

        if ((customer is null || (packages.Count == 0 && stop.PackageCount > 0)) &&
            await TryHydrateAsync(token => RefreshStopDetailAsync(stopId, token), cancellationToken).ConfigureAwait(false))
        {
            stop = await _store.GetStopAsync(stopId, cancellationToken).ConfigureAwait(false) ?? stop;
            packages = await _store.GetPackagesForStopAsync(stopId, cancellationToken).ConfigureAwait(false);
            customer = await _store.GetCustomerAsync(stop.CustomerId, cancellationToken).ConfigureAwait(false);
        }

        StopDetail detail = new(
            FleetRecordMapper.ToContract(stop),
            [.. packages.Select(package => FleetRecordMapper.ToContract(package))],
            customer is null ? null : FleetRecordMapper.ToContract(customer));

        return new CachedData<StopDetail>(
            detail,
            OldestSync([stop.SyncedAtUtc, .. packages.Select(package => package.SyncedAtUtc)]),
            stop.HasPendingLocalChange || packages.Any(package => package.HasPendingLocalChange));
    }

    // --- refresh -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task RefreshRouteWindowAsync(CancellationToken cancellationToken = default)
    {
        DateTime syncedAt = _timeProvider.GetUtcNow().UtcDateTime;
        List<LocalRoute> routes = [];

        for (int page = 1; page <= RouteWindowPages; page++)
        {
            PagedResponse<RouteResponse> response = await _apiClient
                .GetRoutesAsync(page: page, pageSize: RoutePageSize, sort: "-routeDate", cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            routes.AddRange(response.Items.Select(route => FleetRecordMapper.ToLocal(route, syncedAt)));

            if (routes.Count >= response.TotalCount || response.Items.Count < RoutePageSize)
            {
                break;
            }
        }

        await _store.ReplaceRoutesAsync(routes, cancellationToken).ConfigureAwait(false);
        await ReapplyPendingChangesAsync(cancellationToken).ConfigureAwait(false);
        await RecordSuccessfulSyncAsync(syncedAt, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RefreshRouteDetailAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        DateTime syncedAt = _timeProvider.GetUtcNow().UtcDateTime;

        RouteResponse route = await _apiClient.GetRouteAsync(routeId, cancellationToken).ConfigureAwait(false);

        PagedResponse<StopResponse> stops = await _apiClient
            .GetStopsAsync(routeId: routeId, page: 1, pageSize: ChildPageSize, sort: "sequence", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _store.UpsertRouteAsync(FleetRecordMapper.ToLocal(route, syncedAt), cancellationToken).ConfigureAwait(false);
        await _store.ReplaceStopsForRouteAsync(
            routeId,
            [.. stops.Items.Select(stop => FleetRecordMapper.ToLocal(stop, syncedAt))],
            cancellationToken).ConfigureAwait(false);

        if (route.VehicleId is { } vehicleId)
        {
            // The vehicle is supporting detail, not the point of the screen: a driver who can
            // see the route and its stops should not lose both because the vehicle lookup
            // failed. Same reasoning Phase 4B used on RouteDetailViewModel.
            try
            {
                VehicleResponse vehicle = await _apiClient.GetVehicleAsync(vehicleId, cancellationToken).ConfigureAwait(false);
                await _store.UpsertVehicleAsync(FleetRecordMapper.ToLocal(vehicle, syncedAt), cancellationToken).ConfigureAwait(false);
            }
            catch (FleetGoApiException exception)
            {
                _logger.LogDebug("Vehicle {VehicleId} could not be refreshed: HTTP {StatusCode}.", vehicleId, exception.StatusCode);
            }
        }

        await ReapplyPendingChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RefreshStopDetailAsync(Guid stopId, CancellationToken cancellationToken = default)
    {
        DateTime syncedAt = _timeProvider.GetUtcNow().UtcDateTime;

        StopResponse stop = await _apiClient.GetStopAsync(stopId, cancellationToken).ConfigureAwait(false);

        PagedResponse<PackageResponse> packages = await _apiClient
            .GetPackagesAsync(stopId: stopId, page: 1, pageSize: ChildPageSize, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await _store.UpsertStopAsync(FleetRecordMapper.ToLocal(stop, syncedAt), cancellationToken).ConfigureAwait(false);
        await _store.ReplacePackagesForStopAsync(
            stopId,
            [.. packages.Items.Select(package => FleetRecordMapper.ToLocal(package, syncedAt))],
            cancellationToken).ConfigureAwait(false);

        try
        {
            CustomerResponse customer = await _apiClient.GetCustomerAsync(stop.CustomerId, cancellationToken).ConfigureAwait(false);
            await _store.UpsertCustomerAsync(FleetRecordMapper.ToLocal(customer, syncedAt), cancellationToken).ConfigureAwait(false);
        }
        catch (FleetGoApiException exception)
        {
            _logger.LogDebug("Customer for stop {StopId} could not be refreshed: HTTP {StatusCode}.", stopId, exception.StatusCode);
        }

        await ReapplyPendingChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // --- writes ------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<QueuedChangeResult> QueueStopStatusAsync(Guid stopId, StopStatus newStatus, CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return QueuedChangeResult.Refused("Sign in before changing a stop.");
        }

        LocalStop? stop = await _store.GetStopAsync(stopId, cancellationToken).ConfigureAwait(false);

        return stop is null
            ? QueuedChangeResult.Refused("This stop isn't saved on this device yet. Open it once while you have a connection.")
            : await QueueStatusChangeAsync(
                OutboxEntityType.Stop,
                stopId,
                newStatus.ToString(),
                EntitySnapshot.FromLocalStop(stop),
                cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<QueuedChangeResult> QueueRouteStatusAsync(Guid routeId, RouteStatus newStatus, CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return QueuedChangeResult.Refused("Sign in before changing a route.");
        }

        LocalRoute? route = await _store.GetRouteAsync(routeId, cancellationToken).ConfigureAwait(false);

        return route is null
            ? QueuedChangeResult.Refused("This route isn't saved on this device yet. Open it once while you have a connection.")
            : await QueueStatusChangeAsync(
                OutboxEntityType.Route,
                routeId,
                newStatus.ToString(),
                EntitySnapshot.FromLocalRoute(route),
                cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<QueuedChangeResult> QueuePackageStatusAsync(Guid packageId, PackageStatus newStatus, CancellationToken cancellationToken = default)
    {
        if (!await EnsureReadyAsync(cancellationToken).ConfigureAwait(false))
        {
            return QueuedChangeResult.Refused("Sign in before changing a package.");
        }

        LocalPackage? package = await _store.GetPackageAsync(packageId, cancellationToken).ConfigureAwait(false);

        return package is null
            ? QueuedChangeResult.Refused("This package isn't saved on this device yet. Open it once while you have a connection.")
            : await QueueStatusChangeAsync(
                OutboxEntityType.Package,
                packageId,
                newStatus.ToString(),
                EntitySnapshot.FromLocalPackage(package),
                cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DiscardQueuedChangeAsync(Guid operationId, CancellationToken cancellationToken = default) =>
        _store.DeleteOperationAsync(operationId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxOperation>> GetUnresolvedChangesAsync(CancellationToken cancellationToken = default) =>
        _store.GetUnresolvedOperationsAsync(cancellationToken);

    // --- internals ---------------------------------------------------------------------

    /// <summary>
    /// Opens the database and enforces the cache's owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ownership check is the whole of the cross-driver isolation story, so the two cases
    /// are worth spelling out:
    /// </para>
    /// <para>
    /// <b>The driver profile is known.</b> The cache is bound to that driver, wiping it first
    /// if it belonged to someone else. A second driver signing in on a shared device never
    /// sees the first one's stops, and never inherits their queue.
    /// </para>
    /// <para>
    /// <b>The profile is not loaded yet.</b> This happens when the app resumes offline: the
    /// tokens restored from secure storage, but <c>GET /auth/me</c> could not run. The cache
    /// is served as-is rather than withheld, and that is safe because signing in requires the
    /// network - so an offline session is necessarily the same session that was last online,
    /// belonging to the same driver the cache was bound to. Ownership is re-checked the moment
    /// the profile does load, and sign-out wipes the cache outright, so the next driver on
    /// this device starts from nothing either way.
    /// </para>
    /// </remarks>
    private async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (!_authentication.IsAuthenticated)
        {
            return false;
        }

        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (_authentication.CurrentUser?.DriverId is { } driverId)
        {
            await _store.EnsureOwnedByAsync(driverId, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Runs a refresh for a screen that found nothing cached, and reports whether it worked.
    /// <para>
    /// Transport failures are swallowed on purpose: a read is allowed to come back with
    /// whatever the device has, and "the network died" is the sync banner's job to report, not
    /// something that should turn into an error dialog over a screen that has data to show.
    /// Anything the API actually answered with - 401, 403, 500 - is left to propagate, because
    /// those mean something specific and the screens already map them.
    /// </para>
    /// </summary>
    private async Task<bool> TryHydrateAsync(Func<CancellationToken, Task> refresh, CancellationToken cancellationToken)
    {
        if (!_connectivity.IsOnline)
        {
            return false;
        }

        try
        {
            await refresh(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogDebug(exception, "Hydration skipped: the API could not be reached.");
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Hydration skipped: the request timed out.");
            return false;
        }
    }

    private async Task<QueuedChangeResult> QueueStatusChangeAsync(
        OutboxEntityType entityType,
        Guid entityId,
        string newStatus,
        EntitySnapshot currentServerState,
        CancellationToken cancellationToken)
    {
        OutboxOperation? existing = await _store
            .FindPendingOperationAsync(entityType, entityId, OutboxOperationType.UpdateStatus, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // Coalesce. The driver tapped twice before anything could be sent, so the server
            // is owed one update, not two - and the baseline stays the one captured before any
            // of this device's changes were applied, which is the only value that still
            // describes what the server had.
            StatusChangePayload? previous = OfflineJson.TryDeserialize(existing.PayloadJson);

            existing.PayloadJson = OfflineJson.Serialize(new StatusChangePayload
            {
                NewStatus = newStatus,
                Baseline = previous?.Baseline ?? currentServerState,
            });

            // A fresh intent deserves a fresh attempt budget: whatever backoff the previous
            // value had accumulated was about that value, not this one.
            existing.Status = nameof(OutboxOperationStatus.Pending);
            existing.RetryCount = 0;
            existing.NextAttemptAtUtc = null;
            existing.LastError = null;
            existing.ConflictDetail = null;

            await _store.UpdateOperationAsync(existing, cancellationToken).ConfigureAwait(false);
            await _store.ApplyLocalStatusAsync(entityType, entityId, newStatus, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Updated queued {EntityType} status change for {EntityId}.", entityType, entityId);
            return new QueuedChangeResult(Queued: true, existing.Id);
        }

        int pending = await _store.CountOperationsAsync(OutboxOperationStatus.Pending, cancellationToken).ConfigureAwait(false);

        if (pending >= MaxQueuedChanges)
        {
            _logger.LogWarning("Refused a {EntityType} status change: {Pending} changes are already queued.", entityType, pending);
            return QueuedChangeResult.Refused(
                "There are too many unsent changes on this device. Reconnect and let them send before making more.");
        }

        OutboxOperation operation = new()
        {
            Id = Guid.NewGuid(),
            EntityType = entityType.ToString(),
            EntityId = entityId,
            OperationType = OutboxOperationType.UpdateStatus.ToString(),
            PayloadJson = OfflineJson.Serialize(new StatusChangePayload
            {
                NewStatus = newStatus,
                Baseline = currentServerState,
            }),
            CreatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime,
            Status = nameof(OutboxOperationStatus.Pending),
        };

        await _store.EnqueueAsync(operation, cancellationToken).ConfigureAwait(false);

        // Applied to the cache only after the queue accepted it. The other order would show
        // the driver a change that no longer exists anywhere if the insert failed.
        await _store.ApplyLocalStatusAsync(entityType, entityId, newStatus, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Queued a {EntityType} status change for {EntityId}.", entityType, entityId);
        return new QueuedChangeResult(Queued: true, operation.Id);
    }

    /// <summary>
    /// Puts unsent changes back on top of a cache that was just overwritten with server state.
    /// <para>
    /// Without this, every refresh would make the driver's own work appear to undo itself: they
    /// mark a stop delivered in a dead spot, the network returns, the sync pulls the server's
    /// copy - which still says Pending, because the change has not been sent yet - and the
    /// screen silently reverts. Only genuinely pending operations are re-applied; a change that
    /// ended in conflict or failure deliberately shows the server's value, with the problem
    /// reported next to it.
    /// </para>
    /// </summary>
    private async Task ReapplyPendingChangesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<OutboxOperation> unresolved = await _store.GetUnresolvedOperationsAsync(cancellationToken).ConfigureAwait(false);

        foreach (OutboxOperation operation in unresolved)
        {
            if (!string.Equals(operation.Status, nameof(OutboxOperationStatus.Pending), StringComparison.Ordinal))
            {
                continue;
            }

            if (OfflineJson.TryDeserialize(operation.PayloadJson) is not { } payload ||
                !Enum.TryParse(operation.EntityType, out OutboxEntityType entityType))
            {
                continue;
            }

            await _store.ApplyLocalStatusAsync(entityType, operation.EntityId, payload.NewStatus, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordSuccessfulSyncAsync(DateTime syncedAtUtc, CancellationToken cancellationToken)
    {
        SyncMetadata metadata = await _store.GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        metadata.LastSuccessfulSyncUtc = syncedAtUtc;
        metadata.LastSyncError = null;
        await _store.SaveMetadataAsync(metadata, cancellationToken).ConfigureAwait(false);
    }

    private static CachedData<T> Empty<T>(T value) => new(value, null, false);

    /// <summary>
    /// The least fresh timestamp in a set, or null when there is nothing. Deliberately the
    /// oldest rather than the newest: "synced 2 minutes ago" should mean all of it, not the
    /// one row that happened to be re-read most recently.
    /// </summary>
    private static DateTime? OldestSync(IEnumerable<DateTime> timestamps)
    {
        DateTime? oldest = null;

        foreach (DateTime timestamp in timestamps)
        {
            if (oldest is null || timestamp < oldest)
            {
                oldest = timestamp;
            }
        }

        return oldest;
    }
}
