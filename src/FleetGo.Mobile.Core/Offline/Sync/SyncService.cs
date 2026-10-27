using System.Net;
using FleetGo.Mobile.Core.Offline.Connectivity;
using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Mobile.Core.Session;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;
using Microsoft.Extensions.Logging;

namespace FleetGo.Mobile.Core.Offline.Sync;

/// <summary>
/// Sends what the driver changed, then pulls back what they are about to need.
/// </summary>
/// <remarks>
/// <para><b>Push before pull.</b> The other order would pull the server's copy of a record the
/// device is about to change, overwrite the driver's unsent value with it, and then send the
/// change anyway - a visible flicker backwards, for no gain.</para>
///
/// <para><b>Read before write.</b> The API's update endpoints replace the whole record and
/// accept no concurrency token: no ETag, no <c>If-Match</c>, no version column. So the only
/// honest way to change one field is to read the record, check it still looks the way it did
/// when the driver made the change, and send it back with that one field different. That
/// leaves a real gap - between the read and the write, someone else can still get in - and
/// this code does not pretend otherwise. Closing it needs the API to grow a concurrency token;
/// inventing a protocol the server does not implement would only move the silent overwrite
/// somewhere harder to see. See docs/architecture.md.</para>
///
/// <para><b>A conflict is never resolved automatically.</b> Not last-write-wins, not
/// merge-on-a-guess. The operation is parked, the server's value is written back to the cache
/// so the screen stops showing something that is not true, and a person decides. Phase 5's
/// brief is explicit that a conflict must never disappear silently, and quietly picking a
/// winner is exactly that.</para>
/// </remarks>
public sealed class SyncService : ISyncService, ISyncStatusProvider
{
    /// <summary>
    /// Today's routes whose stops are pulled down in full, ready for a day with no signal.
    /// <para>
    /// Capped because a sync has no batch endpoint to lean on - each stop costs its own
    /// requests - and an uncapped prefetch on a busy day would be a request storm on cellular
    /// data the driver pays for. A batch or "route with stops and packages" endpoint is the
    /// obvious server-side answer, and is noted as future work rather than faked here.
    /// </para>
    /// </summary>
    private const int MaxPrefetchRoutes = 3;

    /// <summary>Stops prefetched in full across those routes.</summary>
    private const int MaxPrefetchStops = 25;

    /// <summary>Completed operations are kept this long so the UI can say "sent", then pruned.</summary>
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromHours(24);

    private readonly ILocalFleetStore _store;
    private readonly IFleetRepository _repository;
    private readonly IFleetGoApiClient _apiClient;
    private readonly IConnectivityService _connectivity;
    private readonly IAuthenticationService _authentication;
    private readonly IRetryPolicy _retryPolicy;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SyncService> _logger;

    /// <summary>
    /// Single-flight guard. Taken with a zero timeout rather than waited on: two syncs racing
    /// would send the same operations twice, and a caller that arrives while one is running
    /// wants the answer "already happening", not a queue behind it.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SyncSnapshot _current = SyncSnapshot.Unknown;

