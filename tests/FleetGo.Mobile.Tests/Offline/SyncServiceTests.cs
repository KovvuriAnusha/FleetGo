using System.Net;
using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Connectivity;
using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// The sync engine's push path: what it sends, what it refuses to send, and what it does with
/// each kind of failure.
/// </summary>
/// <remarks>
/// These use the real store and a real <see cref="FleetRepository"/> to <em>queue</em> the
/// change, so the payload under test is the one the app actually writes rather than one the
/// test invented. The repository handed to the sync engine itself is a fake, which keeps the
/// pull phase out of the way of assertions about the push.
/// </remarks>
public class SyncServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sync_SendsAQueuedChangeComposedFromFreshServerState()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);

        SyncResult result = await harness.Sync.SyncAsync(SyncReason.Manual);

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.Equal(1, result.SentCount);

        // Read before write: the record is fetched, checked, and then sent back whole - with
        // the fields this app is not changing taken from what the server just said, not from
        // the stale snapshot.
        (Guid stopId, UpdateStopRequest request) = Assert.Single(harness.Api.StopUpdates);
        Assert.Equal(harness.Stop.Id, stopId);
        Assert.Equal(StopStatus.Completed, request.Status);
        Assert.Equal(harness.Stop.CustomerId, request.CustomerId);
        Assert.Equal(harness.Stop.Sequence, request.Sequence);
        Assert.Equal(harness.Stop.DeliveryNotes, request.DeliveryNotes);

        Assert.Empty(await harness.Store.GetUnresolvedOperationsAsync());
    }

    [Fact]
    public async Task Sync_WhenTheServersCopyHasMoved_RecordsAConflictAndSendsNothing()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);

        // Dispatch marked it failed while the driver was out of signal.
        harness.ServerStop = harness.Stop with
        {
            Status = StopStatus.Failed,
            UpdatedAtUtc = harness.Stop.UpdatedAtUtc.AddMinutes(30),
        };

        SyncResult result = await harness.Sync.SyncAsync(SyncReason.Manual);

        Assert.Equal(SyncOutcome.CompletedWithProblems, result.Outcome);
        Assert.Equal(1, result.ConflictCount);
        Assert.Empty(harness.Api.StopUpdates);

        OutboxOperation operation = Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Equal(nameof(OutboxOperationStatus.Conflict), operation.Status);
        Assert.Contains("Failed", operation.ConflictDetail!);

        // Never retried on its own - and never scheduled to be.
        Assert.Null(operation.NextAttemptAtUtc);
        Assert.Empty(await harness.Store.GetSendableOperationsAsync(Now.UtcDateTime.AddDays(7)));

        // The screen now shows what the server actually says, rather than the driver's
        // rejected value with nothing to explain it.
        Assert.Equal(nameof(StopStatus.Failed), (await harness.Store.GetStopAsync(harness.Stop.Id))!.Status);
    }

    [Fact]
    public async Task Sync_WhenTheServerAnswers409_RecordsAConflictRatherThanRetrying()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);

        harness.Api.UpdateStopHandler = (_, _, _) =>
            Task.FromException<StopResponse>(new FleetGoApiException("clash", HttpStatusCode.Conflict));

        SyncResult result = await harness.Sync.SyncAsync(SyncReason.Manual);

        Assert.Equal(1, result.ConflictCount);

        OutboxOperation operation = Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Equal(nameof(OutboxOperationStatus.Conflict), operation.Status);
        Assert.Equal(0, operation.RetryCount);
    }

    [Fact]
    public async Task Sync_WhenTheSessionIsRejected_StopsAndLeavesTheQueueIntact()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);

        harness.Api.GetStopHandler = (_, _) =>
            Task.FromException<StopResponse>(new FleetGoApiException("expired", HttpStatusCode.Unauthorized));

        bool sessionExpiredRaised = false;
        harness.Sync.SessionExpired += (_, _) => sessionExpiredRaised = true;

        SyncResult result = await harness.Sync.SyncAsync(SyncReason.Manual);

        Assert.Equal(SyncOutcome.SessionExpired, result.Outcome);
        Assert.True(sessionExpiredRaised);

        // The queue is untouched: the change still requires a real authenticated caller, and
        // nothing here tries to manufacture one.
        OutboxOperation operation = Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Equal(nameof(OutboxOperationStatus.Pending), operation.Status);
        Assert.Equal(0, operation.RetryCount);
        Assert.Null(operation.NextAttemptAtUtc);

        // And the pull phase never ran, so nothing overwrote the cache under a dead session.
        Assert.Equal(0, harness.Repository.RouteWindowRefreshCount);
    }

    [Fact]
    public async Task Sync_WhenTheNetworkDies_BacksTheChangeOffAndStops()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);

        harness.Api.GetStopHandler = (_, _) => Task.FromException<StopResponse>(new HttpRequestException("gone"));

        SyncResult result = await harness.Sync.SyncAsync(SyncReason.Manual);

        Assert.Equal(SyncOutcome.Offline, result.Outcome);

        OutboxOperation operation = Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Equal(nameof(OutboxOperationStatus.Pending), operation.Status);
        Assert.Equal(1, operation.RetryCount);
        Assert.NotNull(operation.NextAttemptAtUtc);
        Assert.True(operation.NextAttemptAtUtc > Now.UtcDateTime);
    }

    [Fact]
    public async Task Sync_GivesUpOnceTheAttemptBudgetIsSpent()
    {
        // Two attempts allowed, so the second failure is the last one.
        await using SyncHarness harness = await SyncHarness.CreateAsync(
            new RetryPolicyOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), MaxAttempts: 2));

        await harness.QueueStopStatusAsync(StopStatus.Completed);

        harness.Api.GetStopHandler = (_, _) => Task.FromException<StopResponse>(new HttpRequestException("gone"));

        await harness.Sync.SyncAsync(SyncReason.Manual);
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        SyncResult second = await harness.Sync.SyncAsync(SyncReason.Manual);

        Assert.Equal(1, second.FailedCount);

        OutboxOperation operation = Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Equal(nameof(OutboxOperationStatus.Failed), operation.Status);
        Assert.Null(operation.NextAttemptAtUtc);

        // Nothing will pick this up again on its own - which is the whole point of having a
        // budget rather than retrying forever.
        Assert.Empty(await harness.Store.GetSendableOperationsAsync(Now.UtcDateTime.AddYears(1)));
    }

    [Fact]
    public async Task Sync_DoesNotRetryAnythingTheServerWillAlwaysRefuse()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);

        harness.Api.UpdateStopHandler = (_, _, _) =>
            Task.FromException<StopResponse>(new FleetGoApiException("invalid", HttpStatusCode.BadRequest));

        SyncResult result = await harness.Sync.SyncAsync(SyncReason.Manual);

        Assert.Equal(1, result.FailedCount);

        OutboxOperation operation = Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Equal(nameof(OutboxOperationStatus.Failed), operation.Status);
        Assert.Equal(0, operation.RetryCount);
    }

    [Fact]
    public async Task Sync_WhenOffline_DoesNothingAtAll()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);
        harness.Connectivity.GoOffline();

        SyncResult result = await harness.Sync.SyncAsync(SyncReason.Periodic);

        Assert.Equal(SyncOutcome.Offline, result.Outcome);
        Assert.Empty(harness.Api.StopUpdates);
        Assert.Equal(0, harness.Repository.RouteWindowRefreshCount);
    }

    [Fact]
    public async Task Sync_WithNobodySignedIn_DoesNothing()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        harness.Authentication.IsAuthenticated = false;

        SyncResult result = await harness.Sync.SyncAsync(SyncReason.AppResumed);

        Assert.Equal(SyncOutcome.NotSignedIn, result.Outcome);
        Assert.Equal(0, harness.Repository.RouteWindowRefreshCount);
    }

    [Fact]
    public async Task Sync_RunOnlyOnceAtATime()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);

        var gate = new TaskCompletionSource();
        harness.Api.GetStopHandler = async (_, _) =>
        {
            await gate.Task;
            return harness.ServerStop;
        };

        Task<SyncResult> first = harness.Sync.SyncAsync(SyncReason.Manual);
        SyncResult second = await harness.Sync.SyncAsync(SyncReason.ConnectivityRestored);

        gate.SetResult();
        await first;

        // Two syncs at once would send the same operation twice. The second caller is told
        // so rather than queued behind the first.
        Assert.Equal(SyncOutcome.AlreadyRunning, second.Outcome);
        Assert.Single(harness.Api.StopUpdates);
    }

    [Fact]
    public async Task Sync_RefreshesTheCacheAfterTheQueueHasDrained()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.Sync.SyncAsync(SyncReason.SignedIn);

        Assert.Equal(1, harness.Repository.RouteWindowRefreshCount);
    }

    [Fact]
    public async Task SyncStatus_ReportsWhatIsActuallyInTheQueue()
    {
        await using SyncHarness harness = await SyncHarness.CreateAsync();

        await harness.QueueStopStatusAsync(StopStatus.Completed);
        await harness.Sync.RefreshStatusAsync();

        Assert.Equal(1, harness.Sync.Current.PendingCount);
        Assert.True(harness.Sync.Current.HasUnsentWork);
        Assert.False(harness.Sync.Current.NeedsAttention);

        harness.ServerStop = harness.Stop with
        {
            Status = StopStatus.Skipped,
            UpdatedAtUtc = harness.Stop.UpdatedAtUtc.AddHours(1),
        };

        await harness.Sync.SyncAsync(SyncReason.Manual);

        Assert.Equal(0, harness.Sync.Current.PendingCount);
        Assert.Equal(1, harness.Sync.Current.ConflictCount);
        Assert.True(harness.Sync.Current.NeedsAttention);
    }

    /// <summary>Everything one of these tests needs, wired the way the app wires it.</summary>
    private sealed class SyncHarness : IAsyncDisposable
    {
        private LocalStoreHarness _store = null!;
        private FleetRepository _queueing = null!;

        public FakeFleetGoApiClient Api { get; private set; } = null!;

        public FakeConnectivityService Connectivity { get; private set; } = null!;

        public FakeAuthenticationService Authentication { get; private set; } = null!;

        public FakeFleetRepository Repository { get; private set; } = null!;

        public SyncService Sync { get; private set; } = null!;

        public FakeTimeProvider Clock { get; private set; } = null!;

        public ILocalFleetStore Store => _store.Store;

        /// <summary>The stop as it was when the change was queued.</summary>
        public StopResponse Stop { get; private set; } = null!;

        /// <summary>What the server returns now. Change it to simulate somebody else editing the record.</summary>
        public StopResponse ServerStop { get; set; } = null!;

        public static async Task<SyncHarness> CreateAsync(RetryPolicyOptions? retryOptions = null)
        {
            LocalStoreHarness store = await LocalStoreHarness.CreateAsync();

            RouteResponse route = FleetTestData.Route("R-001", routeDate: Today, stopCount: 1);
            StopResponse stop = FleetTestData.Stop(sequence: 1, routeId: route.Id, deliveryNotes: "Gate code 1234");

            var harness = new SyncHarness
            {
                _store = store,
                Stop = stop,
                ServerStop = stop,
                Connectivity = new FakeConnectivityService(NetworkStatus.Online),
                Authentication = new FakeAuthenticationService(),
                Repository = new FakeFleetRepository(),
                Clock = new FakeTimeProvider(Now),
            };

            harness.Authentication.SignIn();

            harness.Api = new FakeFleetGoApiClient
            {
                GetRouteHandler = (_, _) => Task.FromResult(route),
                GetStopsHandler = (_, _, _, _, _, _, _) => Task.FromResult(FleetTestData.Page(stop)),
                GetStopHandler = (_, _) => Task.FromResult(harness.ServerStop),
            };

            harness._queueing = new FleetRepository(
                store.Store,
                harness.Api,
                harness.Connectivity,
                harness.Authentication,
                harness.Clock,
                NullLogger<FleetRepository>.Instance);

            await harness._queueing.RefreshRouteDetailAsync(route.Id);

            harness.Sync = new SyncService(
                store.Store,
                harness.Repository,
                harness.Api,
                harness.Connectivity,
                harness.Authentication,
                new ExponentialBackoffRetryPolicy(retryOptions, jitterSource: () => 0.5),
                harness.Clock,
                NullLogger<SyncService>.Instance);

            return harness;
        }

        public Task QueueStopStatusAsync(StopStatus status) => _queueing.QueueStopStatusAsync(Stop.Id, status);

        public ValueTask DisposeAsync() => _store.DisposeAsync();
    }
}
