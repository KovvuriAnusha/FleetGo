using FleetGo.Mobile.Core.Offline.Connectivity;
using FleetGo.Mobile.Core.Session;
using Microsoft.Extensions.Logging;

namespace FleetGo.Mobile.Core.Offline.Sync;

/// <summary>When the coordinator decides to sync.</summary>
/// <param name="Interval">
/// The heartbeat, for a long shift where nothing else happens. Deliberately minutes, not
/// seconds: this runs on a phone in someone's pocket, and the expensive triggers (signal
/// returning, the app coming back to the foreground, a change being made) already cover the
/// moments that matter.
/// </param>
public sealed record SyncCoordinatorOptions(TimeSpan Interval)
{
    public static SyncCoordinatorOptions Default { get; } = new(TimeSpan.FromMinutes(5));
}

/// <summary>
/// Decides <em>when</em> to sync. <see cref="ISyncService"/> decides how.
/// </summary>
/// <remarks>
/// <para>
/// Four triggers, each chosen because it marks a moment when the answer to "is there anything
/// to do?" has genuinely changed: a session starting, the network coming back, the app
/// returning to the foreground, and a periodic heartbeat for the long quiet stretches. A
/// change being queued asks for a sync too, from whoever queued it - there is no point waiting
/// five minutes to send something the device could send now.
/// </para>
/// <para>
/// <b>What this is not.</b> It is not OS-level background execution. Android's WorkManager and
/// iOS's BGTaskScheduler can run work while the app is closed, and a delivery app would
/// eventually want that - but both need platform scheduling code, per-platform entitlements,
/// and a story about what the driver is told when a queued change fails with nobody watching.
/// Phase 5 stops short of that on purpose and says so, rather than shipping a timer and calling
/// it background sync. Everything here runs while the app is alive; a change queued and left
/// unsent is sent the next time the app is opened, which is the guarantee the UI actually makes.
/// </para>
/// </remarks>
public sealed class SyncCoordinator : ISyncTrigger, IDisposable, IAsyncDisposable
{
    private readonly ISyncService _syncService;
    private readonly IFleetRepository _repository;
    private readonly IAuthenticationService _authentication;
    private readonly IConnectivityService _connectivity;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SyncCoordinator> _logger;
    private readonly SyncCoordinatorOptions _options;

    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Guards <see cref="_inFlight"/> only - triggers arrive from timers, OS callbacks and the UI thread alike.</summary>
    private readonly object _inFlightGate = new();

    private Task _inFlight = Task.CompletedTask;
    private Task? _heartbeat;
    private bool _started;
    private bool _tornDown;

    public SyncCoordinator(
        ISyncService syncService,
        IFleetRepository repository,
        IAuthenticationService authentication,
        IConnectivityService connectivity,
        TimeProvider timeProvider,
        ILogger<SyncCoordinator> logger,
        SyncCoordinatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(connectivity);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _syncService = syncService;
        _repository = repository;
        _authentication = authentication;
        _connectivity = connectivity;
        _timeProvider = timeProvider;
        _logger = logger;
        _options = options ?? SyncCoordinatorOptions.Default;
    }

    /// <summary>
    /// Subscribes to the triggers and starts the heartbeat. Called once, from the app's
    /// startup path.
    /// </summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        _authentication.AuthenticationStateChanged += OnAuthenticationStateChanged;
        _connectivity.StatusChanged += OnConnectivityChanged;

        _heartbeat = RunHeartbeatAsync(_shutdown.Token);

