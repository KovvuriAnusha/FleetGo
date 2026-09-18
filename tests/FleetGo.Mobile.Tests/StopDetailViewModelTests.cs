using System.Net;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Tests;

public sealed class StopDetailViewModelTests
{
    [Fact]
    public async Task Load_PopulatesTheStopItsCustomerAndItsPackages()
    {
        Guid stopId = Guid.NewGuid();
        Guid customerId = Guid.NewGuid();

        FakeFleetGoApiClient apiClient = new()
        {
            GetStopHandler = (_, _) => Task.FromResult(
                FleetTestData.Stop(
                    sequence: 3,
                    customerName: "Acme Ltd",
                    status: StopStatus.Arrived,
                    packageCount: 2,
                    id: stopId,
                    customerId: customerId,
                    deliveryNotes: "Gate code 1234")),
            GetCustomerHandler = (_, _) => Task.FromResult(
                FleetTestData.Customer("Acme Ltd", "42 Depot Road", "Testville", "TE5 7CD", customerId)),
            GetPackagesHandler = (_, page, pageSize, _, _, _) => Task.FromResult(
                FleetTestData.PageOf(
                    page,
                    pageSize,
                    totalCount: 2,
                    FleetTestData.Package("TRK-001", PackageStatus.OutForDelivery, "Small box"),
                    FleetTestData.Package("TRK-002", PackageStatus.Pending))),
        };

        StopDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(stopId, TestContext.Current.CancellationToken);

        Assert.True(viewModel.HasStop);
        Assert.Equal(3, viewModel.Stop!.Sequence);
        Assert.Equal(StopStatus.Arrived, viewModel.Stop.Status);
        Assert.Equal("Gate code 1234", viewModel.Stop.DeliveryNotes);

        Assert.True(viewModel.HasCustomer);
        Assert.Equal("42 Depot Road", viewModel.Customer!.AddressLine1);

        Assert.Equal(2, viewModel.Packages.Count);
        Assert.Equal("TRK-001", viewModel.Packages[0].TrackingNumber);
        Assert.Equal(PackageStatus.OutForDelivery, viewModel.Packages[0].Status);
        Assert.False(viewModel.HasNoPackages);
        Assert.Null(viewModel.ErrorMessage);

        // Packages must be scoped to this stop, not to every stop the driver owns.
        Assert.Equal(stopId, Assert.Single(apiClient.PackageQueryStopIds));
    }

    [Fact]
    public async Task Load_WithNoPackages_ReportsTheEmptyState()
    {
        FakeFleetGoApiClient apiClient = new();
        StopDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Empty(viewModel.Packages);
        Assert.True(viewModel.HasNoPackages);
        Assert.True(viewModel.HasStop);
        Assert.False(viewModel.HasErrorMessage);
    }

    [Fact]
    public async Task Load_StillShowsTheStop_WhenTheCustomerLookupFails()
    {
        FakeFleetGoApiClient apiClient = new()
        {
            GetCustomerHandler = (_, _) => Task.FromException<CustomerResponse>(new HttpRequestException("no network")),
        };

        StopDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        // The customer's name travels on the stop, so the screen is still useful without the
        // full customer record - that lookup failing must not replace everything with an error.
        Assert.True(viewModel.HasStop);
        Assert.False(viewModel.HasCustomer);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Load_WhenTheStopIsNotFound_ShowsTheNotFoundMessage()
    {
        FakeFleetGoApiClient apiClient = new()
        {
            GetStopHandler = (_, _) => Task.FromException<StopResponse>(
                new FleetGoApiException("not found", HttpStatusCode.NotFound)),
        };

        StopDetailViewModel viewModel = new(apiClient);

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal("We couldn't find that. It may have been removed.", viewModel.ErrorMessage);
        Assert.False(viewModel.HasStop);
        Assert.False(viewModel.HasNoPackages);
    }

    [Fact]
    public async Task Load_WhenTheSessionHasExpired_RaisesSessionExpired()
    {
        FakeFleetGoApiClient apiClient = new()
        {
            GetStopHandler = (_, _) => Task.FromException<StopResponse>(
                new FleetGoApiException("unauthorized", HttpStatusCode.Unauthorized)),
        };

        StopDetailViewModel viewModel = new(apiClient);

        bool raised = false;
        viewModel.SessionExpired += (_, _) => raised = true;

        await viewModel.LoadAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.True(raised);
    }
}
