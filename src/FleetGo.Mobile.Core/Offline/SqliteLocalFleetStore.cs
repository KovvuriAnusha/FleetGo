using FleetGo.Mobile.Core.Offline.Models;
using SQLite;

namespace FleetGo.Mobile.Core.Offline;

/// <summary>
/// sqlite-net implementation of <see cref="ILocalFleetStore"/>.
/// <para>
/// sqlite-net is the SQLite library .NET MAUI documents for on-device storage: its async
/// connection keeps every statement off the UI thread, and <c>CreateTableAsync</c> is
/// idempotent, which is what makes initialisation safe to run on every launch. Schema changes
/// that are more than "add a table" would be handled by inspecting <c>user_version</c>; there
/// is only one schema so far, so there is nothing to migrate yet and no speculative migration
/// machinery here.
/// </para>
/// <para>
/// sqlite-net's async API predates <see cref="CancellationToken"/> and takes none. Tokens are
/// honoured at the boundaries of each operation rather than inside a statement: a long
/// sequence of work stops promptly, a single row write is not interrupted halfway.
/// </para>
/// </summary>
public sealed class SqliteLocalFleetStore : ILocalFleetStore, IAsyncDisposable, IDisposable
{
    private const SQLiteOpenFlags OpenFlags =
        SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;

    private readonly LocalStoreOptions _options;
    private readonly SemaphoreSlim _initialisationLock = new(1, 1);

    /// <summary>
    /// Points SQLitePCLRaw at the bundled native SQLite before anything tries to open a
    /// database.
    /// <para>
    /// Called explicitly rather than relied upon. sqlite-net does initialise the provider
    /// itself on most platforms, but the failure mode when something has not - a
    /// <c>You need to call SQLitePCL.raw.SetProvider()</c> exception on the first query - is
    /// exactly the kind that shows up on one platform's release build and nowhere else.
    /// <c>Init</c> is safe to call more than once, so a static constructor is the cheapest
    /// possible insurance.
    /// </para>
    /// </summary>
    static SqliteLocalFleetStore() => SQLitePCL.Batteries_V2.Init();

    private SQLiteAsyncConnection? _connection;

    public SqliteLocalFleetStore(LocalStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> EnsureOwnedByAsync(Guid driverId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        SyncMetadata metadata = await LoadMetadataAsync(connection).ConfigureAwait(false);

        if (metadata.OwnerDriverId == driverId)
        {
            return false;
        }

        if (metadata.OwnerDriverId is null)
        {
            // Unowned. Adopt it - do not wipe it.
            //
            // "No owner recorded" is not "somebody else's data": it is a cache that has never
            // been bound to anyone, which happens on a fresh install, after a sign-out, and -
            // the case that matters - whenever records were written before the driver profile
            // was known. The refresh methods on FleetRepository are public and do exactly
            // that: they fill the cache without binding an owner, because they can run before
            // GET /auth/me has returned.
            //
            // Clearing here would therefore delete data, and queued changes, that provably
            // belong to nobody else. The worst version of that is a driver who works a whole
            // morning offline (tokens restored from SecureStorage, profile unreachable) and
            // loses every unsent delivery the moment the network returns and the profile
            // finally loads. The isolation rule is "never serve or send another driver's
            // data", and that is satisfied by clearing only when the owner genuinely differs.
            //
            // LastSuccessfulSyncUtc is carried over rather than reset, because adopting a
            // cache does not make it less current than it was a moment ago.
            metadata.Id = SyncMetadata.SingletonId;
            metadata.OwnerDriverId = driverId;
            await connection.InsertOrReplaceAsync(metadata).ConfigureAwait(false);

            return false;
        }

        // A different driver on the same device must never see what the previous one cached,
        // and must never inherit their queued changes either.
        await ClearAllInternalAsync(connection).ConfigureAwait(false);

        await connection.InsertOrReplaceAsync(
            new SyncMetadata { Id = SyncMetadata.SingletonId, OwnerDriverId = driverId }).ConfigureAwait(false);

        return true;
    }

    /// <inheritdoc />
    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await ClearAllInternalAsync(connection).ConfigureAwait(false);
    }

