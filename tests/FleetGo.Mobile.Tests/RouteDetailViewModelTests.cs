using System.Net;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public sealed class RouteDetailViewModelTests
{
    [Fact]
    public async Task Load_PopulatesTheRouteAndItsStops()
    {
        Guid routeId = Guid.NewGuid();
        RouteResponse route = FleetTestData.Route("R-777", RouteStatus.InProgress, stopCount: 2, id: routeId);

        FakeFleetGoApiClient apiClient = new()
        {
            GetRouteHandler = (_, _) => Task.FromResult(route),
            GetStopsHandler = (_, page, pageSize, _, _, _, _) => Task.FromResult(
                FleetTestData.PageOf(
                    page,
                    pageSize,
                    totalCount: 2,
                    FleetTestData.Stop(sequence: 1, customerName: "Acme Ltd"),
                    FleetTestData.Stop(sequence: 2, customerName: "Globex"))),
        };

        RouteDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(routeId, TestContext.Current.CancellationToken);

        Assert.NotNull(viewModel.Route);
        Assert.Equal("R-777", viewModel.Route.RouteNumber);
        Assert.True(viewModel.HasRoute);
        Assert.Equal(2, viewModel.Stops.Count);
        Assert.Equal("Globex", viewModel.Stops[1].CustomerName);
        Assert.False(viewModel.HasNoStops);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.ErrorMessage);

        // The stop query has to be scoped to this route, not to every route the driver owns.
        Assert.Equal(routeId, Assert.Single(apiClient.StopQueryRouteIds));
    }

    [Fact]
    public async Task Load_WithNoStops_ReportsTheEmptyState()
    {
        FakeFleetGoApiClient apiClient = new()
        {
            GetRouteHandler = (_, _) => Task.FromResult(FleetTestData.Route("R-001")),
        };

        RouteDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.Stops);
        Assert.True(viewModel.HasNoStops);
        Assert.True(viewModel.HasRoute);
    }

    [Fact]
    public async Task Load_FetchesTheAssignedVehicle()
    {
        Guid vehicleId = Guid.NewGuid();
        FakeFleetGoApiClient apiClient = new()
        {
            GetRouteHandler = (_, _) => Task.FromResult(
                FleetTestData.Route("R-001", vehicleId: vehicleId, vehicleRegistrationNumber: "VAN-01")),
            GetVehicleHandler = (_, _) => Task.FromResult(FleetTestData.Vehicle("VAN-01", "Mercedes", "Sprinter", vehicleId)),
        };

        RouteDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasVehicle);
        Assert.Equal("Sprinter", viewModel.Vehicle!.Model);
        Assert.True(viewModel.HasVehicleRegistration);
    }

    [Fact]
    public async Task Load_WithNoVehicleAssigned_LeavesTheVehicleEmpty()
    {
        FakeFleetGoApiClient apiClient = new()
        {
            GetRouteHandler = (_, _) => Task.FromResult(FleetTestData.Route("R-001")),
            GetVehicleHandler = (_, _) => throw new InvalidOperationException("must not be called"),
        };

        RouteDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.False(viewModel.HasVehicle);
        Assert.False(viewModel.HasVehicleRegistration);
        Assert.True(viewModel.HasRoute);
    }

    [Fact]
    public async Task Load_StillShowsTheRoute_WhenTheVehicleLookupFails()
    {
        FakeFleetGoApiClient apiClient = new()
        {
            GetRouteHandler = (_, _) => Task.FromResult(
                FleetTestData.Route("R-001", vehicleId: Guid.NewGuid(), vehicleRegistrationNumber: "VAN-01")),
            GetVehicleHandler = (_, _) => Task.FromException<VehicleResponse>(new HttpRequestException("no network")),
        };

        RouteDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        // Losing the make/model must not cost the driver the route and its stops.
        Assert.True(viewModel.HasRoute);
        Assert.False(viewModel.HasVehicle);
        Assert.Null(viewModel.ErrorMessage);
        // The registration still shows, because it travels on the route itself.
        Assert.True(viewModel.HasVehicleRegistration);
    }

    [Fact]
    public async Task Load_WhenTheRouteIsNotFound_ShowsTheNotFoundMessage()
    {
        FakeFleetGoApiClient apiClient = new()
        {
            GetRouteHandler = (_, _) => Task.FromException<RouteResponse>(
                new FleetGoApiException("not found", HttpStatusCode.NotFound)),
        };

        RouteDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal("We couldn't find that. It may have been removed.", viewModel.ErrorMessage);
        Assert.False(viewModel.HasRoute);
        Assert.False(viewModel.HasNoStops);
    }

    [Fact]
    public async Task Reload_RequestsTheSameRouteAgain()
    {
        List<Guid> requested = [];
        FakeFleetGoApiClient apiClient = new()
        {
            GetRouteHandler = (routeId, _) =>
            {
                requested.Add(routeId);
                return Task.FromResult(FleetTestData.Route("R-001"));
            },
        };

        Guid id = Guid.NewGuid();
        RouteDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(id, TestContext.Current.CancellationToken);
        await viewModel.ReloadCommand.ExecuteAsync(null);

        Assert.Equal(new[] { id, id }, requested);
    }

    [Fact]
    public async Task SelectStop_RaisesStopSelectedWithTheStopId()
    {
        StopResponse stop = FleetTestData.Stop(sequence: 1);
        FakeFleetGoApiClient apiClient = new()
        {
            GetStopsHandler = (_, page, pageSize, _, _, _, _) =>
                Task.FromResult(FleetTestData.PageOf(page, pageSize, totalCount: 1, stop)),
        };

        RouteDetailViewModel viewModel = new(apiClient);

        Guid? selected = null;
        viewModel.StopSelected += (_, stopId) => selected = stopId;

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);
        viewModel.SelectStopCommand.Execute(viewModel.Stops[0]);

        Assert.Equal(stop.Id, selected);
    }
}
