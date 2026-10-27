using System.Net;
using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Mobile.Tests.Offline;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public class RouteDetailViewModelTests
{
    [Fact]
    public async Task Load_PopulatesTheRouteAndItsStops()
    {
        RouteResponse route = FleetTestData.Route("R-100", RouteStatus.InProgress, stopCount: 2);

        var repository = RepositoryReturning(new RouteDetail(
            route,
            [FleetTestData.Stop(sequence: 1), FleetTestData.Stop(sequence: 2)],
            Vehicle: null));

        RouteDetailViewModel viewModel = Build(repository);

        await viewModel.LoadAsync(route.Id);

        Assert.True(viewModel.HasRoute);
        Assert.Equal("R-100", viewModel.Route!.RouteNumber);
        Assert.Equal(2, viewModel.Stops.Count);
        Assert.False(viewModel.HasNoStops);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task Load_WithNoStops_ReportsTheEmptyState()
    {
        RouteResponse route = FleetTestData.Route();
        RouteDetailViewModel viewModel = Build(RepositoryReturning(new RouteDetail(route, [], null)));

        await viewModel.LoadAsync(route.Id);

        Assert.True(viewModel.HasNoStops);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task Load_ShowsTheAssignedVehicleWhenTheDeviceHasIt()
    {
        Guid vehicleId = Guid.NewGuid();
        RouteResponse route = FleetTestData.Route(vehicleId: vehicleId, vehicleRegistrationNumber: "VAN-07");

        var repository = RepositoryReturning(new RouteDetail(
            route,
            [],
            FleetTestData.Vehicle("VAN-07", "Ford", "Transit", vehicleId)));

        RouteDetailViewModel viewModel = Build(repository);

        await viewModel.LoadAsync(route.Id);

        Assert.True(viewModel.HasVehicle);
        Assert.Equal("Transit", viewModel.Vehicle!.Model);
        Assert.True(viewModel.HasVehicleRegistration);
    }

    [Fact]
    public async Task Load_StillShowsTheRoute_WhenTheVehicleRecordIsNotCached()
    {
        // The registration number travels on the route itself, so the screen stays useful
        // even when the fuller vehicle record has never been synced.
        RouteResponse route = FleetTestData.Route(vehicleId: Guid.NewGuid(), vehicleRegistrationNumber: "VAN-07");
        RouteDetailViewModel viewModel = Build(RepositoryReturning(new RouteDetail(route, [], null)));

        await viewModel.LoadAsync(route.Id);

        Assert.True(viewModel.HasRoute);
        Assert.False(viewModel.HasVehicle);
        Assert.True(viewModel.HasVehicleRegistration);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task Load_WhenTheRouteIsNotOnTheDevice_SaysSoRatherThanFailing()
    {
        RouteDetailViewModel viewModel = Build(new FakeFleetRepository());

        await viewModel.LoadAsync(Guid.NewGuid());

        Assert.False(viewModel.HasRoute);
        Assert.Contains("isn't saved on this device", viewModel.ErrorMessage!);
        // Not an empty-stops state: the route itself is missing, which is a different problem.
        Assert.False(viewModel.HasNoStops);
    }

    [Fact]
    public async Task Load_WhenTheSessionHasExpired_RaisesSessionExpired()
    {
        var repository = new FakeFleetRepository
        {
            RouteDetailHandler = (_, _) => Task.FromException<CachedData<RouteDetail>>(
                new FleetGoApiException("expired", HttpStatusCode.Unauthorized)),
        };

        RouteDetailViewModel viewModel = Build(repository);

        bool raised = false;
        viewModel.SessionExpired += (_, _) => raised = true;

        await viewModel.LoadAsync(Guid.NewGuid());

        Assert.True(raised);
    }

    [Fact]
    public async Task Reload_TriesToPullTheRouteDownAgainAndThenRereadsTheDevice()
    {
        RouteResponse route = FleetTestData.Route();
        var repository = RepositoryReturning(new RouteDetail(route, [], null));

        RouteDetailViewModel viewModel = Build(repository);

        await viewModel.LoadAsync(route.Id);
        await viewModel.ReloadCommand.ExecuteAsync(null);

        AssertThat.Sequence([route.Id], repository.RefreshedRouteIds);
        Assert.True(viewModel.HasRoute);
    }

    [Fact]
    public async Task Reload_WithNoNetwork_StillShowsWhatTheDeviceHas()
    {
        RouteResponse route = FleetTestData.Route();
        var repository = RepositoryReturning(new RouteDetail(route, [FleetTestData.Stop()], null));
        repository.RefreshException = new HttpRequestException("no network");

        RouteDetailViewModel viewModel = Build(repository);

        await viewModel.ReloadCommand.ExecuteAsync(null);

        // Losing the network is not a failed load once the data lives on the device.
        Assert.False(viewModel.HasErrorMessage);
        Assert.Single(viewModel.Stops);
    }

    [Fact]
    public async Task SelectStop_RaisesStopSelectedWithTheStopId()
    {
        StopResponse stop = FleetTestData.Stop();
        RouteDetailViewModel viewModel = Build(new FakeFleetRepository());

        Guid? selected = null;
        viewModel.StopSelected += (_, id) => selected = id;

        viewModel.SelectStopCommand.Execute(stop);

        Assert.Equal(stop.Id, selected);
    }

    private static FakeFleetRepository RepositoryReturning(RouteDetail detail, bool hasPendingLocalChange = false) =>
        new()
        {
            RouteDetailHandler = (_, _) => Task.FromResult(
                new CachedData<RouteDetail>(detail, DateTime.UnixEpoch, hasPendingLocalChange)),
        };

    private static RouteDetailViewModel Build(FakeFleetRepository repository) =>
        new(repository, OfflineTestFactory.SyncStatus());
}