    // --- writes from a sync ----------------------------------------------------------

    /// <inheritdoc />
    public async Task ReplaceRoutesAsync(IReadOnlyList<LocalRoute> routes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(routes);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.RunInTransactionAsync(transaction =>
        {
            transaction.DeleteAll<LocalRoute>();

            foreach (LocalRoute route in routes)
            {
                transaction.InsertOrReplace(route);
            }
        }).ConfigureAwait(false);

        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplaceStopsForRouteAsync(Guid routeId, IReadOnlyList<LocalStop> stops, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stops);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.RunInTransactionAsync(transaction =>
        {
            transaction.Execute("DELETE FROM local_stops WHERE RouteId = ?", routeId);

            foreach (LocalStop stop in stops)
            {
                transaction.InsertOrReplace(stop);
            }
        }).ConfigureAwait(false);

        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReplacePackagesForStopAsync(Guid stopId, IReadOnlyList<LocalPackage> packages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packages);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.RunInTransactionAsync(transaction =>
        {
            transaction.Execute("DELETE FROM local_packages WHERE StopId = ?", stopId);

            foreach (LocalPackage package in packages)
            {
                transaction.InsertOrReplace(package);
            }
        }).ConfigureAwait(false);

        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertRouteAsync(LocalRoute route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.InsertOrReplaceAsync(route).ConfigureAwait(false);
        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertStopAsync(LocalStop stop, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stop);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.InsertOrReplaceAsync(stop).ConfigureAwait(false);
        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertPackageAsync(LocalPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.InsertOrReplaceAsync(package).ConfigureAwait(false);
        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertVehicleAsync(LocalVehicle vehicle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await connection.InsertOrReplaceAsync(vehicle).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertCustomerAsync(LocalCustomer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await connection.InsertOrReplaceAsync(customer).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ApplyLocalStatusAsync(OutboxEntityType entityType, Guid entityId, string status, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        string table = TableFor(entityType);

        // The local copy moves immediately so the driver sees their own change; the pending
        // flag is what stops the UI claiming the server has agreed to it.
        await connection.ExecuteAsync(
            $"UPDATE {table} SET Status = ?, HasPendingLocalChange = 1 WHERE Id = ?",
            status,
            entityId).ConfigureAwait(false);
    }

    // --- reads -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalRoute>> GetRoutesAsync(string? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        AsyncTableQuery<LocalRoute> query = connection.Table<LocalRoute>();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(route => route.Status == status);
        }

        List<LocalRoute> routes = await query
            .OrderByDescending(route => route.RouteDate)
            .ThenBy(route => route.RouteNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync()
            .ConfigureAwait(false);

        return routes;
    }

    /// <inheritdoc />
    public async Task<int> CountRoutesAsync(string? status, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        AsyncTableQuery<LocalRoute> query = connection.Table<LocalRoute>();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(route => route.Status == status);
        }

        return await query.CountAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalRoute>> GetRoutesForDateAsync(string routeDate, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        List<LocalRoute> routes = await connection.Table<LocalRoute>()
            .Where(route => route.RouteDate == routeDate)
            .OrderBy(route => route.RouteNumber)
            .ToListAsync()
            .ConfigureAwait(false);

        return routes;
    }

    /// <inheritdoc />
    public async Task<LocalRoute?> GetRouteAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await connection.FindAsync<LocalRoute>(routeId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalStop>> GetStopsForRouteAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        List<LocalStop> stops = await connection.Table<LocalStop>()
            .Where(stop => stop.RouteId == routeId)
            .OrderBy(stop => stop.Sequence)
            .ToListAsync()
            .ConfigureAwait(false);

        return stops;
    }

    /// <inheritdoc />
    public async Task<LocalStop?> GetStopAsync(Guid stopId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await connection.FindAsync<LocalStop>(stopId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalPackage>> GetPackagesForStopAsync(Guid stopId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        List<LocalPackage> packages = await connection.Table<LocalPackage>()
            .Where(package => package.StopId == stopId)
            .OrderBy(package => package.TrackingNumber)
            .ToListAsync()
            .ConfigureAwait(false);

        return packages;
    }

    /// <inheritdoc />
    public async Task<LocalPackage?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await connection.FindAsync<LocalPackage>(packageId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LocalVehicle?> GetVehicleAsync(Guid vehicleId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await connection.FindAsync<LocalVehicle>(vehicleId).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LocalCustomer?> GetCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await connection.FindAsync<LocalCustomer>(customerId).ConfigureAwait(false);
    }

    // --- outbox ----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task EnqueueAsync(OutboxOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.InsertAsync(operation).ConfigureAwait(false);
        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OutboxOperation?> FindPendingOperationAsync(
        OutboxEntityType entityType,
        Guid entityId,
        OutboxOperationType operationType,
        CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        List<OutboxOperation> matches = await connection.QueryAsync<OutboxOperation>(
            "SELECT * FROM outbox_operations WHERE EntityType = ? AND EntityId = ? AND OperationType = ? AND Status = ? ORDER BY Sequence ASC LIMIT 1",
            entityType.ToString(),
            entityId,
            operationType.ToString(),
            nameof(OutboxOperationStatus.Pending)).ConfigureAwait(false);

        return matches.Count == 0 ? null : matches[0];
    }

    /// <inheritdoc />
    public async Task DeleteOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.ExecuteAsync("DELETE FROM outbox_operations WHERE Id = ?", operationId).ConfigureAwait(false);
        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountOperationsAsync(OutboxOperationStatus status, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        return await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM outbox_operations WHERE Status = ?",
            status.ToString()).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxOperation>> GetSendableOperationsAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        // Ordered by Sequence: the order the driver made the changes in.
        List<OutboxOperation> operations = await connection.QueryAsync<OutboxOperation>(
            "SELECT * FROM outbox_operations WHERE Status = ? AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= ?) ORDER BY Sequence ASC",
            nameof(OutboxOperationStatus.Pending),
            nowUtc).ConfigureAwait(false);

        return operations;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutboxOperation>> GetUnresolvedOperationsAsync(CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        List<OutboxOperation> operations = await connection.QueryAsync<OutboxOperation>(
            "SELECT * FROM outbox_operations WHERE Status IN (?, ?, ?) ORDER BY Sequence ASC",
            nameof(OutboxOperationStatus.Pending),
            nameof(OutboxOperationStatus.Failed),
            nameof(OutboxOperationStatus.Conflict)).ConfigureAwait(false);

        return operations;
    }

    /// <inheritdoc />
    public async Task UpdateOperationAsync(OutboxOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await connection.UpdateAsync(operation).ConfigureAwait(false);
        await RefreshPendingFlagsAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> PruneCompletedOperationsAsync(DateTime completedBeforeUtc, CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        // Only Completed rows are pruned on age. Failed and Conflict rows stay until a person
        // deals with them - that is the whole point of surfacing them.
        return await connection.ExecuteAsync(
            "DELETE FROM outbox_operations WHERE Status = ? AND LastAttemptAtUtc IS NOT NULL AND LastAttemptAtUtc < ?",
            nameof(OutboxOperationStatus.Completed),
            completedBeforeUtc).ConfigureAwait(false);
    }

    // --- metadata --------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<SyncMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await LoadMetadataAsync(connection).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveMetadataAsync(SyncMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        SQLiteAsyncConnection connection = await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        metadata.Id = SyncMetadata.SingletonId;
        await connection.InsertOrReplaceAsync(metadata).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            await connection.CloseAsync().ConfigureAwait(false);
        }

        _initialisationLock.Dispose();
    }

    /// <summary>
    /// The synchronous half, and not optional: this store is registered as a singleton, and
    /// <c>ServiceProvider.Dispose()</c> throws outright on a singleton that implements only
    /// <see cref="IAsyncDisposable"/>. A container that cannot be disposed is a crash at app
    /// shutdown, so both are implemented.
    /// <para>
    /// Blocking here is safe rather than merely convenient: sqlite-net's <c>CloseAsync</c>
    /// schedules its work on the thread pool and captures no synchronisation context, so
    /// waiting on it cannot deadlock the UI thread the way blocking on arbitrary async code
    /// would.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            connection.CloseAsync().GetAwaiter().GetResult();
        }

        _initialisationLock.Dispose();
    }

    // --- internals -------------------------------------------------------------------

    private async Task<SQLiteAsyncConnection> EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_connection is { } existing)
        {
            return existing;
        }

        await _initialisationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_connection is { } created)
            {
                return created;
            }

            SQLiteAsyncConnection connection = new(_options.DatabasePath, OpenFlags);

            // CreateTableAsync is a no-op when the table already exists, which is what makes
            // this safe on every launch rather than only on a first run.
            await connection.CreateTableAsync<LocalRoute>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalStop>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalPackage>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalVehicle>().ConfigureAwait(false);
            await connection.CreateTableAsync<LocalCustomer>().ConfigureAwait(false);
            await connection.CreateTableAsync<OutboxOperation>().ConfigureAwait(false);
            await connection.CreateTableAsync<SyncMetadata>().ConfigureAwait(false);

            await EnsureMetadataRowAsync(connection).ConfigureAwait(false);

            _connection = connection;
            return connection;
        }
        finally
        {
            _initialisationLock.Release();
        }
    }

