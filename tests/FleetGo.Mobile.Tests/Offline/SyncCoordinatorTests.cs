using FleetGo.Mobile.Core.Offline.Connectivity;
using FleetGo.Mobile.Core.Offline.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// When a sync happens. The heartbeat is pushed out of the way in these tests (a one-hour
/// interval) so that what is being asserted is the event-driven triggers, which are the ones
/// that actually matter to a driver.
/// </summary>
public class SyncCoordinatorTests
{
    private static readonly SyncCoordinatorOptions NoHeartbeat = new(TimeSpan.FromHours(1));

    [Fact]
    public async Task Start_SyncsImmediatelyWhenASessionIsAlreadyRestored()
    {
        var harness = new Harness();
        harness.Authentication.SignIn();

        await using SyncCoordinator coordinator = harness.Build();
        coordinator.Start();
        await coordinator.WhenIdleAsync();

        // Launch order is not something to rely on: a session restored before the coordinator
        // was wired up must not wait for the next trigger.
        AssertThat.Sequence([SyncReason.SignedIn], harness.Sync.Reasons);
    }

    [Fact]
    public async Task TheNetworkComingBack_DrainsTheQueue()
    {
        var harness = new Harness(NetworkStatus.Offline);
        harness.Authentication.SignIn();

        await using SyncCoordinator coordinator = harness.Build();
        coordinator.Start();
        await coordinator.WhenIdleAsync();

        harness.Sync.Reasons.Clear();
        harness.Connectivity.GoOnline();
        await coordinator.WhenIdleAsync();

        AssertThat.Sequence([SyncReason.ConnectivityRestored], harness.Sync.Reasons);
    }

    [Fact]
    public async Task TheNetworkComingBack_DoesNothingWhenNobodyIsSignedIn()
    {
        var harness = new Harness(NetworkStatus.Offline);

        await using SyncCoordinator coordinator = harness.Build();
        coordinator.Start();

        harness.Connectivity.GoOnline();
        await coordinator.WhenIdleAsync();

        Assert.Empty(harness.Sync.Reasons);
    }

    [Fact]
    public async Task SigningIn_BindsTheCacheToThatDriverAndSyncs()
    {
        var harness = new Harness();

        await using SyncCoordinator coordinator = harness.Build();
        coordinator.Start();

        Guid driverId = Guid.NewGuid();
        harness.Authentication.SignIn(driverId);
        await coordinator.WhenIdleAsync();

        Assert.Equal(driverId, harness.Repository.PreparedDriverId);
        AssertThat.Sequence([SyncReason.SignedIn], harness.Sync.Reasons);
    }

    [Fact]
    public async Task SigningOut_WipesTheDevice()
    {
        var harness = new Harness();
        harness.Authentication.SignIn();

        await using SyncCoordinator coordinator = harness.Build();
        coordinator.Start();
        await coordinator.WhenIdleAsync();

        await harness.Authentication.LogoutAsync();
        await coordinator.WhenIdleAsync();

        // This is the path that covers the sessions nobody ends deliberately - a revoked or
        // expired refresh token - not just the sign-out button.
        Assert.Equal(1, harness.Repository.ClearCount);
    }

    [Fact]
    public async Task AppResumed_AsksForASync()
    {
        var harness = new Harness();
        harness.Authentication.SignIn();

        await using SyncCoordinator coordinator = harness.Build();
        coordinator.Start();
        await coordinator.WhenIdleAsync();

        harness.Sync.Reasons.Clear();
        coordinator.NotifyAppResumed();
        await coordinator.WhenIdleAsync();

        AssertThat.Sequence([SyncReason.AppResumed], harness.Sync.Reasons);
    }

    [Fact]
    public async Task ASyncThatBlowsUp_IsReportedRatherThanThrown()
    {
        var harness = new Harness();
        harness.Authentication.SignIn();
        harness.Sync.Throw = new InvalidOperationException("something unexpected");

        await using SyncCoordinator coordinator = harness.Build();

        SyncResult result = await coordinator.SyncNowAsync(SyncReason.Manual);

        // Background work has no business taking the app down with it.
        Assert.Equal(SyncOutcome.Failed, result.Outcome);
    }

    private sealed class Harness
    {
        public Harness(NetworkStatus status = NetworkStatus.Online) =>
            Connectivity = new FakeConnectivityService(status);

        public FakeConnectivityService Connectivity { get; }

        public FakeAuthenticationService Authentication { get; } = new();

        public FakeFleetRepository Repository { get; } = new();

        public RecordingSyncService Sync { get; } = new();

        public SyncCoordinator Build() =>
            new(
                Sync,
                Repository,
                Authentication,
                Connectivity,
                TimeProvider.System,
                NullLogger<SyncCoordinator>.Instance,
                NoHeartbeat);
    }

    private sealed class RecordingSyncService : ISyncService
    {
        public List<SyncReason> Reasons { get; } = [];

        public Exception? Throw { get; set; }

        public event EventHandler? SessionExpired;

        public Task<SyncResult> SyncAsync(SyncReason reason, CancellationToken cancellationToken = default)
        {
            Reasons.Add(reason);

            if (Throw is { } exception)
            {
                return Task.FromException<SyncResult>(exception);
            }

            return Task.FromResult(new SyncResult(SyncOutcome.Succeeded));
        }

        public Task RefreshStatusAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>Unused by these tests, but present so the interface's event is genuinely implemented rather than declared and abandoned.</summary>
        public void RaiseSessionExpired() => SessionExpired?.Invoke(this, EventArgs.Empty);
    }
}
