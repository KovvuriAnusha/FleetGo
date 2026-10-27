namespace FleetGo.Mobile.Core.Offline.Sync;

/// <summary>How a sync run ended.</summary>
public enum SyncOutcome
{
    /// <summary>Everything queued was sent and the cache was refreshed.</summary>
    Succeeded,

    /// <summary>
    /// The run finished, but something in the queue did not: an operation is in conflict or
    /// has run out of attempts. Deliberately not <see cref="Succeeded"/> - a queue with a
    /// stuck item in it is not a synced device, and the UI should not claim otherwise.
    /// </summary>
    CompletedWithProblems,

    /// <summary>A run was already in progress; this call did nothing.</summary>
    AlreadyRunning,

    /// <summary>No network, or the network turned out not to reach the API.</summary>
    Offline,

    /// <summary>Nobody is signed in, so there is no authenticated caller to sync as.</summary>
    NotSignedIn,

    /// <summary>
    /// The API rejected the session. Nothing was sent; the queue is untouched and waits for
    /// the driver to sign in again.
    /// </summary>
    SessionExpired,

    /// <summary>The API answered, but with something the app could not work with.</summary>
    Failed,
}

/// <summary>What one sync run did.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="SentCount">Operations the server accepted during this run.</param>
/// <param name="ConflictCount">Operations that ended in conflict during this run.</param>
/// <param name="FailedCount">Operations that ran out of attempts, or were rejected outright, during this run.</param>
/// <param name="RemainingCount">Operations still waiting when the run ended.</param>
/// <param name="Message">
/// A short, driver-readable explanation, or null when there is nothing worth saying. Never
/// contains customer details, delivery notes or anything from the session.
/// </param>
public sealed record SyncResult(
    SyncOutcome Outcome,
    int SentCount = 0,
    int ConflictCount = 0,
    int FailedCount = 0,
    int RemainingCount = 0,
    string? Message = null)
{
    public static SyncResult AlreadyRunning { get; } = new(SyncOutcome.AlreadyRunning);

    public static SyncResult NotSignedIn { get; } = new(SyncOutcome.NotSignedIn);

    public static SyncResult Offline { get; } =
        new(SyncOutcome.Offline, Message: "You're offline. Changes will be sent when you're back on a network.");

    public static SyncResult SessionExpired { get; } =
        new(SyncOutcome.SessionExpired, Message: "Your session has expired. Sign in again to send your changes.");
}

/// <summary>
/// Everything the UI needs to describe the sync situation truthfully, in one immutable value.
/// </summary>
/// <param name="IsOnline">What the device believes about its network.</param>
/// <param name="IsSyncing">Whether a run is in progress right now.</param>
/// <param name="PendingCount">Changes queued and still expected to succeed.</param>
/// <param name="ConflictCount">Changes the server's copy has moved out from under - a person has to settle these.</param>
/// <param name="FailedCount">Changes that will not be retried.</param>
/// <param name="LastSuccessfulSyncUtc">When the cache was last refreshed from the server. Null means never, on this device, for this driver.</param>
/// <param name="LastError">The most recent problem, in words a driver can act on. Null when the last run was clean.</param>
public sealed record SyncSnapshot(
    bool IsOnline,
    bool IsSyncing,
    int PendingCount,
    int ConflictCount,
    int FailedCount,
    DateTime? LastSuccessfulSyncUtc,
    string? LastError)
{
    /// <summary>The state before anything has been looked at - assumed offline, because assuming online would let the UI claim a freshness it has not checked.</summary>
    public static SyncSnapshot Unknown { get; } = new(false, false, 0, 0, 0, null, null);

    /// <summary>True when there is anything at all waiting or stuck.</summary>
    public bool HasUnsentWork => PendingCount > 0 || ConflictCount > 0 || FailedCount > 0;

    /// <summary>True when something needs a person rather than another retry.</summary>
    public bool NeedsAttention => ConflictCount > 0 || FailedCount > 0;
}

/// <summary>
/// The current sync situation, and a notification when it changes. Separated from
/// <see cref="ISyncService"/> so a screen can watch the state without being handed the ability
/// to start a sync.
/// </summary>
public interface ISyncStatusProvider
{
    SyncSnapshot Current { get; }

    /// <summary>Raised on whatever thread the change happened on - subscribers that touch UI must marshal.</summary>
    event EventHandler<SyncSnapshot>? Changed;
}
