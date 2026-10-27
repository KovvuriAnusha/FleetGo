using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.Threading;
using FleetGo.Mobile.Core.ViewModels;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// Builders for the pieces every Phase 5 view model now needs, so a test that is about the
/// dashboard does not open with six lines of sync plumbing.
/// </summary>
internal static class OfflineTestFactory
{
    public static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A banner backed by fakes. <see cref="ImmediateUiDispatcher"/> rather than a real one:
    /// there is no UI thread in a test host, and posting to a queue nothing pumps would mean
    /// the assertions ran before the properties changed.
    /// </summary>
    public static SyncStatusViewModel SyncStatus(
        FakeSyncStatusProvider? provider = null,
        FakeSyncTrigger? trigger = null,
        TimeProvider? timeProvider = null) =>
        new(
            provider ?? new FakeSyncStatusProvider(),
            trigger ?? new FakeSyncTrigger(),
            ImmediateUiDispatcher.Instance,
            timeProvider ?? new FakeTimeProvider(Now));
}
