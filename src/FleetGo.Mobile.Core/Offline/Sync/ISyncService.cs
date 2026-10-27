namespace FleetGo.Mobile.Core.Offline.Sync;

/// <summary>Why a sync was started. Recorded in the log so a device's behaviour can be read back afterwards.</summary>
public enum SyncReason
{
    /// <summary>The driver pulled to refresh, or pressed a retry button.</summary>
    Manual,

    /// <summary>A session just started, or was restored at launch.</summary>
    SignedIn,

    /// <summary>The network came back.</summary>
    ConnectivityRestored,

    /// <summary>The app came back to the foreground.</summary>
    AppResumed,

    /// <summary>A change was just queued and there may be a network to send it on.</summary>
    ChangeQueued,

    /// <summary>The periodic check, for a long shift where nothing else has happened.</summary>
    Periodic,
}

/// <summary>
/// Drains the outbox and refreshes the cache.
/// <para>
/// Everything about when this runs lives in <c>SyncCoordinator</c>; this interface is only
/// concerned with doing one run properly, which is what makes it testable without timers.
/// </para>
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Runs one sync: send what is queued, then refresh what the driver is likely to need.
    /// Safe to call from anywhere at any time - a second concurrent call returns
    /// <see cref="SyncOutcome.AlreadyRunning"/> rather than doing the work twice.
    /// </summary>
    Task<SyncResult> SyncAsync(SyncReason reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the queue counts and last-sync time from the database without touching the
    /// network, so the UI can show an accurate banner the moment a screen opens.
    /// </summary>
    Task RefreshStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the API rejects the session during a sync. Nothing was sent; the queue is
    /// intact and waits for the driver to sign in again.
    /// </summary>
    event EventHandler? SessionExpired;
}
