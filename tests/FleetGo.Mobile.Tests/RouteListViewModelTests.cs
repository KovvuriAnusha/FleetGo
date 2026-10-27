using System.Net;
using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Mobile.Tests.Offline;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public class RouteListViewModelTests
{
    [Fact]
    public async Task Load_PopulatesRoutesAndTotal()
    {
        var repository = PagedRepository(
            total: 2,
            FleetTestData.Route("R-001"),
            FleetTestData.Route("R-002"));

        RouteListViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Routes.Count);
        Assert.Equal(2, viewModel.TotalCount);
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.CanLoadMore);
    }

    [Fact]
    public async Task Load_WithNoRoutes_ReportsTheEmptyState()
    {
        RouteListViewModel viewModel = Build(new FakeFleetRepository());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEmpty);
        Assert.True(viewModel.HasLoaded);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task Load_WhenTheSessionHasExpired_RaisesSessionExpired()
    {
        var repository = new FakeFleetRepository
        {
            RoutesHandler = (_, _, _, _) => Task.FromException<CachedData<RoutePage>>(
                new FleetGoApiException("expired", HttpStatusCode.Unauthorized)),
        };

        RouteListViewModel viewModel = Build(repository);

        bool raised = false;
        viewModel.SessionExpired += (_, _) => raised = true;

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Equal("Your session has expired. Please sign in again.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Load_PassesTheSelectedStatusFilterToTheRepository()
    {
        var repository = new FakeFleetRepository();
        var filters = new List<RouteStatus?>();

        repository.RoutesHandler = (status, _, _, _) =>
        {
            filters.Add(status);
            return Task.FromResult(new CachedData<RoutePage>(new RoutePage([], 0), null, false));
        };

        RouteListViewModel viewModel = Build(repository);
        viewModel.SelectedStatusFilter = viewModel.StatusFilterOptions.Single(option => option.Value == RouteStatus.Completed);

        await viewModel.LoadCommand.ExecuteAsync(null);

        AssertThat.Sequence([RouteStatus.Completed], filters);
    }

    [Fact]
    public async Task LoadMore_AppendsTheNextPageFromWhereTheListGotTo()
    {
        var repository = new FakeFleetRepository();
        var skips = new List<int>();

        repository.RoutesHandler = (_, skip, take, _) =>
        {
            skips.Add(skip);

            RouteResponse[] items = skip == 0
                ? [FleetTestData.Route("R-001"), FleetTestData.Route("R-002")]
                : [FleetTestData.Route("R-003")];

            return Task.FromResult(new CachedData<RoutePage>(new RoutePage(items, 3), null, false));
        };

        RouteListViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanLoadMore);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        AssertThat.Sequence([0, 2], skips);
        Assert.Equal(3, viewModel.Routes.Count);
        Assert.False(viewModel.CanLoadMore);
    }

    [Fact]
    public async Task LoadMore_DoesNothing_WhenEverythingIsAlreadyLoaded()
    {
        var repository = PagedRepository(total: 1, FleetTestData.Route("R-001"));
        RouteListViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        int readsAfterFirstLoad = 0;
        repository.RoutesHandler = (_, _, _, _) =>
        {
            readsAfterFirstLoad++;
            return Task.FromResult(new CachedData<RoutePage>(new RoutePage([], 1), null, false));
        };

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(0, readsAfterFirstLoad);
        Assert.Single(viewModel.Routes);
    }

    [Fact]
    public async Task Refresh_LeavesTheLoadedRoutesAlone_WhenTheReadFails()
    {
        var repository = PagedRepository(total: 1, FleetTestData.Route("R-001"));
        RouteListViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.Single(viewModel.Routes);

        repository.RoutesHandler = (_, _, _, _) => Task.FromException<CachedData<RoutePage>>(
            new FleetGoApiException("boom", HttpStatusCode.InternalServerError));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Blanking a list the driver can still read, because a refresh failed, is worse than
        // showing it with a message over the top.
        Assert.Single(viewModel.Routes);
        Assert.True(viewModel.HasErrorMessage);
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task Refresh_AsksForASync()
    {
        var trigger = new FakeSyncTrigger();
        RouteListViewModel viewModel = Build(new FakeFleetRepository(), trigger);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        AssertThat.Sequence([SyncReason.Manual], trigger.AwaitedReasons);
    }

    [Fact]
    public async Task LoadMore_DiscardsItsResponse_WhenTheFilterChangedWhileItWasInFlight()
    {
        // The Phase 4B defect this guards against: a load-more that was started under one
        // filter must not append its rows to the list a later refresh has since replaced.
        var repository = new FakeFleetRepository();
        var gate = new TaskCompletionSource();
        bool firstLoadDone = false;

        repository.RoutesHandler = async (status, skip, _, _) =>
        {
            if (!firstLoadDone)
            {
                firstLoadDone = true;
                return new CachedData<RoutePage>(
                    new RoutePage([FleetTestData.Route("R-001"), FleetTestData.Route("R-002")], 4),
                    null,
                    false);
            }

            if (skip > 0)
            {
                // The stale page: held open until the test has started a newer first-page load.
                await gate.Task;
                return new CachedData<RoutePage>(new RoutePage([FleetTestData.Route("STALE")], 4), null, false);
            }

            return new CachedData<RoutePage>(
                new RoutePage([FleetTestData.Route("R-100", status: status ?? RouteStatus.Planned)], 1),
                null,
                false);
        };

        RouteListViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Task loadMore = viewModel.LoadMoreCommand.ExecuteAsync(null);

        viewModel.SelectedStatusFilter = viewModel.StatusFilterOptions.Single(option => option.Value == RouteStatus.Completed);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        gate.SetResult();
        await loadMore;

        Assert.Single(viewModel.Routes);
        Assert.Equal("R-100", viewModel.Routes[0].RouteNumber);
        Assert.Equal(1, viewModel.TotalCount);
        Assert.DoesNotContain(viewModel.Routes, route => route.RouteNumber == "STALE");
    }

    [Fact]
    public async Task SelectRoute_RaisesRouteSelectedWithTheRouteId()
    {
        RouteResponse route = FleetTestData.Route();
        RouteListViewModel viewModel = Build(PagedRepository(total: 1, route));

        Guid? selected = null;
        viewModel.RouteSelected += (_, id) => selected = id;

        viewModel.SelectRouteCommand.Execute(route);

        Assert.Equal(route.Id, selected);
    }

    private static FakeFleetRepository PagedRepository(int total, params RouteResponse[] routes) =>
        new()
        {
            RoutesHandler = (_, skip, take, _) => Task.FromResult(
                new CachedData<RoutePage>(new RoutePage([.. routes.Skip(skip).Take(take)], total), DateTime.UnixEpoch, false)),
        };

    private static RouteListViewModel Build(FakeFleetRepository repository, FakeSyncTrigger? trigger = null) =>
        new(repository, trigger ?? new FakeSyncTrigger(), OfflineTestFactory.SyncStatus());
}
