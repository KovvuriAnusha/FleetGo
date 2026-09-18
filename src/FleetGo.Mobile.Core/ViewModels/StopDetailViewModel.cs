using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// Backs the stop detail screen: one stop, the customer it delivers to, and the packages
/// going to that stop.
/// </summary>
public sealed partial class StopDetailViewModel : FleetPageViewModel
{
    /// <summary>One stop's packages are a handful, so they load in a single call.</summary>
    private const int PackagePageSize = 100;

    private readonly IFleetGoApiClient _apiClient;

    /// <summary>Remembered so <see cref="ReloadCommand"/> can retry without the page passing the id again.</summary>
    private Guid _stopId;

    public StopDetailViewModel(IFleetGoApiClient apiClient)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        _apiClient = apiClient;
    }

    public ObservableCollection<PackageResponse> Packages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStop))]
    public partial StopResponse? Stop { get; set; }

    /// <summary>
    /// The customer's full record - address, phone, email - which the stop summary does not
    /// carry. Null when that lookup failed; the customer's name still shows, because it
    /// travels on the stop itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomer))]
    public partial CustomerResponse? Customer { get; set; }

    /// <summary>True once the stop loaded and genuinely has no packages on it.</summary>
    [ObservableProperty]
    public partial bool HasNoPackages { get; set; }

    public bool HasStop => Stop is not null;

    public bool HasCustomer => Customer is not null;

    /// <summary>Loads the stop, its customer and its packages.</summary>
    public async Task LoadAsync(Guid stopId, CancellationToken cancellationToken = default)
    {
        _stopId = stopId;

        bool succeeded = await RunAsync(
            async token =>
            {
                StopResponse stop = await _apiClient.GetStopAsync(stopId, token);

                PagedResponse<PackageResponse> packages = await _apiClient.GetPackagesAsync(
                    stopId: stopId,
                    page: 1,
                    pageSize: PackagePageSize,
                    cancellationToken: token);

                Stop = stop;

                Packages.Clear();

                foreach (PackageResponse package in packages.Items)
                {
                    Packages.Add(package);
                }

                await LoadCustomerAsync(stop, token);
            },
            cancellationToken);

        HasNoPackages = succeeded && Packages.Count == 0;
    }

    /// <summary>Retry after a failure, and the pull-to-refresh action.</summary>
    [RelayCommand]
    private Task ReloadAsync(CancellationToken cancellationToken) => LoadAsync(_stopId, cancellationToken);

    /// <summary>
    /// Customers are shared reference data, so this is a second call rather than something
    /// the stop payload could have carried. A failure is swallowed for the same reason the
    /// vehicle lookup is on route detail - see <c>RouteDetailViewModel.LoadVehicleAsync</c>.
    /// </summary>
    private async Task LoadCustomerAsync(StopResponse stop, CancellationToken cancellationToken)
    {
        try
        {
            Customer = await _apiClient.GetCustomerAsync(stop.CustomerId, cancellationToken);
        }
        catch (Exception exception) when (exception is FleetGoApiException or HttpRequestException)
        {
            Customer = null;
        }
    }
}
