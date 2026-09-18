using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// Backs the route detail screen: one route, the vehicle assigned to it (when there is one),
/// and its stops in running order.
/// </summary>
public sealed partial class RouteDetailViewModel : FleetPageViewModel
{
    /// <summary>
    /// A single route's stops comfortably fit one page - a working day is tens of stops, not
    /// hundreds - so this screen loads them in one call rather than paging a list the driver
    /// wants to see whole.
    /// </summary>
    private const int StopPageSize = 100;

    private readonly IFleetGoApiClient _apiClient;

    /// <summary>Remembered so <see cref="ReloadCommand"/> can retry without the page passing the id again.</summary>
    private Guid _routeId;

    public RouteDetailViewModel(IFleetGoApiClient apiClient)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        _apiClient = apiClient;
    }

    /// <summary>Raised when a driver picks a stop. The page turns this into Shell navigation.</summary>
    public event EventHandler<Guid>? StopSelected;

    public ObservableCollection<StopResponse> Stops { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoute))]
    [NotifyPropertyChangedFor(nameof(HasVehicleRegistration))]
    public partial RouteResponse? Route { get; set; }

    /// <summary>
    /// Null when the route has no vehicle assigned yet, and also when the vehicle lookup
    /// itself failed - see <see cref="LoadVehicleAsync"/> for why that is not treated as a
    /// failure of the whole screen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVehicle))]
    public partial VehicleResponse? Vehicle { get; set; }

    /// <summary>True once the route loaded and genuinely has no stops on it.</summary>
    [ObservableProperty]
    public partial bool HasNoStops { get; set; }

    public bool HasRoute => Route is not null;

    /// <summary>
    /// Whether a vehicle is assigned at all. The registration number travels on the route
    /// itself, so this is true even when the fuller vehicle record could not be loaded.
    /// </summary>
    public bool HasVehicleRegistration => !string.IsNullOrEmpty(Route?.VehicleRegistrationNumber);

    public bool HasVehicle => Vehicle is not null;

    /// <summary>
    /// Loads the route, its stops and its vehicle. Called by the page on appearing with the
    /// id Shell handed it.
    /// </summary>
    public async Task LoadAsync(Guid routeId, CancellationToken cancellationToken = default)
    {
        _routeId = routeId;

        bool succeeded = await RunAsync(
            async token =>
            {
                RouteResponse route = await _apiClient.GetRouteAsync(routeId, token);

                PagedResponse<StopResponse> stops = await _apiClient.GetStopsAsync(
                    routeId: routeId,
                    page: 1,
                    pageSize: StopPageSize,
                    sort: "sequence",
                    cancellationToken: token);

                Route = route;

                Stops.Clear();

                foreach (StopResponse stop in stops.Items)
                {
                    Stops.Add(stop);
                }

                await LoadVehicleAsync(route, token);
            },
            cancellationToken);

        HasNoStops = succeeded && Stops.Count == 0;
    }

    /// <summary>Retry after a failure, and the pull-to-refresh action.</summary>
    [RelayCommand]
    private Task ReloadAsync(CancellationToken cancellationToken) => LoadAsync(_routeId, cancellationToken);

    [RelayCommand]
    private void SelectStop(StopResponse? stop)
    {
        if (stop is not null)
        {
            StopSelected?.Invoke(this, stop.Id);
        }
    }

    /// <summary>
    /// Fetches the assigned vehicle's full record for the make/model the route summary does
    /// not carry. Failures here are swallowed deliberately: the route and its stops are what
    /// this screen is for, and losing the vehicle's make is not worth replacing all of it
    /// with an error. The registration number still shows, because it travels on the route
    /// itself.
    /// </summary>
    private async Task LoadVehicleAsync(RouteResponse route, CancellationToken cancellationToken)
    {
        if (route.VehicleId is null)
        {
            Vehicle = null;
            return;
        }

        try
        {
            Vehicle = await _apiClient.GetVehicleAsync(route.VehicleId.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is FleetGoApiException or HttpRequestException)
        {
            Vehicle = null;
        }
    }
}
