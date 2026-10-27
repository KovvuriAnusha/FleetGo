using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.ViewModels;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// The banner's wording. These read like copy tests, and that is the point: this is the only
/// place the app makes a claim about how current its data is, and a claim that is too
/// optimistic is a bug even when every other line of code is correct.
/// </summary>
public class SyncStatusViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Offline_WithUnsentWork_SaysSoRatherThanClaimingItIsSaved()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        provider.Publish(Snapshot(isOnline: false, pending: 2, lastSync: Now.UtcDateTime.AddMinutes(-10)));

        Assert.True(banner.IsBannerVisible);
        Assert.Equal("Offline. 2 changes waiting to send.", banner.StatusText);
        Assert.Equal("Last synced 10 minutes ago", banner.LastSyncedText);
        Assert.False(banner.NeedsAttention);
    }

    [Fact]
    public void Offline_WithNothingQueued_StillSaysTheDataIsSaved()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        provider.Publish(Snapshot(isOnline: false, lastSync: Now.UtcDateTime.AddHours(-3)));

        Assert.Equal("Offline. Showing saved data.", banner.StatusText);
        Assert.Equal("Last synced 3 hours ago", banner.LastSyncedText);
    }

    [Fact]
    public void Conflicts_AskForAPersonRatherThanPromisingARetry()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        provider.Publish(Snapshot(isOnline: true, conflicts: 1, lastSync: Now.UtcDateTime));

        Assert.True(banner.NeedsAttention);
        Assert.Equal("1 change clashed with the server and needs you to decide.", banner.StatusText);
    }

    [Fact]
    public void Failures_AreReportedEvenThoughNothingWillRetryThem()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        provider.Publish(Snapshot(isOnline: true, failed: 2, lastSync: Now.UtcDateTime));

        Assert.True(banner.NeedsAttention);
        Assert.Equal("2 changes couldn't be saved.", banner.StatusText);
    }

    [Fact]
    public void NeverSynced_IsAdmittedRatherThanShownAsUpToDate()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        provider.Publish(Snapshot(isOnline: true));

        Assert.Equal("Not synced yet.", banner.StatusText);
        Assert.Equal("Not synced yet", banner.LastSyncedText);
        Assert.True(banner.IsBannerVisible);
    }

    [Fact]
    public void UpToDateAndOnline_HasNothingWorthSaying()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        provider.Publish(Snapshot(isOnline: true, lastSync: Now.UtcDateTime.AddSeconds(-20)));

        Assert.Equal("Up to date.", banner.StatusText);
        Assert.Equal("Last synced just now", banner.LastSyncedText);
        Assert.False(banner.IsBannerVisible);
    }

    [Fact]
    public void Syncing_TakesPrecedenceOverEverythingElse()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        provider.Publish(Snapshot(isOnline: true, isSyncing: true, pending: 3, lastSync: Now.UtcDateTime));

        Assert.Equal("Syncing…", banner.StatusText);
        Assert.True(banner.IsBannerVisible);
    }

    [Fact]
    public void AClockThatWentBackwards_DoesNotProduceANegativeAge()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        provider.Publish(Snapshot(isOnline: true, lastSync: Now.UtcDateTime.AddMinutes(5)));

        Assert.Equal("Last synced just now", banner.LastSyncedText);
    }

    [Fact]
    public async Task SyncNow_AsksForAManualSync()
    {
        var trigger = new FakeSyncTrigger();
        (SyncStatusViewModel banner, _) = Build(trigger);

        await banner.SyncNowCommand.ExecuteAsync(null);

        AssertThat.Sequence([SyncReason.Manual], trigger.AwaitedReasons);
    }

    [Fact]
    public void Dispose_StopsListening()
    {
        (SyncStatusViewModel banner, FakeSyncStatusProvider provider) = Build();

        banner.Dispose();
        provider.Publish(Snapshot(isOnline: false, pending: 9));

        // A banner that has been disposed should not still be repainting itself; the leak
        // this guards against is a screen's subscription outliving the screen.
        Assert.Equal(0, banner.PendingCount);
    }

    private static SyncSnapshot Snapshot(
        bool isOnline,
        bool isSyncing = false,
        int pending = 0,
        int conflicts = 0,
        int failed = 0,
        DateTime? lastSync = null,
        string? lastError = null) =>
        new(isOnline, isSyncing, pending, conflicts, failed, lastSync, lastError);

    private static (SyncStatusViewModel Banner, FakeSyncStatusProvider Provider) Build(FakeSyncTrigger? trigger = null)
    {
        var provider = new FakeSyncStatusProvider();
        SyncStatusViewModel banner = OfflineTestFactory.SyncStatus(provider, trigger, new FakeTimeProvider(Now));
        return (banner, provider);
    }
}
