namespace FleetGo.Mobile.Core.Offline.Sync;

/// <summary>
/// Asks for a sync. The two shapes a caller ever needs: wait for it, or just say it should
/// happen.
/// <para>
/// Separate from <see cref="ISyncService"/> - which does the work - and from
/// <c>SyncCoordinator</c> - which decides when. A view model wants neither of those; it wants
/// "the driver pulled down, go", and depending on this rather than the coordinator keeps the
/// screens out of the business of session preparation, timers and event subscriptions.
/// </para>
/// </summary>
public interface ISyncTrigger
{
    /// <summary>
    /// Runs a sync and waits for the outcome. Never throws for an ordinary failure - the
    /// result says what happened - so a pull-to-refresh has nothing to catch.
    /// </summary>
    Task<SyncResult> SyncNowAsync(SyncReason reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for a sync without waiting. Used right after a change is queued: the change is
    /// already durable, and making the driver watch a spinner to find that out would undo the
    /// point of writing it to the device first.
    /// </summary>
    void RequestSync(SyncReason reason);
}
