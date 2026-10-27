using FleetGo.Mobile.Core.Offline.Sync;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// Records the syncs a view model asked for without running any. What the tests care about is
/// that a pull-to-refresh asks, and that queueing a change nudges the engine - not what the
/// engine then does, which <see cref="SyncServiceTests"/> covers on its own.
/// </summary>
internal sealed class FakeSyncTrigger : ISyncTrigger
{
    public List<SyncReason> AwaitedReasons { get; } = [];

    public List<SyncReason> RequestedReasons { get; } = [];

    public SyncResult Result { get; set; } = new(SyncOutcome.Succeeded);

    public Task<SyncResult> SyncNowAsync(SyncReason reason, CancellationToken cancellationToken = default)
    {
        AwaitedReasons.Add(reason);
        return Task.FromResult(Result);
    }

    public void RequestSync(SyncReason reason) => RequestedReasons.Add(reason);
}

/// <summary>Publishes whatever snapshot a test hands it, so the banner can be driven directly.</summary>
internal sealed class FakeSyncStatusProvider : ISyncStatusProvider
{
    public event EventHandler<SyncSnapshot>? Changed;

    public SyncSnapshot Current { get; private set; } = SyncSnapshot.Unknown;

    public void Publish(SyncSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(this, snapshot);
    }
}
