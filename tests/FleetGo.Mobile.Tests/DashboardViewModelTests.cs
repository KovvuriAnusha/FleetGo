using System.Net;
using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Mobile.Tests.Offline;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public class DashboardViewModelTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    [Fact]
    public async Task Load_SummarisesTodaysRoutes()
    {
        var repository = RepositoryReturning(
            FleetTestData.Route("R-001", RouteStatus.Planned, stopCount: 4),
            FleetTestData.Route("R-002", RouteStatus.InProgress, stopCount: 6),
            FleetTestData.Route("R-003", RouteStatus.Completed, stopCount: 2));

        DashboardViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(3, viewModel.TodayRoutes.Count);
        Assert.Equal(1, viewModel.PlannedCount);
        Assert.Equal(1, viewModel.InProgressCount);
        Assert.Equal(1, viewModel.CompletedCount);
        Assert.Equal(12, viewModel.StopsToday);
        Assert.False(viewModel.HasNoRoutesToday);
    }

    [Fact]
    public async Task Load_ExcludesCancelledRoutesFromTheStopCount()
    {
        var repository = RepositoryReturning(
            FleetTestData.Route("R-001", RouteStatus.Planned, stopCount: 5),
            FleetTestData.Route("R-002", RouteStatus.Cancelled, stopCount: 9));

        DashboardViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        // The tiles above never counted cancelled routes, so the stop total must not either.
        Assert.Equal(5, viewModel.StopsToday);
    }

    [Fact]
    public async Task Load_PicksTheInProgressRouteAsTheActiveOne()
    {
        var repository = RepositoryReturning(
            FleetTestData.Route("R-001", RouteStatus.Planned),
            FleetTestData.Route("R-002", RouteStatus.InProgress));

        DashboardViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasActiveRoute);
        Assert.Equal("R-002", viewModel.ActiveRoute!.RouteNumber);
    }

    [Fact]
    public async Task Load_FallsBackToTheFirstPlannedRoute_WhenNothingIsInProgress()
    {
        var repository = RepositoryReturning(
            FleetTestData.Route("R-001", RouteStatus.Completed),
            FleetTestData.Route("R-002", RouteStatus.Planned));

        DashboardViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("R-002", viewModel.ActiveRoute!.RouteNumber);
    }

    [Fact]
    public async Task Load_ReadsTheDeviceForTodayOnly_AndNeverTheApi()
    {
        var repository = new FakeFleetRepository();
        var asked = new List<DateOnly>();

        repository.RoutesForDateHandler = (date, _) =>
        {
            asked.Add(date);
            return Task.FromResult(new CachedData<IReadOnlyList<RouteResponse>>([], null, false));
        };

        DashboardViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        // The dashboard reads the device. Anything to do with the network is the sync
        // engine's job, which is why no refresh was asked for here either.
        AssertThat.Sequence([Today], asked);
        Assert.Equal(0, repository.RouteWindowRefreshCount);
    }

    [Fact]
    public async Task Load_WithNothingCached_ReportsTheEmptyState()
    {
        DashboardViewModel viewModel = Build(new FakeFleetRepository());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasNoRoutesToday);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task Load_WhenTheReadFails_ShowsAnErrorAndNotTheEmptyState()
    {
        var repository = new FakeFleetRepository
        {
            RoutesForDateHandler = (_, _) =>
                Task.FromException<CachedData<IReadOnlyList<RouteResponse>>>(
                    new FleetGoApiException("boom", HttpStatusCode.InternalServerError)),
        };

        DashboardViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasErrorMessage);
        // "Nothing scheduled today" after a failure would be a lie: the app does not know.
        Assert.False(viewModel.HasNoRoutesToday);
    }

    [Fact]
    public async Task Load_SurfacesThatSomethingIsStillWaitingToSend()
    {
        var repository = new FakeFleetRepository
        {
            RoutesForDateHandler = (_, _) => Task.FromResult(
                new CachedData<IReadOnlyList<RouteResponse>>([FleetTestData.Route()], DateTime.UnixEpoch, true)),
        };

        DashboardViewModel viewModel = Build(repository);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasPendingLocalChange);
    }

    [Fact]
    public async Task Load_GreetsTheSignedInDriver()
    {
        var authentication = new FakeAuthenticationService();
        authentication.SignIn();

        DashboardViewModel viewModel = Build(new FakeFleetRepository(), authentication);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Test Driver", viewModel.DriverName);
        Assert.Equal("D-1001", viewModel.DriverCode);
        Assert.True(viewModel.HasDriverCode);
    }

    [Fact]
    public async Task Load_WithNoProfileLoaded_FallsBackToAGenericGreeting()
    {
        // Exactly what resuming offline looks like: tokens restored, profile call impossible.
        var authentication = new FakeAuthenticationService();
        authentication.SignInWithoutProfile();

        DashboardViewModel viewModel = Build(new FakeFleetRepository(), authentication);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("Driver", viewModel.DriverName);
        Assert.False(viewModel.HasDriverCode);
    }

    [Fact]
    public async Task Refresh_AsksForASyncAndThenRereadsTheDevice()
    {
        var repository = RepositoryReturning(FleetTestData.Route("R-001"));
        var trigger = new FakeSyncTrigger();

        DashboardViewModel viewModel = Build(repository, trigger: trigger);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        AssertThat.Sequence([SyncReason.Manual], trigger.AwaitedReasons);
        Assert.Single(viewModel.TodayRoutes);
        Assert.False(viewModel.IsRefreshing);
    }

    [Fact]
    public async Task SelectRoute_AndViewAllRoutes_RaiseTheirEvents()
    {
        RouteResponse route = FleetTestData.Route();
        DashboardViewModel viewModel = Build(RepositoryReturning(route));

        Guid? selected = null;
        bool viewAllRequested = false;

        viewModel.RouteSelected += (_, id) => selected = id;
        viewModel.ViewAllRoutesRequested += (_, _) => viewAllRequested = true;

        viewModel.SelectRouteCommand.Execute(route);
        viewModel.ViewAllRoutesCommand.Execute(null);

        Assert.Equal(route.Id, selected);
        Assert.True(viewAllRequested);
    }

    private static FakeFleetRepository RepositoryReturning(params RouteResponse[] routes) =>
        new()
        {
            RoutesForDateHandler = (_, _) => Task.FromResult(
                new CachedData<IReadOnlyList<RouteResponse>>(routes, DateTime.UnixEpoch, false)),
        };

    private static DashboardViewModel Build(
        FakeFleetRepository repository,
        FakeAuthenticationService? authentication = null,
        FakeSyncTrigger? trigger = null) =>
        new(
            repository,
            authentication ?? new FakeAuthenticationService(),
            trigger ?? new FakeSyncTrigger(),
            OfflineTestFactory.SyncStatus(),
            new FakeTimeProvider(new DateTimeOffset(Today, new TimeOnly(9, 0), TimeSpan.Zero)));
}
