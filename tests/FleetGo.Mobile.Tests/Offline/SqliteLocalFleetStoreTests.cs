using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Models;

namespace FleetGo.Mobile.Tests.Offline;

/// <summary>
/// The local database, exercised against real SQLite. See <see cref="LocalStoreHarness"/> for
/// why these run against the real engine rather than a stand-in.
/// </summary>
public class SqliteLocalFleetStoreTests
{
    private static readonly DateTime SyncedAt = new(2026, 9, 18, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Initialize_IsSafeToRunTwice()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        // Every launch calls this. If it were not idempotent, the second start of the app
        // would be the one that failed - the worst possible time to find out.
        await harness.Store.InitializeAsync();
        await harness.Store.InitializeAsync();

        SyncMetadata metadata = await harness.Store.GetMetadataAsync();
        Assert.Null(metadata.OwnerDriverId);
    }

    [Fact]
    public async Task ReplaceRoutes_RemovesRoutesTheServerNoLongerReturns()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        LocalRoute first = Route("R-001");
        LocalRoute second = Route("R-002");

        await harness.Store.ReplaceRoutesAsync([first, second]);
        await harness.Store.ReplaceRoutesAsync([first]);

        IReadOnlyList<LocalRoute> routes = await harness.Store.GetRoutesAsync(null, 0, 50);