        // A session may already have been restored before this was wired up (launch order is
        // not something to rely on), so ask once rather than waiting for the next trigger.
        if (_authentication.IsAuthenticated)
        {
            RequestSync(SyncReason.SignedIn);
        }
    }

    /// <summary>Called by the app's lifecycle hook when it returns to the foreground.</summary>
    public void NotifyAppResumed() => RequestSync(SyncReason.AppResumed);

    /// <inheritdoc />
    public async Task<SyncResult> SyncNowAsync(SyncReason reason, CancellationToken cancellationToken = default)
    {
        try
        {
            await PrepareSessionAsync(cancellationToken).ConfigureAwait(false);
            return await _syncService.SyncAsync(reason, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A sync is background work. It has no business taking the app down, and the
            // screens already show what the queue looks like.
            _logger.LogError(exception, "Sync failed unexpectedly.");
            return new SyncResult(SyncOutcome.Failed, Message: "Something went wrong while syncing.");
        }
    }

    /// <inheritdoc />
    public void RequestSync(SyncReason reason)
    {
        lock (_inFlightGate)
        {
            _inFlight = RunAsync(reason);
        }
    }

    /// <summary>
    /// Completes when the most recently requested sync has finished. A test seam: the
    /// triggers are events, and a test that raises one needs somewhere to await.
    /// </summary>
    public Task WhenIdleAsync()
    {
        lock (_inFlightGate)
        {
            return _inFlight;
        }
    }

    /// <summary>
    /// Synchronous teardown: stop listening, and tell the heartbeat to stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because the app's container disposes its singletons synchronously, and
    /// <c>ServiceProvider.Dispose()</c> throws outright on a singleton that offers only
    /// <see cref="IAsyncDisposable"/> - so an async-only coordinator turns a clean shutdown
    /// into an exception. The same is true of the local store, which implements both for the
    /// same reason.
    /// </para>
    /// <para>
    /// It deliberately does not wait for the heartbeat to unwind. Nothing here needs to be
    /// awaited for correctness - the heartbeat holds no handle a later caller could collide
    /// with, and its <c>PeriodicTimer</c> is disposed by its own <c>using</c> as the loop
    /// exits - so blocking an app-shutdown path on background work would buy tidiness at the
    /// risk of a hang. For the same reason the token source is left to the GC here rather
    /// than disposed while the heartbeat may still be unwinding on its token;
    /// <see cref="DisposeAsync"/> waits and then releases it properly.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_tornDown)
        {
            return;
        }

        _tornDown = true;

        if (_started)
        {
            _authentication.AuthenticationStateChanged -= OnAuthenticationStateChanged;
            _connectivity.StatusChanged -= OnConnectivityChanged;
            _started = false;
        }

        _shutdown.Cancel();
    }

    /// <summary>
    /// Teardown for callers that can wait: the same work as <see cref="Dispose"/>, plus
    /// waiting for the heartbeat to finish so the token source can be released and a test can
    /// assert against a coordinator that has genuinely stopped.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();

        if (_heartbeat is { } heartbeat)
        {
            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            _heartbeat = null;
        }

        _shutdown.Dispose();
    }

    private async Task RunAsync(SyncReason reason)
    {
        try
        {
            await SyncNowAsync(reason, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down.
        }
    }

    private async Task RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        // Constructed with the injected TimeProvider so tests can advance time rather than
        // wait five real minutes for one assertion.
        using PeriodicTimer timer = new(_options.Interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_authentication.IsAuthenticated && _connectivity.IsOnline)
                {
                    await SyncNowAsync(SyncReason.Periodic, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    /// <summary>
    /// Binds the cache to whoever is signed in, or wipes it when nobody is.
    /// <para>
    /// The sign-out case matters more than it looks: the explicit sign-out path clears the
    /// cache itself, but a session can also end without anyone pressing anything - a refresh
    /// token that has been revoked, or one that expired while the app was closed. Those paths
    /// go through the same state change, so they end up here, and the next driver on this
    /// device does not inherit a database full of someone else's stops.
    /// </para>
    /// </summary>
    private async Task PrepareSessionAsync(CancellationToken cancellationToken)
    {
        if (!_authentication.IsAuthenticated)
        {
            await _repository.ClearAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_authentication.CurrentUser?.DriverId is { } driverId)
        {
            await _repository.PrepareForDriverAsync(driverId, cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnAuthenticationStateChanged(object? sender, EventArgs e)
    {
        if (_authentication.IsAuthenticated)
        {
            RequestSync(SyncReason.SignedIn);
            return;
        }

        lock (_inFlightGate)
        {
            _inFlight = ClearAfterSignOutAsync();
        }
    }

    private async Task ClearAfterSignOutAsync()
    {
        try
        {
            await _repository.ClearAsync(_shutdown.Token).ConfigureAwait(false);
            _logger.LogInformation("Local cache cleared after sign-out.");
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not clear the local cache after sign-out.");
        }
    }

    private void OnConnectivityChanged(object? sender, NetworkStatus status)
    {
        if (status == NetworkStatus.Online && _authentication.IsAuthenticated)
        {
            RequestSync(SyncReason.ConnectivityRestored);
        }
    }
}