    private static async Task EnsureMetadataRowAsync(SQLiteAsyncConnection connection)
    {
        SyncMetadata? existing = await connection.FindAsync<SyncMetadata>(SyncMetadata.SingletonId).ConfigureAwait(false);

        if (existing is null)
        {
            await connection.InsertAsync(new SyncMetadata { Id = SyncMetadata.SingletonId }).ConfigureAwait(false);
        }
    }

    private static async Task<SyncMetadata> LoadMetadataAsync(SQLiteAsyncConnection connection)
    {
        SyncMetadata? metadata = await connection.FindAsync<SyncMetadata>(SyncMetadata.SingletonId).ConfigureAwait(false);
        return metadata ?? new SyncMetadata { Id = SyncMetadata.SingletonId };
    }

    private static async Task ClearAllInternalAsync(SQLiteAsyncConnection connection)
    {
        await connection.RunInTransactionAsync(transaction =>
        {
            transaction.DeleteAll<LocalRoute>();
            transaction.DeleteAll<LocalStop>();
            transaction.DeleteAll<LocalPackage>();
            transaction.DeleteAll<LocalVehicle>();
            transaction.DeleteAll<LocalCustomer>();
            transaction.DeleteAll<OutboxOperation>();
            transaction.DeleteAll<SyncMetadata>();
        }).ConfigureAwait(false);

        await EnsureMetadataRowAsync(connection).ConfigureAwait(false);
    }