        Assert.Single(routes);
        Assert.Equal("R-001", routes[0].RouteNumber);
        Assert.Null(await harness.Store.GetRouteAsync(second.Id));
    }

    [Fact]
    public async Task GetRoutes_FiltersByStatusAndPages()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        await harness.Store.ReplaceRoutesAsync(
        [
            Route("R-001", status: "Planned"),
            Route("R-002", status: "Planned"),
            Route("R-003", status: "Completed"),
        ]);

        Assert.Equal(2, await harness.Store.CountRoutesAsync("Planned"));
        Assert.Equal(3, await harness.Store.CountRoutesAsync(null));

        IReadOnlyList<LocalRoute> page = await harness.Store.GetRoutesAsync("Planned", skip: 1, take: 10);
        Assert.Single(page);
    }

    [Fact]
    public async Task GetStopsForRoute_ReturnsThemInRunningOrder()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        Guid routeId = Guid.NewGuid();

        await harness.Store.ReplaceStopsForRouteAsync(routeId,
        [
            Stop(routeId, sequence: 3),
            Stop(routeId, sequence: 1),
            Stop(routeId, sequence: 2),
        ]);

        IReadOnlyList<LocalStop> stops = await harness.Store.GetStopsForRouteAsync(routeId);

        AssertThat.Sequence([1, 2, 3], stops.Select(stop => stop.Sequence));
    }

    [Fact]
    public async Task EnsureOwnedBy_WipesEverythingWhenADifferentDriverSignsIn()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        Guid driverA = Guid.NewGuid();
        Guid driverB = Guid.NewGuid();

        await harness.Store.EnsureOwnedByAsync(driverA);
        await harness.Store.ReplaceRoutesAsync([Route("R-001")]);
        await harness.Store.EnqueueAsync(Operation(Guid.NewGuid()));

        bool wiped = await harness.Store.EnsureOwnedByAsync(driverB);

        // The security property Phase 5 asks for: driver B must not inherit driver A's
        // routes, and must not inherit their queue either - sending A's work under B's token
        // would be worse than losing it.
        Assert.True(wiped);
        Assert.Empty(await harness.Store.GetRoutesAsync(null, 0, 50));
        Assert.Empty(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Equal(driverB, (await harness.Store.GetMetadataAsync()).OwnerDriverId);
    }

    [Fact]
    public async Task EnsureOwnedBy_AdoptsAnUnownedCacheRatherThanWipingIt()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        // Records and queued work written before any driver was bound. That is not a strange
        // state: FleetRepository's refresh methods fill the cache without binding an owner,
        // because they can run before GET /auth/me has returned.
        await harness.Store.ReplaceRoutesAsync([Route("R-001")]);
        await harness.Store.EnqueueAsync(Operation(Guid.NewGuid()));

        bool wiped = await harness.Store.EnsureOwnedByAsync(Guid.NewGuid());

        // "No owner recorded" is not "somebody else's data". Clearing here would destroy work
        // that provably belongs to nobody else - including unsent deliveries.
        Assert.False(wiped);
        Assert.Single(await harness.Store.GetRoutesAsync(null, 0, 50));
        Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
    }

    [Fact]
    public async Task EnsureOwnedBy_AdoptingACacheKeepsWhenItWasLastSynced()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        SyncMetadata metadata = await harness.Store.GetMetadataAsync();
        metadata.LastSuccessfulSyncUtc = SyncedAt;
        await harness.Store.SaveMetadataAsync(metadata);

        await harness.Store.EnsureOwnedByAsync(Guid.NewGuid());

        // Adopting a cache does not make it less current than it was a moment earlier, and
        // the banner reads this value to tell the driver how fresh their data is.
        Assert.Equal(SyncedAt, (await harness.Store.GetMetadataAsync()).LastSuccessfulSyncUtc);
    }

    [Fact]
    public async Task EnsureOwnedBy_KeepsEverythingForTheSameDriver()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        Guid driver = Guid.NewGuid();

        await harness.Store.EnsureOwnedByAsync(driver);
        await harness.Store.ReplaceRoutesAsync([Route("R-001")]);

        bool wiped = await harness.Store.EnsureOwnedByAsync(driver);

        Assert.False(wiped);
        Assert.Single(await harness.Store.GetRoutesAsync(null, 0, 50));
    }

    [Fact]
    public async Task ClearAll_LeavesNothingBehindButAnEmptyMetadataRow()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        await harness.Store.EnsureOwnedByAsync(Guid.NewGuid());
        await harness.Store.ReplaceRoutesAsync([Route("R-001")]);
        await harness.Store.UpsertCustomerAsync(new LocalCustomer { Id = Guid.NewGuid(), Name = "Acme" });
        await harness.Store.EnqueueAsync(Operation(Guid.NewGuid()));

        await harness.Store.ClearAllAsync();

        Assert.Empty(await harness.Store.GetRoutesAsync(null, 0, 50));
        Assert.Empty(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.Null((await harness.Store.GetMetadataAsync()).OwnerDriverId);
    }

    [Fact]
    public async Task QueuedWork_SurvivesTheProcess()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        Guid entityId = Guid.NewGuid();
        await harness.Store.EnqueueAsync(Operation(entityId));

        // The whole promise of an outbox: a change made in a lift is still there after the
        // app is killed and reopened.
        SqliteLocalFleetStore reopened = await harness.ReopenAsync();

        try
        {
            IReadOnlyList<OutboxOperation> operations = await reopened.GetUnresolvedOperationsAsync();

            Assert.Single(operations);
            Assert.Equal(entityId, operations[0].EntityId);
        }
        finally
        {
            await reopened.DisposeAsync();
        }
    }

    [Fact]
    public async Task SendableOperations_ComeBackInTheOrderTheDriverMadeThem()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        OutboxOperation first = Operation(Guid.NewGuid());
        OutboxOperation second = Operation(Guid.NewGuid());
        OutboxOperation third = Operation(Guid.NewGuid());

        await harness.Store.EnqueueAsync(first);
        await harness.Store.EnqueueAsync(second);
        await harness.Store.EnqueueAsync(third);

        IReadOnlyList<OutboxOperation> sendable = await harness.Store.GetSendableOperationsAsync(SyncedAt);

        AssertThat.Sequence([first.Id, second.Id, third.Id], sendable.Select(operation => operation.Id));
    }

    [Fact]
    public async Task SendableOperations_SkipAnythingStillInBackoff()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        OutboxOperation ready = Operation(Guid.NewGuid());
        OutboxOperation waiting = Operation(Guid.NewGuid());
        waiting.NextAttemptAtUtc = SyncedAt.AddMinutes(5);

        await harness.Store.EnqueueAsync(ready);
        await harness.Store.EnqueueAsync(waiting);

        IReadOnlyList<OutboxOperation> now = await harness.Store.GetSendableOperationsAsync(SyncedAt);
        AssertThat.Sequence([ready.Id], now.Select(operation => operation.Id));

        IReadOnlyList<OutboxOperation> later = await harness.Store.GetSendableOperationsAsync(SyncedAt.AddMinutes(6));
        Assert.Equal(2, later.Count);
    }

    [Fact]
    public async Task SendableOperations_ExcludeConflictsAndFailures()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        OutboxOperation conflicted = Operation(Guid.NewGuid());
        conflicted.Status = nameof(OutboxOperationStatus.Conflict);

        OutboxOperation failed = Operation(Guid.NewGuid());
        failed.Status = nameof(OutboxOperationStatus.Failed);

        await harness.Store.EnqueueAsync(conflicted);
        await harness.Store.EnqueueAsync(failed);

        // Neither is retried automatically - that is the point of both states - but both are
        // still visible to the UI.
        Assert.Empty(await harness.Store.GetSendableOperationsAsync(SyncedAt));
        Assert.Equal(2, (await harness.Store.GetUnresolvedOperationsAsync()).Count);
    }

    [Fact]
    public async Task PendingFlag_IsDerivedFromTheQueueAndSurvivesARefresh()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        LocalRoute route = Route("R-001");
        await harness.Store.ReplaceRoutesAsync([route]);

        await harness.Store.EnqueueAsync(Operation(route.Id, OutboxEntityType.Route));

        Assert.True((await harness.Store.GetRouteAsync(route.Id))!.HasPendingLocalChange);

        // A sync replaces the row wholesale. The flag has to come back, because it is a fact
        // about the queue rather than a value the row happens to carry.
        await harness.Store.ReplaceRoutesAsync([Route("R-001", id: route.Id)]);

        Assert.True((await harness.Store.GetRouteAsync(route.Id))!.HasPendingLocalChange);
    }

    [Fact]
    public async Task FindPendingOperation_FindsOnlyTheOneStillWaiting()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        Guid entityId = Guid.NewGuid();

        OutboxOperation settled = Operation(entityId);
        settled.Status = nameof(OutboxOperationStatus.Completed);
        await harness.Store.EnqueueAsync(settled);

        Assert.Null(await harness.Store.FindPendingOperationAsync(OutboxEntityType.Stop, entityId, OutboxOperationType.UpdateStatus));

        OutboxOperation pending = Operation(entityId);
        await harness.Store.EnqueueAsync(pending);

        OutboxOperation? found = await harness.Store.FindPendingOperationAsync(
            OutboxEntityType.Stop, entityId, OutboxOperationType.UpdateStatus);

        Assert.Equal(pending.Id, found?.Id);
    }

    [Fact]
    public async Task Prune_RemovesSettledWorkButNeverAnythingUnresolved()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        OutboxOperation completed = Operation(Guid.NewGuid());
        completed.Status = nameof(OutboxOperationStatus.Completed);
        completed.LastAttemptAtUtc = SyncedAt.AddDays(-2);

        OutboxOperation conflicted = Operation(Guid.NewGuid());
        conflicted.Status = nameof(OutboxOperationStatus.Conflict);
        conflicted.LastAttemptAtUtc = SyncedAt.AddDays(-2);

        await harness.Store.EnqueueAsync(completed);
        await harness.Store.EnqueueAsync(conflicted);

        int pruned = await harness.Store.PruneCompletedOperationsAsync(SyncedAt.AddDays(-1));

        // This is what keeps the queue from growing without bound - and what must never
        // quietly delete a conflict a person has not dealt with yet.
        Assert.Equal(1, pruned);
        Assert.Single(await harness.Store.GetUnresolvedOperationsAsync());
    }

    [Fact]
    public async Task DeleteOperation_RemovesItAndClearsTheRecordsPendingFlag()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        LocalRoute route = Route("R-001");
        await harness.Store.ReplaceRoutesAsync([route]);

        OutboxOperation operation = Operation(route.Id, OutboxEntityType.Route);
        await harness.Store.EnqueueAsync(operation);

        await harness.Store.DeleteOperationAsync(operation.Id);

        Assert.Empty(await harness.Store.GetUnresolvedOperationsAsync());
        Assert.False((await harness.Store.GetRouteAsync(route.Id))!.HasPendingLocalChange);
    }

    [Fact]
    public async Task ApplyLocalStatus_MovesTheCachedRecordImmediately()
    {
        await using LocalStoreHarness harness = await LocalStoreHarness.CreateAsync();

        Guid routeId = Guid.NewGuid();
        await harness.Store.ReplaceStopsForRouteAsync(routeId, [Stop(routeId, sequence: 1)]);

        LocalStop stop = (await harness.Store.GetStopsForRouteAsync(routeId))[0];

        await harness.Store.ApplyLocalStatusAsync(OutboxEntityType.Stop, stop.Id, "Completed");

        LocalStop? updated = await harness.Store.GetStopAsync(stop.Id);

        Assert.Equal("Completed", updated!.Status);
        Assert.True(updated.HasPendingLocalChange);
    }

    private static LocalRoute Route(string routeNumber, string status = "Planned", Guid? id = null, string routeDate = "2026-09-18") =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            RouteNumber = routeNumber,
            DriverId = Guid.NewGuid(),
            RouteDate = routeDate,
            Status = status,
            SyncedAtUtc = SyncedAt,
        };

    private static LocalStop Stop(Guid routeId, int sequence) =>
        new()
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Acme Ltd",
            Sequence = sequence,
            Status = "Pending",
            SyncedAtUtc = SyncedAt,
        };

    private static OutboxOperation Operation(Guid entityId, OutboxEntityType entityType = OutboxEntityType.Stop) =>
        new()
        {
            Id = Guid.NewGuid(),
            EntityType = entityType.ToString(),
            EntityId = entityId,
            OperationType = nameof(OutboxOperationType.UpdateStatus),
            PayloadJson = "{}",
            CreatedAtUtc = SyncedAt,
            Status = nameof(OutboxOperationStatus.Pending),
        };
}
