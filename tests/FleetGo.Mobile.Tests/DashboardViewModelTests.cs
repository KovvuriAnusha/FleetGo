using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Tests;

public sealed class DashboardViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Load_SummarisesTodaysRoutes()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes(
            FleetTestData.Route("R-001", RouteStatus.Completed, stopCount: 6),
            FleetTestData.Route("R-002", RouteStatus.Planned, stopCount: 4),
            FleetTestData.Route("R-003", RouteStatus.Planned, stopCount: 2));

        DashboardViewModel viewModel = CreateViewModel(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.TodayRoutes.Count);
        Assert.Equal(2, viewModel.PlannedCount);
        Assert.Equal(0, viewModel.InProgressCount);
        Assert.Equal(1, viewModel.CompletedCount);
        Assert.Equal(12, viewModel.StopsToday);
        Assert.False(viewModel.HasNoRoutesToday);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Load_ExcludesCancelledRoutesFromTheStopCount()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes(
            FleetTestData.Route("R-001", RouteStatus.Planned, stopCount: 5),
            FleetTestData.Route("R-002", RouteStatus.Cancelled, stopCount: 7));

        DashboardViewModel viewModel = CreateViewModel(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        // A cancelled route's stops are not work still to do, and counting them made the
        // headline number disagree with the Planned/Active/Done tiles beside it.
        Assert.Equal(5, viewModel.StopsToday);
        Assert.Equal(1, viewModel.PlannedCount);
        Assert.Equal(0, viewModel.InProgressCount);
        Assert.Equal(0, viewModel.CompletedCount);

        // The route itself is still listed - it is part of today, just not outstanding work.
        Assert.Equal(2, viewModel.TodayRoutes.Count);
    }

    [Fact]
    public async Task Load_PicksTheInProgressRouteAsTheActiveOne()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes(
            FleetTestData.Route("R-001", RouteStatus.Planned),
            FleetTestData.Route("R-002", RouteStatus.InProgress),
            FleetTestData.Route("R-003", RouteStatus.Planned));

        DashboardViewModel viewModel = CreateViewModel(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasActiveRoute);
        Assert.Equal("R-002", viewModel.ActiveRoute!.RouteNumber);
    }

    [Fact]
    public async Task Load_FallsBackToTheFirstPlannedRoute_WhenNothingIsInProgress()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes(
            FleetTestData.Route("R-001", RouteStatus.Completed),
            FleetTestData.Route("R-002", RouteStatus.Planned));

        DashboardViewModel viewModel = CreateViewModel(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("R-002", viewModel.ActiveRoute!.RouteNumber);
    }

    [Fact]
    public async Task Load_AsksTheApiForTodaysRoutesOnly()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes();
        FakeTimeProvider timeProvider = new(Now);
        DashboardViewModel viewModel = CreateViewModel(apiClient, timeProvider);

        await viewModel.LoadCommand.ExecuteAsync(null);

        // Derived the same way the view model does, so the assertion holds in any time zone
        // the tests happen to run in.
        DateOnly expectedDate = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
        Assert.Equal(expectedDate, apiClient.RouteQueries[0].RouteDate);
    }

    [Fact]
    public async Task Load_WithNothingScheduled_ReportsTheEmptyState()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.WithRoutes();
        DashboardViewModel viewModel = CreateViewModel(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.TodayRoutes);
        Assert.True(viewModel.HasNoRoutesToday);
        Assert.False(viewModel.HasActiveRoute);
        Assert.Equal(0, viewModel.StopsToday);
    }

    [Fact]
    public async Task Load_WhenTheApiFails_ShowsAnErrorAndNotTheEmptyState()
    {
        FakeFleetGoApiClient apiClient = FakeFleetGoApiClient.ThatFailsRouteListWith(new HttpRequestException("no network"));
        DashboardViewModel viewModel = CreateViewModel(apiClient);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Could not reach the API. Is it running?", viewModel.ErrorMessage);
        Assert.False(viewModel.HasNoRoutesToday);
    }

    [Fact]
    public async Task Load_GreetsTheSignedInDriver()
    {
        FakeAuthenticationService authService = new()
        {
            CurrentUser = new CurrentUserResponse(
                UserId: Guid.NewGuid(),
                Email: "driver@example.com",
                FirstName: "Asha",
                LastName: "Patel",
                DriverId: Guid.NewGuid(),
                DriverCode: "D-1001"),
        };

        DashboardViewModel viewModel = CreateViewModel(FakeFleetGoApiClient.WithRoutes(), authService);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Asha Patel", viewModel.DriverName);
        Assert.Equal("D-1001", viewModel.DriverCode);
        Assert.True(viewModel.HasDriverCode);
    }

    [Fact]
    public async Task Load_WithNoProfileLoaded_FallsBackToAGenericGreeting()
    {
        DashboardViewModel viewModel = CreateViewModel(FakeFleetGoApiClient.WithRoutes());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Driver", viewModel.DriverName);
        Assert.False(viewModel.HasDriverCode);
    }

    [Fact]
    public async Task SelectRoute_AndViewAllRoutes_RaiseTheirEvents()
    {
        RouteResponse route = FleetTestData.Route("R-001");
        DashboardViewModel viewModel = CreateViewModel(FakeFleetGoApiClient.WithRoutes(route));

        Guid? selected = null;
        bool viewAllRequested = false;
        viewModel.RouteSelected += (_, routeId) => selected = routeId;
        viewModel.ViewAllRoutesRequested += (_, _) => viewAllRequested = true;

        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.SelectRouteCommand.Execute(viewModel.TodayRoutes[0]);
        viewModel.ViewAllRoutesCommand.Execute(null);

        Assert.Equal(route.Id, selected);
        Assert.True(viewAllRequested);
    }

    private static DashboardViewModel CreateViewModel(
        FakeFleetGoApiClient apiClient,
        FakeTimeProvider? timeProvider = null) =>
        new(apiClient, new FakeAuthenticationService(), timeProvider ?? new FakeTimeProvider(Now));

    private static DashboardViewModel CreateViewModel(
        FakeFleetGoApiClient apiClient,
        FakeAuthenticationService authenticationService) =>
        new(apiClient, authenticationService, new FakeTimeProvider(Now));
}
