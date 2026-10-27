using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Connectivity;
using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Shared.Contracts.Fleet;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// The local-first read path and the outbox, against the real SQLite store and a stubbed API.
/// </summary>
public class FleetRepositoryTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reads_ComeBackEmptyWhenNobodyIsSignedIn()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        var api = FakeFleetGoApiClient.WithRoutes(FleetTestData.Route("R-001", routeDate: Today));

        var authentication = new FakeAuthenticationService();
        FleetRepository repository = Build(harness, api, authentication: authentication);

        CachedData<IReadOnlyList<RouteResponse>> result = await repository.GetRoutesForDateAsync(Today);

        // No session, no fleet data, and nothing fetched in the attempt - a signed-out device
        // has no business holding or requesting a driver's day.
        Assert.Empty(result.Value);
        Assert.Empty(api.RouteQueries);
    }

    [Fact]
    public async Task FirstRead_WithAnEmptyCacheAndANetwork_FillsTheDeviceThenReadsIt()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        var api = FakeFleetGoApiClient.WithRoutes(
            FleetTestData.Route("R-001", routeDate: Today),
            FleetTestData.Route("R-002", routeDate: Today));

        FleetRepository repository = Build(harness, api);

        CachedData<IReadOnlyList<RouteResponse>> result = await repository.GetRoutesForDateAsync(Today);

        Assert.Equal(2, result.Value.Count);
        Assert.Equal(Now.UtcDateTime, result.SyncedAtUtc);

        // And it really is on the device now, not just in the answer.
        Assert.Equal(2, await harness.Store.CountRoutesAsync(null));
    }

    [Fact]
    public async Task Reads_WhenOffline_ComeOutOfTheCacheWithoutTouchingTheApi()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        var api = FakeFleetGoApiClient.WithRoutes(FleetTestData.Route("R-001", routeDate: Today));
        var connectivity = new FakeConnectivityService();

        FleetRepository repository = Build(harness, api, connectivity);

        await repository.GetRoutesForDateAsync(Today);
        int callsWhileOnline = api.RouteQueries.Count;

        connectivity.GoOffline();

        CachedData<IReadOnlyList<RouteResponse>> offline = await repository.GetRoutesForDateAsync(Today);

        Assert.Single(offline.Value);
        Assert.Equal(callsWhileOnline, api.RouteQueries.Count);
    }

    [Fact]
    public async Task Reads_WithAnEmptyCacheAndNoNetwork_ReturnNothingRatherThanThrowing()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        var connectivity = new FakeConnectivityService(NetworkStatus.Offline);

        FleetRepository repository = Build(harness, new FakeFleetGoApiClient(), connectivity);

        CachedData<RouteDetail> detail = await repository.GetRouteDetailAsync(Guid.NewGuid());

        Assert.Null(detail.Value.Route);
        Assert.Null(detail.SyncedAtUtc);
    }

    [Fact]
    public async Task Reads_NeverServeAnotherDriversCache()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        var api = FakeFleetGoApiClient.WithRoutes(FleetTestData.Route("R-001", routeDate: Today));

        var driverA = new FakeAuthenticationService();
        driverA.SignIn(Guid.NewGuid());

        FleetRepository asDriverA = Build(harness, api, authentication: driverA);
        await asDriverA.GetRoutesForDateAsync(Today);
        Assert.Equal(1, await harness.Store.CountRoutesAsync(null));

        // Driver B signs in on the same handset. Nothing of A's may survive that.
        var driverB = new FakeAuthenticationService();
        driverB.SignIn(Guid.NewGuid());

        var quietApi = new FakeFleetGoApiClient();
        FleetRepository asDriverB = Build(harness, quietApi, authentication: driverB);

        CachedData<RoutePage> routes = await asDriverB.GetRoutesAsync(null, 0, 20);

        Assert.Empty(routes.Value.Items);
        Assert.Equal(0, routes.Value.TotalCount);
    }

    [Fact]
    public async Task QueueStopStatus_MovesTheStopOnTheDeviceAndQueuesItForTheServer()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        (FleetRepository repository, StopResponse stop) = await WithCachedStopAsync(harness);

        QueuedChangeResult result = await repository.QueueStopStatusAsync(stop.Id, StopStatus.Completed);

        Assert.True(result.Queued);

        LocalStop? cached = await harness.Store.GetStopAsync(stop.Id);
        Assert.Equal("Completed", cached!.Status);
        Assert.True(cached.HasPendingLocalChange);

        IReadOnlyList<OutboxOperation> queued = await harness.Store.GetUnresolvedOperationsAsync();
        OutboxOperation operation = Assert.Single(queued);

        StatusChangePayload payload = OfflineJson.TryDeserialize(operation.PayloadJson)!;
        Assert.Equal("Completed", payload.NewStatus);

        // The baseline is the server's state, captured before the local change was applied -
        // not the value the driver just chose. Getting that order wrong would make every
        // change look like it matched the server.
        Assert.Equal(nameof(StopStatus.Pending), payload.Baseline.Status);
    }

    [Fact]
    public async Task QueueStopStatus_Twice_LeavesOneOperationWithTheOriginalBaseline()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        (FleetRepository repository, StopResponse stop) = await WithCachedStopAsync(harness);

        await repository.QueueStopStatusAsync(stop.Id, StopStatus.Arrived);
        await repository.QueueStopStatusAsync(stop.Id, StopStatus.Completed);

        IReadOnlyList<OutboxOperation> queued = await harness.Store.GetUnresolvedOperationsAsync();
        OutboxOperation operation = Assert.Single(queued);

        StatusChangePayload payload = OfflineJson.TryDeserialize(operation.PayloadJson)!;

        // One update is owed, not two - and the baseline still describes what the server had
        // before any of this, which is the only value the conflict check can use.
        Assert.Equal("Completed", payload.NewStatus);
        Assert.Equal(nameof(StopStatus.Pending), payload.Baseline.Status);
        Assert.Equal("Completed", (await harness.Store.GetStopAsync(stop.Id))!.Status);
    }

    [Fact]
    public async Task QueueStopStatus_ForSomethingTheDeviceHasNeverSeen_IsRefusedWithAnExplanation()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        FleetRepository repository = Build(harness, new FakeFleetGoApiClient());

        QueuedChangeResult result = await repository.QueueStopStatusAsync(Guid.NewGuid(), StopStatus.Completed);

        Assert.False(result.Queued);
        Assert.Contains("isn't saved on this device", result.Message!);
        Assert.Empty(await harness.Store.GetUnresolvedOperationsAsync());
    }

    [Fact]
    public async Task Queue_RefusesToGrowWithoutBound()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        (FleetRepository repository, StopResponse stop) = await WithCachedStopAsync(harness);

        // Fill the queue with changes to other records, so coalescing cannot absorb them.
        for (int i = 0; i < 500; i++)
        {
            await harness.Store.EnqueueAsync(new OutboxOperation
            {
                Id = Guid.NewGuid(),
                EntityType = nameof(OutboxEntityType.Stop),
                EntityId = Guid.NewGuid(),
                OperationType = nameof(OutboxOperationType.UpdateStatus),
                PayloadJson = "{}",
                CreatedAtUtc = Now.UtcDateTime,
                Status = nameof(OutboxOperationStatus.Pending),
            });
        }

        QueuedChangeResult result = await repository.QueueStopStatusAsync(stop.Id, StopStatus.Completed);

        Assert.False(result.Queued);
        Assert.Contains("too many unsent changes", result.Message!);

        // Refused, not silently dropped - and the device's copy is untouched, so the screen
        // does not show a change that nothing is going to send.
        Assert.Equal("Pending", (await harness.Store.GetStopAsync(stop.Id))!.Status);
    }

    [Fact]
    public async Task Refresh_PutsUnsentChangesBackOnTopOfWhatTheServerSaid()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        (FleetRepository repository, StopResponse stop) = await WithCachedStopAsync(harness);

        await repository.QueueStopStatusAsync(stop.Id, StopStatus.Completed);

        // The server still says Pending, because the change has not been sent yet. Without
        // re-applying, the refresh would silently undo the driver's own work in front of them.
        await repository.RefreshRouteDetailAsync(stop.RouteId);

        LocalStop? cached = await harness.Store.GetStopAsync(stop.Id);

        Assert.Equal("Completed", cached!.Status);
        Assert.True(cached.HasPendingLocalChange);
    }

    [Fact]
    public async Task QueueStopStatus_NeitherAbsorbsNorDisturbsAConflictedOperation()
    {
        // This is the property StopDetailViewModel's "Keep mine" depends on, pinned against
        // the real store: a conflicted operation is invisible to the outbox's de-duplication,
        // so queueing a replacement always creates a new row and can neither absorb nor
        // mutate the one it replaces. That is what makes it safe to queue first and discard
        // the conflict only once the replacement is on disk.
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        (FleetRepository repository, StopResponse stop) = await WithCachedStopAsync(harness);

        await repository.QueueStopStatusAsync(stop.Id, StopStatus.Completed);

        OutboxOperation conflicted = Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
        conflicted.Status = nameof(OutboxOperationStatus.Conflict);
        conflicted.ConflictDetail = "The server now shows this as Failed, not Pending.";
        await harness.Store.UpdateOperationAsync(conflicted);

        QueuedChangeResult replacement = await repository.QueueStopStatusAsync(stop.Id, StopStatus.Failed);

        Assert.True(replacement.Queued);
        Assert.NotEqual(conflicted.Id, replacement.OperationId);

        IReadOnlyList<OutboxOperation> unresolved = await harness.Store.GetUnresolvedOperationsAsync();
        Assert.Equal(2, unresolved.Count);

        // The conflict is untouched - same status, same detail, same intended value.
        OutboxOperation survivor = unresolved.Single(operation => operation.Id == conflicted.Id);
        Assert.Equal(nameof(OutboxOperationStatus.Conflict), survivor.Status);
        Assert.Equal("The server now shows this as Failed, not Pending.", survivor.ConflictDetail);
        Assert.Equal(nameof(StopStatus.Completed), OfflineJson.TryDeserialize(survivor.PayloadJson)!.NewStatus);

        // And the replacement is a separate, genuinely pending row.
        OutboxOperation queued = unresolved.Single(operation => operation.Id == replacement.OperationId);
        Assert.Equal(nameof(OutboxOperationStatus.Pending), queued.Status);
        Assert.Equal(nameof(StopStatus.Failed), OfflineJson.TryDeserialize(queued.PayloadJson)!.NewStatus);
    }

    [Fact]
    public async Task UnsentWork_SurvivesTheDriverProfileArrivingLate()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        RouteResponse route = FleetTestData.Route("R-001", routeDate: Today, stopCount: 1);
        StopResponse stop = FleetTestData.Stop(sequence: 1, routeId: route.Id);

        var api = new FakeFleetGoApiClient
        {
            GetRouteHandler = (_, _) => Task.FromResult(route),
            GetStopsHandler = (_, _, _, _, _, _, _) => Task.FromResult(FleetTestData.Page(stop)),
            GetStopHandler = (_, _) => Task.FromResult(stop),
        };

        // The app resumed with tokens restored from SecureStorage but no network, so
        // GET /auth/me has not returned and the driver id is not known yet.
        var authentication = new FakeAuthenticationService();
        authentication.SignInWithoutProfile();

        FleetRepository repository = Build(harness, api, authentication: authentication);

        await repository.RefreshRouteDetailAsync(route.Id);

        QueuedChangeResult queued = await repository.QueueStopStatusAsync(stop.Id, StopStatus.Arrived);
        Assert.True(queued.Queued);

        // The profile finally loads, and the cache is bound to a driver for the first time.
        authentication.SignIn(Guid.NewGuid());

        CachedData<StopDetail> detail = await repository.GetStopDetailAsync(stop.Id);

        // This is the regression: binding an owner for the first time used to clear the
        // database, so a driver who worked a morning offline lost every unsent delivery the
        // moment their profile came back.
        Assert.NotNull(detail.Value.Stop);
        Assert.Equal(nameof(StopStatus.Arrived), (await harness.Store.GetStopAsync(stop.Id))!.Status);
        Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
    }

    [Fact]
    public async Task Clear_LeavesNothingForTheNextSession()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();
        (FleetRepository repository, StopResponse stop) = await WithCachedStopAsync(harness);

        await repository.QueueStopStatusAsync(stop.Id, StopStatus.Completed);
        await repository.ClearAsync();

        Assert.Null(await harness.Store.GetStopAsync(stop.Id));
        Assert.Empty(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Null((await harness.Store.GetMetadataAsync()).OwnerDriverId);
    }

    /// <summary>A repository whose device already holds one route with one stop on it.</summary>
    private static async Task<(FleetRepository Repository, StopResponse Stop)> WithCachedStopAsync(LocalStoreHarness harness)
    {
        RouteResponse route = FleetTestData.Route("R-001", routeDate: Today, stopCount: 1);
        StopResponse stop = FleetTestData.Stop(sequence: 1, routeId: route.Id);

        var api = new FakeFleetGoApiClient
        {
            GetRouteHandler = (_, _) => Task.FromResult(route),
            GetStopsHandler = (_, _, _, _, _, _, _) => Task.FromResult(FleetTestData.Page(stop)),
            GetStopHandler = (_, _) => Task.FromResult(stop),
        };

        FleetRepository repository = Build(harness, api);
        await repository.RefreshRouteDetailAsync(route.Id);

        return (repository, stop);
    }

    private static FleetRepository Build(
        LocalStoreHarness harness,
        FakeFleetGoApiClient api,
        FakeConnectivityService? connectivity = null,
        FakeAuthenticationService? authentication = null)
    {
        FakeAuthenticationService session = authentication ?? SignedIn();

        return new FleetRepository(
            harness.Store,
            api,
            connectivity ?? new FakeConnectivityService(),
            session,
            new FakeTimeProvider(Now),
            NullLogger<FleetRepository>.Instance);
    }

    private static FakeAuthenticationService SignedIn()
    {
        var authentication = new FakeAuthenticationService();
        authentication.SignIn();
        return authentication;
    }
}
