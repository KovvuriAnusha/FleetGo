using System.Net;
using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Models;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Mobile.Tests.Offline;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public class StopDetailViewModelTests
{
    [Fact]
    public async Task Load_PopulatesTheStopItsCustomerAndItsPackages()
    {
        StopResponse stop = FleetTestData.Stop(sequence: 3, customerName: "Acme Ltd", packageCount: 2);

        var repository = RepositoryReturning(new StopDetail(
            stop,
            [FleetTestData.Package("TRK-1"), FleetTestData.Package("TRK-2")],
            FleetTestData.Customer(id: stop.CustomerId)));

        StopDetailViewModel viewModel = Build(repository);

        await viewModel.LoadAsync(stop.Id);

        Assert.True(viewModel.HasStop);
        Assert.Equal(3, viewModel.Stop!.Sequence);
        Assert.True(viewModel.HasCustomer);
        Assert.Equal(2, viewModel.Packages.Count);
        Assert.False(viewModel.HasNoPackages);
    }

    [Fact]
    public async Task Load_WithNoPackages_ReportsTheEmptyState()
    {
        StopResponse stop = FleetTestData.Stop();
        StopDetailViewModel viewModel = Build(RepositoryReturning(new StopDetail(stop, [], null)));

        await viewModel.LoadAsync(stop.Id);

        Assert.True(viewModel.HasNoPackages);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task Load_StillShowsTheStop_WhenTheCustomerRecordIsNotCached()
    {
        StopResponse stop = FleetTestData.Stop(customerName: "Acme Ltd");
        StopDetailViewModel viewModel = Build(RepositoryReturning(new StopDetail(stop, [], null)));

        await viewModel.LoadAsync(stop.Id);

        Assert.True(viewModel.HasStop);
        Assert.False(viewModel.HasCustomer);
        Assert.Equal("Acme Ltd", viewModel.Stop!.CustomerName);
    }

    [Fact]
    public async Task Load_WhenTheStopIsNotOnTheDevice_SaysSoRatherThanFailing()
    {
        StopDetailViewModel viewModel = Build(new FakeFleetRepository());

        await viewModel.LoadAsync(Guid.NewGuid());

        Assert.False(viewModel.HasStop);
        Assert.Contains("isn't saved on this device", viewModel.ErrorMessage!);
    }

    [Fact]
    public async Task Load_WhenTheSessionHasExpired_RaisesSessionExpired()
    {
        var repository = new FakeFleetRepository
        {
            StopDetailHandler = (_, _) => Task.FromException<CachedData<StopDetail>>(
                new FleetGoApiException("expired", HttpStatusCode.Unauthorized)),
        };

        StopDetailViewModel viewModel = Build(repository);

        bool raised = false;
        viewModel.SessionExpired += (_, _) => raised = true;

        await viewModel.LoadAsync(Guid.NewGuid());

        Assert.True(raised);
    }

    [Fact]
    public async Task SetStatus_QueuesTheChangeAndNudgesTheSyncEngine()
    {
        StopResponse stop = FleetTestData.Stop();
        var repository = RepositoryReturning(new StopDetail(stop, [], null));
        var trigger = new FakeSyncTrigger();

        StopDetailViewModel viewModel = Build(repository, trigger);

        await viewModel.LoadAsync(stop.Id);
        await viewModel.SetStatusCommand.ExecuteAsync(
            viewModel.StatusActions.Single(action => action.Value == StopStatus.Completed));

        AssertThat.Sequence([(stop.Id, StopStatus.Completed)], repository.QueuedStopStatuses);

        // Asked for, not waited on: the change is already durable by this point.
        AssertThat.Sequence([SyncReason.ChangeQueued], trigger.RequestedReasons);
        Assert.Empty(trigger.AwaitedReasons);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task SetStatus_WhenTheQueueRefusesIt_TellsTheDriverWhy()
    {
        StopResponse stop = FleetTestData.Stop();
        var repository = RepositoryReturning(new StopDetail(stop, [], null));
        repository.QueueStopStatusHandler = (_, _, _) =>
            Task.FromResult(QueuedChangeResult.Refused("There are too many unsent changes on this device."));

        var trigger = new FakeSyncTrigger();
        StopDetailViewModel viewModel = Build(repository, trigger);

        await viewModel.LoadAsync(stop.Id);
        await viewModel.SetStatusCommand.ExecuteAsync(
            viewModel.StatusActions.Single(action => action.Value == StopStatus.Arrived));

        Assert.Equal("There are too many unsent changes on this device.", viewModel.ErrorMessage);
        Assert.Empty(trigger.RequestedReasons);
    }

    [Fact]
    public async Task Load_ShowsThatAChangeIsWaitingToSend()
    {
        StopResponse stop = FleetTestData.Stop();
        var repository = RepositoryReturning(new StopDetail(stop, [], null), hasPendingLocalChange: true);

        repository.UnresolvedChanges.Add(PendingOperation(stop.Id, StopStatus.Completed));

        StopDetailViewModel viewModel = Build(repository);

        await viewModel.LoadAsync(stop.Id);

        Assert.True(viewModel.HasPendingChangeNote);
        Assert.Equal("Waiting to send: Completed", viewModel.PendingChangeNote);
        Assert.False(viewModel.HasConflict);
        Assert.True(viewModel.HasPendingLocalChange);
    }

    [Fact]
    public async Task Load_SurfacesAConflictRatherThanSwallowingIt()
    {
        StopResponse stop = FleetTestData.Stop();
        var repository = RepositoryReturning(new StopDetail(stop, [], null), hasPendingLocalChange: true);

        OutboxOperation conflicted = PendingOperation(stop.Id, StopStatus.Completed);
        conflicted.Status = nameof(OutboxOperationStatus.Conflict);
        conflicted.ConflictDetail = "The server now shows this as Failed, not Pending.";
        repository.UnresolvedChanges.Add(conflicted);

        StopDetailViewModel viewModel = Build(repository);

        await viewModel.LoadAsync(stop.Id);

        Assert.True(viewModel.HasConflict);
        Assert.Equal("The server now shows this as Failed, not Pending.", viewModel.ConflictNote);
        // A conflict is not also "waiting to send" - it is not going anywhere on its own.
        Assert.False(viewModel.HasPendingChangeNote);
    }

    [Fact]
    public async Task DiscardChange_DropsTheQueuedOperation()
    {
        StopResponse stop = FleetTestData.Stop();
        var repository = RepositoryReturning(new StopDetail(stop, [], null));

        OutboxOperation conflicted = PendingOperation(stop.Id, StopStatus.Completed);
        conflicted.Status = nameof(OutboxOperationStatus.Conflict);
        repository.UnresolvedChanges.Add(conflicted);

        StopDetailViewModel viewModel = Build(repository);

        await viewModel.LoadAsync(stop.Id);
        await viewModel.DiscardChangeCommand.ExecuteAsync(null);

        AssertThat.Sequence([conflicted.Id], repository.DiscardedOperationIds);
        Assert.False(viewModel.HasConflict);
    }

    [Fact]
    public async Task KeepChange_RequeuesTheSameStatusAgainstFreshServerState()
    {
        StopResponse stop = FleetTestData.Stop();
        var repository = RepositoryReturning(new StopDetail(stop, [], null));

        OutboxOperation conflicted = PendingOperation(stop.Id, StopStatus.Failed);
        conflicted.Status = nameof(OutboxOperationStatus.Conflict);
        repository.UnresolvedChanges.Add(conflicted);

        var trigger = new FakeSyncTrigger();
        StopDetailViewModel viewModel = Build(repository, trigger);

        // Observed from inside the queue call: if the conflict has already been deleted by
        // the time the replacement is attempted, a refusal would leave nothing behind. The
        // fix is the ordering, so the ordering is what this asserts.
        bool conflictStillQueuedWhenReplacementWasAttempted = false;
        repository.QueueStopStatusHandler = (_, _, _) =>
        {
            conflictStillQueuedWhenReplacementWasAttempted =
                repository.UnresolvedChanges.Any(operation => operation.Id == conflicted.Id);
            return Task.FromResult(new QueuedChangeResult(true, Guid.NewGuid()));
        };

        await viewModel.LoadAsync(stop.Id);
        await viewModel.KeepChangeCommand.ExecuteAsync(null);

        // The driver's intent is queued afresh - so the comparison the sync engine makes is
        // against what the server holds now - and only then does the old operation go.
        Assert.True(conflictStillQueuedWhenReplacementWasAttempted);
        AssertThat.Sequence([(stop.Id, StopStatus.Failed)], repository.QueuedStopStatuses);
        AssertThat.Sequence([conflicted.Id], repository.DiscardedOperationIds);
        AssertThat.Sequence([SyncReason.ChangeQueued], trigger.RequestedReasons);
    }

    [Fact]
    public async Task KeepChange_WhenTheSessionHasGone_KeepsTheConflictAndTheDriversIntent()
    {
        // A conflict and an expired session arrive from the same sync run, so this pairing is
        // not a contrived one - it is the most likely way "Keep mine" gets refused.
        await AssertKeepChangeRefusalLosesNothingAsync(
            QueuedChangeResult.Refused("Sign in before changing a stop."));
    }

    [Fact]
    public async Task KeepChange_WhenTheQueueIsAtItsLimit_KeepsTheConflictAndTheDriversIntent()
    {
        // The repository's own ceiling is proved against the real store and a real 500-row
        // queue in FleetRepositoryTests.Queue_RefusesToGrowWithoutBound. What matters here is
        // the other half: that this screen does not destroy the driver's work when it is told
        // no.
        await AssertKeepChangeRefusalLosesNothingAsync(
            QueuedChangeResult.Refused("There are too many unsent changes on this device."));
    }

    /// <summary>
    /// Drives "Keep mine" into a refusal and asserts that nothing was lost: the conflicted
    /// operation is still queued, it still carries the status the driver chose, the screen
    /// still offers both ways out, and the driver is told why.
    /// </summary>
    private static async Task AssertKeepChangeRefusalLosesNothingAsync(QueuedChangeResult refusal)
    {
        StopResponse stop = FleetTestData.Stop();
        var repository = RepositoryReturning(new StopDetail(stop, [], null), hasPendingLocalChange: true);

        OutboxOperation conflicted = PendingOperation(stop.Id, StopStatus.Completed);
        conflicted.Status = nameof(OutboxOperationStatus.Conflict);
        conflicted.ConflictDetail = "The server now shows this as Failed, not Pending.";
        repository.UnresolvedChanges.Add(conflicted);

        var trigger = new FakeSyncTrigger();
        StopDetailViewModel viewModel = Build(repository, trigger);

        await viewModel.LoadAsync(stop.Id);

        repository.QueueStopStatusHandler = (_, _, _) => Task.FromResult(refusal);

        await viewModel.KeepChangeCommand.ExecuteAsync(null);

        // Nothing was deleted...
        Assert.Empty(repository.DiscardedOperationIds);

        // ...the operation is still there, still carrying the driver's chosen status, so the
        // change can still be sent once whatever blocked it clears...
        OutboxOperation survivor = Assert.Single(repository.UnresolvedChanges);
        Assert.Equal(conflicted.Id, survivor.Id);
        Assert.Equal(
            nameof(StopStatus.Completed),
            OfflineJson.TryDeserialize(survivor.PayloadJson)!.NewStatus);

        // ...the screen still shows the conflict and both ways out of it...
        Assert.True(viewModel.HasConflict);
        Assert.Equal("The server now shows this as Failed, not Pending.", viewModel.ConflictNote);

        // ...the driver is told what went wrong, and no sync was nudged for work that was
        // never queued.
        Assert.Equal(refusal.Message, viewModel.ErrorMessage);
        Assert.Empty(trigger.RequestedReasons);
    }

    private static OutboxOperation PendingOperation(Guid stopId, StopStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            EntityType = nameof(OutboxEntityType.Stop),
            EntityId = stopId,
            OperationType = nameof(OutboxOperationType.UpdateStatus),
            PayloadJson = OfflineJson.Serialize(new StatusChangePayload
            {
                NewStatus = status.ToString(),
                Baseline = new EntitySnapshot { Status = nameof(StopStatus.Pending) },
            }),
            Status = nameof(OutboxOperationStatus.Pending),
        };

    private static FakeFleetRepository RepositoryReturning(StopDetail detail, bool hasPendingLocalChange = false) =>
        new()
        {
            StopDetailHandler = (_, _) => Task.FromResult(
                new CachedData<StopDetail>(detail, DateTime.UnixEpoch, hasPendingLocalChange)),
        };

    private static StopDetailViewModel Build(FakeFleetRepository repository, FakeSyncTrigger? trigger = null) =>
        new(repository, trigger ?? new FakeSyncTrigger(), OfflineTestFactory.SyncStatus());
}
