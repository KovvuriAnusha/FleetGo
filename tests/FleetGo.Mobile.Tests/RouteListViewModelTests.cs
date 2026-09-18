using System.Net;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public sealed class RouteListViewModelTests
{
    [Fact]
    public async Task Load_PopulatesRoutesAndTotal()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes(
            FleetTestData.Route("R-001", RouteStatus.InProgress, stopCount: 8),
            FleetTestData.Route("R-002", RouteStatus.Planned, stopCount: 3));

        RouteListViewModel viewModel = new(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Routes.Count);
        Assert.Equal("R-001", viewModel.Routes[0].RouteNumber);
        Assert.Equal(2, viewModel.TotalCount);
        Assert.True(viewModel.HasLoaded);
        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.IsEmpty);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Load_WithNoRoutes_ReportsTheEmptyState()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes();
        RouteListViewModel viewModel = new(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Routes);
        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task Load_WhenTheApiIsUnreachable_ShowsAnErrorAndNotTheEmptyState()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.ThatFailsRouteListWith(new HttpRequestException("no network"));
        RouteListViewModel viewModel = new(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Could not reach the API. Is it running?", viewModel.ErrorMessage);
        Assert.True(viewModel.HasErrorMessage);
        // A failure is not an empty result: showing "no routes to show" here would tell the
        // driver something the app does not actually know.
        Assert.False(viewModel.IsEmpty);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task Load_WhenTheApiReturnsAnError_ShowsAGenericFailureMessage()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.ThatFailsRouteListWith(
            new FleetGoApiException("boom", HttpStatusCode.InternalServerError));
        RouteListViewModel viewModel = new(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("The API could not complete that request. Please try again.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Load_WhenTheSessionHasExpired_RaisesSessionExpired()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.ThatFailsRouteListWith(
            new FleetGoApiException("unauthorized", HttpStatusCode.Unauthorized));
        RouteListViewModel viewModel = new(apiClient);

        bool raised = false;
        viewModel.SessionExpired += (_, _) => raised = true;

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Equal("Your session has expired. Please sign in again.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Load_SendsTheSelectedStatusFilterToTheApi()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes();
        RouteListViewModel viewModel = new(apiClient)
        {
            SelectedStatusFilter = new RouteStatusFilterOption("Completed", RouteStatus.Completed),
        };

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(RouteStatus.Completed, apiClient.RouteQueries[0].Status);
        Assert.Equal(1, apiClient.RouteQueries[0].Page);
    }

    [Fact]
    public async Task LoadMore_AppendsTheNextPage()
    {
        RouteResponse first = FleetTestData.Route("R-001");
        RouteResponse second = FleetTestData.Route("R-002");

        FakeFleetGoApiClient apiClient = new()
        {
            GetRoutesHandler = (page, pageSize, _, _, _, _) => Task.FromResult(
                page == 1
                    ? FleetTestData.PageOf(1, pageSize, totalCount: 2, first)
                    : FleetTestData.PageOf(2, pageSize, totalCount: 2, second)),
        };

        RouteListViewModel viewModel = new(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanLoadMore);

        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Routes.Count);
        Assert.Equal("R-002", viewModel.Routes[1].RouteNumber);
        Assert.False(viewModel.CanLoadMore);
        Assert.Equal(2, apiClient.RouteQueries.Count);
        Assert.Equal(2, apiClient.RouteQueries[1].Page);
    }

    [Fact]
    public async Task LoadMore_DoesNothing_WhenEverythingIsAlreadyLoaded()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes(FleetTestData.Route("R-001"));
        RouteListViewModel viewModel = new(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);
        await viewModel.LoadMoreCommand.ExecuteAsync(null);

        // One page loaded, one request made - the list must not keep asking for pages that
        // cannot exist as it scrolls.
        Assert.Single(apiClient.RouteQueries);
        Assert.Single(viewModel.Routes);
    }

    [Fact]
    public async Task Refresh_LeavesTheLoadedRoutesAlone_WhenTheRefreshFails()
    {
        bool fail = false;
        RouteResponse existing = FleetTestData.Route("R-001");

        FakeFleetGoApiClient apiClient = new()
        {
            GetRoutesHandler = (page, pageSize, _, _, _, _) => fail
                ? Task.FromException<PagedResponse<RouteResponse>>(new HttpRequestException("dropped"))
                : Task.FromResult(FleetTestData.PageOf(page, pageSize, totalCount: 1, existing)),
        };

        RouteListViewModel viewModel = new(apiClient);
        await viewModel.LoadCommand.ExecuteAsync(null);

        fail = true;
        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Blanking the list behind an error message would lose what the driver was reading.
        Assert.Single(viewModel.Routes);
        Assert.True(viewModel.HasErrorMessage);
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task LoadMore_DiscardsItsResponse_WhenTheFilterChangedWhileItWasInFlight()
    {
        // Regression: a load-more that is still in flight when the driver changes the filter
        // must not append its rows to the new, differently filtered list - nor move the paging
        // position, which would then page the wrong result set.
        RouteResponse stalePageTwoRoute = FleetTestData.Route("R-ALL-2");
        RouteResponse completedRoute = FleetTestData.Route("R-DONE-1", RouteStatus.Completed);

        TaskCompletionSource<PagedResponse<RouteResponse>> deferredPageTwo =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        FakeFleetGoApiClient apiClient = new()
        {
            GetRoutesHandler = (page, pageSize, status, _, _, _) =>
            {
                if (page == 2)
                {
                    // The "All" filter's second page: held open until the test releases it.
                    return deferredPageTwo.Task;
                }

                return status == RouteStatus.Completed
                    ? Task.FromResult(FleetTestData.PageOf(1, pageSize, totalCount: 1, completedRoute))
                    : Task.FromResult(FleetTestData.PageOf(1, pageSize, totalCount: 40, FleetTestData.Route("R-ALL-1")));
            },
        };

        RouteListViewModel viewModel = new(apiClient);

        // 1. Page 1 of the unfiltered list, with more pages available.
        await viewModel.LoadCommand.ExecuteAsync(null);
        Assert.True(viewModel.CanLoadMore);

        // 2. Start load-more; it blocks on the deferred response.
        Task loadMore = viewModel.LoadMoreCommand.ExecuteAsync(null);

        // 3. The driver switches the filter, which refreshes to page 1 of "Completed".
        viewModel.SelectedStatusFilter = new RouteStatusFilterOption("Completed", RouteStatus.Completed);
        await viewModel.RefreshCommand.ExecuteAsync(null);

        // 4. Only now does the old query answer.
        deferredPageTwo.SetResult(FleetTestData.PageOf(2, 20, totalCount: 40, stalePageTwoRoute));
        await loadMore;

        // 5. The stale rows were dropped.
        Assert.Equal("R-DONE-1", Assert.Single(viewModel.Routes).RouteNumber);
        Assert.DoesNotContain(viewModel.Routes, route => route.RouteNumber == stalePageTwoRoute.RouteNumber);

        // 6. Paging state still describes the refreshed query: one route out of one, so there
        //    is nothing more to fetch - had the stale response been applied, TotalCount would
        //    be 40 and the list would keep paging the unfiltered set.
        Assert.Equal(1, viewModel.TotalCount);
        Assert.False(viewModel.CanLoadMore);
        Assert.False(viewModel.IsLoadingMore);
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task SelectRoute_RaisesRouteSelectedWithTheRouteId()
    {
        RouteResponse route = FleetTestData.Route("R-001");
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes(route);
        RouteListViewModel viewModel = new(apiClient);

        Guid? selected = null;
        viewModel.RouteSelected += (_, routeId) => selected = routeId;

        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.SelectRouteCommand.Execute(viewModel.Routes[0]);

        Assert.Equal(route.Id, selected);
    }
}
