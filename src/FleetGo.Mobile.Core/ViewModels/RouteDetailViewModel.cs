using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Mobile.Core.Offline;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// Backs the route detail screen: one route, the vehicle assigned to it (when there is one),
/// and its stops in running order - all read from the device.
/// </summary>
public sealed partial class RouteDetailViewModel : FleetPageViewModel
{
    private readonly IFleetRepository _repository;

    /// <summary>Remembered so <see cref="ReloadCommand"/> can retry without the page passing the id again.</summary>
    private Guid _routeId;

    public RouteDetailViewModel(IFleetRepository repository, SyncStatusViewModel sync)
        : base(sync)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>Raised when a driver picks a stop. The page turns this into Shell navigation.</summary>
    public event EventHandler<Guid>? StopSelected;

    public ObservableCollection<StopResponse> Stops { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRoute))]
    [NotifyPropertyChangedFor(nameof(HasVehicleRegistration))]
    public partial RouteResponse? Route { get; set; }

    /// <summary>
    /// Null when the route has no vehicle assigned yet, and also when the vehicle has never
    /// been cached on this device.
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
    /// itself, so this is true even when the fuller vehicle record is not cached.
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
            token => LoadFromDeviceAsync(routeId, token),
            cancellationToken);

        HasNoStops = succeeded && HasRoute && Stops.Count == 0;
    }

    /// <summary>
    /// Retry after a failure, and the pull-to-refresh action. Tries to pull this one route
    /// down again first, then re-reads the device either way - so a refresh with no signal
    /// still repaints rather than failing.
    /// </summary>
    [RelayCommand]
    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        Guid routeId = _routeId;

        bool succeeded = await RunAsync(
            async token =>
            {
                await TryRefreshAsync(() => _repository.RefreshRouteDetailAsync(routeId, token));
                await LoadFromDeviceAsync(routeId, token);
            },
            cancellationToken);

        HasNoStops = succeeded && HasRoute && Stops.Count == 0;
    }

    [RelayCommand]
    private void SelectStop(StopResponse? stop)
    {
        if (stop is not null)
        {
            StopSelected?.Invoke(this, stop.Id);
        }
    }

    private async Task LoadFromDeviceAsync(Guid routeId, CancellationToken cancellationToken)
    {
        CachedData<RouteDetail> cached = await _repository.GetRouteDetailAsync(routeId, cancellationToken);

        Route = cached.Value.Route;
        Vehicle = cached.Value.Vehicle;
        HasPendingLocalChange = cached.HasPendingLocalChange;

        Stops.Clear();

        foreach (StopResponse stop in cached.Value.Stops)
        {
            Stops.Add(stop);
        }

        if (Route is null)
        {
            // Not an API failure - the device simply does not have this route, and without a
            // network there is nowhere to get it from. Saying so is more useful than a
            // generic error, because the fix ("open it once with a connection") is different.
            ErrorMessage = "This route isn't saved on this device yet. Connect to a network and pull down to refresh.";
        }
    }
}