    /// <summary>
    /// Recomputes "this record has an unsent change" from the queue, so the flag is a
    /// consequence of the outbox rather than a second copy of the truth that can drift from it
    /// when a sync replaces the cached rows.
    /// </summary>
    private static async Task RefreshPendingFlagsAsync(SQLiteAsyncConnection connection)
    {
        await connection.RunInTransactionAsync(transaction =>
        {
            transaction.Execute("UPDATE local_routes SET HasPendingLocalChange = 0");
            transaction.Execute("UPDATE local_stops SET HasPendingLocalChange = 0");
            transaction.Execute("UPDATE local_packages SET HasPendingLocalChange = 0");

            const string unresolved =
                "SELECT EntityId FROM outbox_operations WHERE EntityType = ? AND Status IN (?, ?, ?)";

            foreach ((OutboxEntityType entityType, string table) in new[]
                     {
                         (OutboxEntityType.Route, "local_routes"),
                         (OutboxEntityType.Stop, "local_stops"),
                         (OutboxEntityType.Package, "local_packages"),
                     })
            {
                transaction.Execute(
                    $"UPDATE {table} SET HasPendingLocalChange = 1 WHERE Id IN ({unresolved})",
                    entityType.ToString(),
                    nameof(OutboxOperationStatus.Pending),
                    nameof(OutboxOperationStatus.Failed),
                    nameof(OutboxOperationStatus.Conflict));
            }
        }).ConfigureAwait(false);
    }

    private static string TableFor(OutboxEntityType entityType) => entityType switch
    {
        OutboxEntityType.Route => "local_routes",
        OutboxEntityType.Stop => "local_stops",
        OutboxEntityType.Package => "local_packages",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown entity type."),
    };
}