    public SyncService(
        ILocalFleetStore store,
        IFleetRepository repository,
        IFleetGoApiClient apiClient,
        IConnectivityService connectivity,
        IAuthenticationService authentication,
        IRetryPolicy retryPolicy,
        TimeProvider timeProvider,
        ILogger<SyncService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(connectivity);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _repository = repository;
        _apiClient = apiClient;
        _connectivity = connectivity;
        _authentication = authentication;
        _retryPolicy = retryPolicy;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? SessionExpired;

    /// <inheritdoc />
    public event EventHandler<SyncSnapshot>? Changed;

    /// <inheritdoc />
    public SyncSnapshot Current => _current;

    /// <inheritdoc />
    public async Task<SyncResult> SyncAsync(SyncReason reason, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return SyncResult.AlreadyRunning;
        }

        try
        {
            if (!_authentication.IsAuthenticated)
            {
                await PublishStatusAsync(isSyncing: false, lastError: null, cancellationToken).ConfigureAwait(false);
                return SyncResult.NotSignedIn;
            }

            await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);

            if (_authentication.CurrentUser?.DriverId is { } driverId)
            {
                await _store.EnsureOwnedByAsync(driverId, cancellationToken).ConfigureAwait(false);
            }

            if (!_connectivity.IsOnline)
            {
                await PublishStatusAsync(isSyncing: false, SyncResult.Offline.Message, cancellationToken).ConfigureAwait(false);
                return SyncResult.Offline;
            }

            _logger.LogInformation("Sync starting ({Reason}).", reason);
            await PublishStatusAsync(isSyncing: true, lastError: null, cancellationToken).ConfigureAwait(false);

            PushSummary push = await PushAsync(cancellationToken).ConfigureAwait(false);

            if (push.SessionExpired)
            {
                await PublishStatusAsync(isSyncing: false, SyncResult.SessionExpired.Message, cancellationToken).ConfigureAwait(false);
                SessionExpired?.Invoke(this, EventArgs.Empty);

                int stillQueued = await CountPendingAsync(cancellationToken).ConfigureAwait(false);
                return SyncResult.SessionExpired with { RemainingCount = stillQueued };
            }

            bool pulled = true;
            string? pullError = null;

            if (!push.NetworkLost)
            {
                (pulled, pullError) = await PullAsync(cancellationToken).ConfigureAwait(false);
            }

            await PruneAsync(cancellationToken).ConfigureAwait(false);

            int remaining = await CountPendingAsync(cancellationToken).ConfigureAwait(false);

            if (push.NetworkLost || !pulled)
            {
                string message = pullError ?? SyncResult.Offline.Message!;
                await PublishStatusAsync(isSyncing: false, message, cancellationToken).ConfigureAwait(false);

                return new SyncResult(
                    push.NetworkLost ? SyncOutcome.Offline : SyncOutcome.Failed,
                    push.Sent,
                    push.Conflicts,
                    push.Failed,
                    remaining,
                    message);
            }

            bool hasProblems = push.Conflicts > 0 || push.Failed > 0;

            string? summary = hasProblems
                ? "Some changes couldn't be saved. Open the sync details to see what happened."
                : null;

            await PublishStatusAsync(isSyncing: false, summary, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Sync finished ({Reason}): {Sent} sent, {Conflicts} conflicted, {Failed} failed, {Remaining} still queued.",
                reason,
                push.Sent,
                push.Conflicts,
                push.Failed,
                remaining);

            return new SyncResult(
                hasProblems ? SyncOutcome.CompletedWithProblems : SyncOutcome.Succeeded,
                push.Sent,
                push.Conflicts,
                push.Failed,
                remaining,
                summary);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The app is going away, or the caller gave up. Nothing is lost: everything the
            // run had not got to is still in the outbox exactly as it was.
            await PublishStatusAsync(isSyncing: false, lastError: null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task RefreshStatusAsync(CancellationToken cancellationToken = default) =>
        PublishStatusAsync(isSyncing: _current.IsSyncing, _current.LastError, cancellationToken);

    // --- push --------------------------------------------------------------------------

    private sealed record PushSummary(int Sent, int Conflicts, int Failed, bool NetworkLost, bool SessionExpired);

    private async Task<PushSummary> PushAsync(CancellationToken cancellationToken)
    {
        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

        IReadOnlyList<OutboxOperation> operations = await _store
            .GetSendableOperationsAsync(now, cancellationToken)
            .ConfigureAwait(false);

        int sent = 0;
        int conflicts = 0;
        int failed = 0;

        foreach (OutboxOperation operation in operations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SendOutcome outcome = await SendAsync(operation, cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case SendOutcome.Sent:
                    sent++;
                    break;

                case SendOutcome.Conflict:
                    conflicts++;
                    break;

                case SendOutcome.Failed:
                    failed++;
                    break;

                case SendOutcome.Retrying:
                    break;

                case SendOutcome.SessionExpired:
                    // Stop immediately. Every remaining operation would get the same answer,
                    // and burning their retry budgets on a problem none of them caused would
                    // turn one expired session into a queue full of dead work.
                    return new PushSummary(sent, conflicts, failed, NetworkLost: false, SessionExpired: true);

                case SendOutcome.NetworkLost:
                    return new PushSummary(sent, conflicts, failed, NetworkLost: true, SessionExpired: false);

                case SendOutcome.FailedAndNetworkLost:
                    return new PushSummary(sent, conflicts, failed + 1, NetworkLost: true, SessionExpired: false);
            }
        }

        return new PushSummary(sent, conflicts, failed, NetworkLost: false, SessionExpired: false);
    }

    private enum SendOutcome
    {
        Sent,
        Conflict,
        Failed,
        Retrying,
        SessionExpired,
        NetworkLost,

        /// <summary>
        /// The network went away <em>and</em> this operation had no attempts left, so it was
        /// marked failed on the way out. Both facts matter: the run stops, and the failure is
        /// counted. Reporting only "network lost" would make an operation that just gave up
        /// for good look like one that is simply waiting for signal.
        /// </summary>
        FailedAndNetworkLost,
    }

    private async Task<SendOutcome> SendAsync(OutboxOperation operation, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(operation.EntityType, out OutboxEntityType entityType) ||
            !Enum.TryParse(operation.OperationType, out OutboxOperationType operationType) ||
            operationType != OutboxOperationType.UpdateStatus)
        {
            return await MarkFailedAsync(
                operation,
                "This change was written by a different version of the app and can't be sent.",
                cancellationToken).ConfigureAwait(false);
        }

        if (OfflineJson.TryDeserialize(operation.PayloadJson) is not { } payload)
        {
            return await MarkFailedAsync(
                operation,
                "This change couldn't be read back and can't be sent.",
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return entityType switch
            {
                OutboxEntityType.Route => await SendRouteAsync(operation, payload, cancellationToken).ConfigureAwait(false),
                OutboxEntityType.Stop => await SendStopAsync(operation, payload, cancellationToken).ConfigureAwait(false),
                OutboxEntityType.Package => await SendPackageAsync(operation, payload, cancellationToken).ConfigureAwait(false),
                _ => await MarkFailedAsync(operation, "Unsupported change type.", cancellationToken).ConfigureAwait(false),
            };
        }
        catch (FleetGoApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Deliberately not "try to refresh the token here". The HTTP pipeline already did
            // that (BearerTokenHandler -> AuthenticationService.GetAccessTokenAsync); a 401
            // that survives it means the session is genuinely over. The operation stays
            // exactly as it is - queued, unsent, and still requiring a real authenticated
            // caller when the driver signs back in.
            _logger.LogWarning("Sync stopped: the API rejected the session.");
            return SendOutcome.SessionExpired;
        }
        catch (FleetGoApiException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            // The server itself refused on a uniqueness rule. Same treatment as a detected
            // divergence: parked for a person, never retried into the same wall.
            return await MarkConflictAsync(
                operation,
                "The server rejected this change because it clashes with another record.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (FleetGoApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return await MarkFailedAsync(
                operation,
                "This record no longer exists, or is no longer assigned to you.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (FleetGoApiException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            return await MarkFailedAsync(
                operation,
                "You don't have permission to change this record.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (FleetGoApiException exception) when (IsPermanent(exception.StatusCode))
        {
            // 400 and friends: the same request will be refused the same way forever, so
            // retrying it is just a slower way of never succeeding.
            return await MarkFailedAsync(
                operation,
                "The server rejected this change and retrying won't help.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (FleetGoApiException exception)
        {
            return await ScheduleRetryAsync(
                operation,
                $"The server couldn't complete this change (HTTP {(int?)exception.StatusCode}).",
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // The network went away mid-run. Back this operation off and let the caller stop:
            // the rest of the queue would only rediscover the same thing, one wasted attempt
            // each. The backoff may itself have exhausted this operation's budget, which is a
            // different piece of news and is reported as such.
            return await ScheduleRetryOnLostNetworkAsync(operation, "Couldn't reach the API.", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await ScheduleRetryOnLostNetworkAsync(operation, "The request timed out.", cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<SendOutcome> SendRouteAsync(OutboxOperation operation, StatusChangePayload payload, CancellationToken cancellationToken)
    {
        RouteResponse server = await _apiClient.GetRouteAsync(operation.EntityId, cancellationToken).ConfigureAwait(false);
        EntitySnapshot serverState = EntitySnapshot.FromRoute(server);

        if (!payload.Baseline.MatchesServerState(OutboxEntityType.Route, serverState))
        {
            await CacheServerStateAsync(server, cancellationToken).ConfigureAwait(false);
            return await MarkConflictAsync(
                operation,
                payload.Baseline.DescribeDifference(OutboxEntityType.Route, serverState),
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryParseStatus(payload.NewStatus, out RouteStatus newStatus))
        {
            return await MarkFailedAsync(operation, "That status isn't one this app recognises.", cancellationToken).ConfigureAwait(false);
        }

        // Every other field comes from what the server just said, not from the snapshot: they
        // are identical right now (that is what the check above established), and taking them
        // from the fresh response is the habit that stays correct if the check is ever
        // loosened.
        RouteResponse updated = await _apiClient.UpdateRouteAsync(
            operation.EntityId,
            new UpdateRouteRequest(server.RouteNumber, server.VehicleId, server.RouteDate, newStatus),
            cancellationToken).ConfigureAwait(false);

        await CacheServerStateAsync(updated, cancellationToken).ConfigureAwait(false);
        return await MarkCompletedAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SendOutcome> SendStopAsync(OutboxOperation operation, StatusChangePayload payload, CancellationToken cancellationToken)
    {
        StopResponse server = await _apiClient.GetStopAsync(operation.EntityId, cancellationToken).ConfigureAwait(false);
        EntitySnapshot serverState = EntitySnapshot.FromStop(server);

        if (!payload.Baseline.MatchesServerState(OutboxEntityType.Stop, serverState))
        {
            await CacheServerStateAsync(server, cancellationToken).ConfigureAwait(false);
            return await MarkConflictAsync(
                operation,
                payload.Baseline.DescribeDifference(OutboxEntityType.Stop, serverState),
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryParseStatus(payload.NewStatus, out StopStatus newStatus))
        {
            return await MarkFailedAsync(operation, "That status isn't one this app recognises.", cancellationToken).ConfigureAwait(false);
        }

        StopResponse updated = await _apiClient.UpdateStopAsync(
            operation.EntityId,
            new UpdateStopRequest(server.CustomerId, server.Sequence, newStatus, server.DeliveryNotes),
            cancellationToken).ConfigureAwait(false);

        await CacheServerStateAsync(updated, cancellationToken).ConfigureAwait(false);
        return await MarkCompletedAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SendOutcome> SendPackageAsync(OutboxOperation operation, StatusChangePayload payload, CancellationToken cancellationToken)
    {
        PackageResponse server = await _apiClient.GetPackageAsync(operation.EntityId, cancellationToken).ConfigureAwait(false);
        EntitySnapshot serverState = EntitySnapshot.FromPackage(server);

        if (!payload.Baseline.MatchesServerState(OutboxEntityType.Package, serverState))
        {
            await CacheServerStateAsync(server, cancellationToken).ConfigureAwait(false);
            return await MarkConflictAsync(
                operation,
                payload.Baseline.DescribeDifference(OutboxEntityType.Package, serverState),
                cancellationToken).ConfigureAwait(false);
        }

        if (!TryParseStatus(payload.NewStatus, out PackageStatus newStatus))
        {
            return await MarkFailedAsync(operation, "That status isn't one this app recognises.", cancellationToken).ConfigureAwait(false);
        }

        PackageResponse updated = await _apiClient.UpdatePackageAsync(
            operation.EntityId,
            new UpdatePackageRequest(server.TrackingNumber, server.Description, newStatus),
            cancellationToken).ConfigureAwait(false);

        await CacheServerStateAsync(updated, cancellationToken).ConfigureAwait(false);
        return await MarkCompletedAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    // --- pull --------------------------------------------------------------------------

    private async Task<(bool Succeeded, string? Error)> PullAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _repository.RefreshRouteWindowAsync(cancellationToken).ConfigureAwait(false);
            await PrefetchTodayAsync(cancellationToken).ConfigureAwait(false);
            return (true, null);
        }
        catch (FleetGoApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            SessionExpired?.Invoke(this, EventArgs.Empty);
            return (false, SyncResult.SessionExpired.Message);
        }
        catch (FleetGoApiException exception)
        {
            _logger.LogWarning("Refresh failed: HTTP {StatusCode}.", exception.StatusCode);
            return (false, "The API couldn't be refreshed just now. Your saved data is still here.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (false, SyncResult.Offline.Message);
        }
    }

    /// <summary>
    /// Pulls today's work down in full, so a driver who loses signal after leaving the depot
    /// still has their stops, addresses and packages.
    /// <para>
    /// Failures here are swallowed: the queue has already drained and the route list has
    /// already refreshed, and losing the prefetch is a smaller problem than reporting the
    /// whole sync as broken because one stop of twenty-five could not be read.
    /// </para>
    /// </summary>
    private async Task PrefetchTodayAsync(CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

        IReadOnlyList<LocalRoute> routes = await _store
            .GetRoutesForDateAsync(FleetRecordMapper.ToStorageDate(today), cancellationToken)
            .ConfigureAwait(false);

        int stopBudget = MaxPrefetchStops;

        foreach (LocalRoute route in routes.Take(MaxPrefetchRoutes))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (stopBudget <= 0)
            {
                break;
            }

            try
            {
                await _repository.RefreshRouteDetailAsync(route.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is FleetGoApiException or HttpRequestException or TaskCanceledException)
            {
                _logger.LogDebug("Prefetch skipped route {RouteId}.", route.Id);
                continue;
            }

            IReadOnlyList<LocalStop> stops = await _store.GetStopsForRouteAsync(route.Id, cancellationToken).ConfigureAwait(false);

            foreach (LocalStop stop in stops)
            {
                if (stopBudget-- <= 0)
                {
                    break;
                }

                try
                {
                    await _repository.RefreshStopDetailAsync(stop.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is FleetGoApiException or HttpRequestException or TaskCanceledException)
                {
                    _logger.LogDebug("Prefetch skipped stop {StopId}.", stop.Id);
                }
            }
        }
    }

    // --- outbox bookkeeping -------------------------------------------------------------

    private async Task<SendOutcome> MarkCompletedAsync(OutboxOperation operation, CancellationToken cancellationToken)
    {
        operation.Status = nameof(OutboxOperationStatus.Completed);
        operation.LastAttemptAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        operation.NextAttemptAtUtc = null;
        operation.LastError = null;
        operation.ConflictDetail = null;

        await _store.UpdateOperationAsync(operation, cancellationToken).ConfigureAwait(false);
        return SendOutcome.Sent;
    }

    private async Task<SendOutcome> MarkConflictAsync(OutboxOperation operation, string detail, CancellationToken cancellationToken)
    {
        operation.Status = nameof(OutboxOperationStatus.Conflict);
        operation.LastAttemptAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        // No next attempt, ever, without a person: retrying a conflict is how one becomes a
        // silent overwrite.
        operation.NextAttemptAtUtc = null;
        operation.ConflictDetail = detail;

        await _store.UpdateOperationAsync(operation, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("Change {OperationId} conflicted with the server's copy.", operation.Id);
        return SendOutcome.Conflict;
    }

    private async Task<SendOutcome> MarkFailedAsync(OutboxOperation operation, string reason, CancellationToken cancellationToken)
    {
        operation.Status = nameof(OutboxOperationStatus.Failed);
        operation.LastAttemptAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        operation.NextAttemptAtUtc = null;
        operation.LastError = reason;

        await _store.UpdateOperationAsync(operation, cancellationToken).ConfigureAwait(false);

        _logger.LogWarning("Change {OperationId} failed permanently.", operation.Id);
        return SendOutcome.Failed;
    }

    private async Task<SendOutcome> ScheduleRetryAsync(OutboxOperation operation, string reason, CancellationToken cancellationToken)
    {
        int attempts = operation.RetryCount + 1;
        RetryDecision decision = _retryPolicy.Next(attempts);

        operation.RetryCount = attempts;
        operation.LastAttemptAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        operation.LastError = reason;

        if (!decision.ShouldRetry)
        {
            // The budget is spent. This is the line that makes "retry until it works" finite.
            operation.Status = nameof(OutboxOperationStatus.Failed);
            operation.NextAttemptAtUtc = null;

            await _store.UpdateOperationAsync(operation, cancellationToken).ConfigureAwait(false);

            _logger.LogWarning(
                "Change {OperationId} gave up after {Attempts} attempts.",
                operation.Id,
                attempts);

            return SendOutcome.Failed;
        }

        operation.Status = nameof(OutboxOperationStatus.Pending);
        operation.NextAttemptAtUtc = _timeProvider.GetUtcNow().UtcDateTime + decision.Delay;

        await _store.UpdateOperationAsync(operation, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Change {OperationId} will be retried in {Delay} (attempt {Attempts} of {Max}).",
            operation.Id,
            decision.Delay,
            attempts,
            _retryPolicy.MaxAttempts);

        return SendOutcome.Retrying;
    }

    /// <summary>
    /// Backs an operation off after a transport failure, and reports both what happened to it
    /// and the fact that the run should stop.
    /// </summary>
    private async Task<SendOutcome> ScheduleRetryOnLostNetworkAsync(
        OutboxOperation operation,
        string reason,
        CancellationToken cancellationToken)
    {
        SendOutcome outcome = await ScheduleRetryAsync(operation, reason, cancellationToken).ConfigureAwait(false);

        return outcome == SendOutcome.Failed ? SendOutcome.FailedAndNetworkLost : SendOutcome.NetworkLost;
    }

    private async Task PruneAsync(CancellationToken cancellationToken)
    {
        DateTime cutoff = _timeProvider.GetUtcNow().UtcDateTime - CompletedRetention;
        int pruned = await _store.PruneCompletedOperationsAsync(cutoff, cancellationToken).ConfigureAwait(false);

        if (pruned > 0)
        {
            _logger.LogDebug("Pruned {Count} completed operations.", pruned);
        }
    }

    // --- cache write-back ----------------------------------------------------------------
    //
    // Writing the server's own answer straight back into the cache is what keeps the
    // repository's invariant true: a cached row is server truth unless a pending operation
    // overlays it. It matters most on the conflict path - without it the screen would keep
    // showing the driver's rejected value with no way to find out what the server actually
    // says.

    private Task CacheServerStateAsync(RouteResponse route, CancellationToken cancellationToken) =>
        _store.UpsertRouteAsync(FleetRecordMapper.ToLocal(route, _timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

    private Task CacheServerStateAsync(StopResponse stop, CancellationToken cancellationToken) =>
        _store.UpsertStopAsync(FleetRecordMapper.ToLocal(stop, _timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

    private Task CacheServerStateAsync(PackageResponse package, CancellationToken cancellationToken) =>
        _store.UpsertPackageAsync(FleetRecordMapper.ToLocal(package, _timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

    // --- status ---------------------------------------------------------------------------

    private async Task<int> CountPendingAsync(CancellationToken cancellationToken) =>
        await _store.CountOperationsAsync(OutboxOperationStatus.Pending, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Recomputes the snapshot from the database and tells anyone watching.
    /// <para>
    /// The counts are read back rather than tracked in a field on purpose: a field would be a
    /// second copy of the truth, and the one place it would drift is exactly where it matters
    /// - a queue that says "0 waiting" while a change sits unsent.
    /// </para>
    /// </summary>
    private async Task PublishStatusAsync(bool isSyncing, string? lastError, CancellationToken cancellationToken)
    {
        int pending = 0;
        int conflicts = 0;
        int failed = 0;
        DateTime? lastSync = null;

        try
        {
            IReadOnlyList<OutboxOperation> unresolved = await _store.GetUnresolvedOperationsAsync(cancellationToken).ConfigureAwait(false);

            foreach (OutboxOperation operation in unresolved)
            {
                if (string.Equals(operation.Status, nameof(OutboxOperationStatus.Conflict), StringComparison.Ordinal))
                {
                    conflicts++;
                }
                else if (string.Equals(operation.Status, nameof(OutboxOperationStatus.Failed), StringComparison.Ordinal))
                {
                    failed++;
                }
                else
                {
                    pending++;
                }
            }

            SyncMetadata metadata = await _store.GetMetadataAsync(cancellationToken).ConfigureAwait(false);
            lastSync = metadata.LastSuccessfulSyncUtc;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The status banner is not worth failing a sync over.
            _logger.LogDebug(exception, "Could not read sync status from the local store.");
        }

        _current = new SyncSnapshot(
            _connectivity.IsOnline,
            isSyncing,
            pending,
            conflicts,
            failed,
            lastSync,
            lastError);

        Changed?.Invoke(this, _current);
    }

    private static bool IsPermanent(HttpStatusCode? statusCode) =>
        statusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.UnprocessableEntity
            or HttpStatusCode.MethodNotAllowed
            or HttpStatusCode.UnsupportedMediaType;

    private static bool TryParseStatus<TStatus>(string value, out TStatus status)
        where TStatus : struct, Enum =>
        Enum.TryParse(value, ignoreCase: false, out status) && Enum.IsDefined(status);
}
