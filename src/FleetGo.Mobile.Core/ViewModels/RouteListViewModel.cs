using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>One entry in the status filter. "All" is the option with a null <see cref="Value"/>.</summary>
public sealed record RouteStatusFilterOption(string Label, RouteStatus? Value);

/// <summary>
/// Backs the route list: the driver's own routes, newest first, filtered by status and paged
/// on demand. Every call is scoped to the caller server-side, so there is no driver id to
/// pass or to get wrong here.
/// </summary>
public sealed partial class RouteListViewModel : FleetPageViewModel
{
    /// <summary>Matches the API's default page size; the endpoint caps any request at 100.</summary>
    private const int RoutePageSize = 20;

    /// <summary>Newest route first - the one a driver is most likely to be looking for.</summary>
    private const string SortNewestFirst = "-routeDate";

    private readonly IFleetGoApiClient _apiClient;

    public RouteListViewModel(IFleetGoApiClient apiClient)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        _apiClient = apiClient;

        StatusFilterOptions =
        [
            new RouteStatusFilterOption("All routes", null),
            new RouteStatusFilterOption("Planned", RouteStatus.Planned),
            new RouteStatusFilterOption("In progress", RouteStatus.InProgress),
            new RouteStatusFilterOption("Completed", RouteStatus.Completed),
            new RouteStatusFilterOption("Cancelled", RouteStatus.Cancelled),
        ];

        SelectedStatusFilter = StatusFilterOptions[0];
    }

    /// <summary>Raised when a driver picks a route. The page turns this into Shell navigation.</summary>
    public event EventHandler<Guid>? RouteSelected;

    public ObservableCollection<RouteResponse> Routes { get; } = [];

    public IReadOnlyList<RouteStatusFilterOption> StatusFilterOptions { get; }

    /// <summary>
    /// Changing this does not reload on its own - the page calls <see cref="RefreshCommand"/>
    /// when the picker changes, the same way the existing pages drive commands from
    /// code-behind handlers rather than binding a command to a picker.
    /// </summary>
    [ObservableProperty]
    public partial RouteStatusFilterOption SelectedStatusFilter { get; set; }

    /// <summary>Bound to the RefreshView; separate from IsBusy so a pull-to-refresh does not also raise the full-page spinner.</summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    /// <summary>Total routes matching the current filter, across all pages - not the number loaded so far.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    public partial int TotalCount { get; set; }

    /// <summary>True only once a load has finished and genuinely returned nothing (see <see cref="FleetPageViewModel.HasLoaded"/>).</summary>
    [ObservableProperty]
    public partial bool IsEmpty { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    public partial bool IsLoadingMore { get; set; }

    public bool CanLoadMore => !IsLoadingMore && Routes.Count < TotalCount;

    /// <summary>The page number most recently loaded; the next load-more asks for this plus one.</summary>
    private int LoadedPage { get; set; }

    /// <summary>
    /// Bumped by every first-page load. A load-more captures the generation it was started
    /// under and discards its response if that no longer matches, because a refresh or a
    /// filter change in the meantime means those rows belong to a result set that is no
    /// longer on screen.
    /// </summary>
    private int _loadGeneration;

    /// <summary>First load, and the retry action after a failure. Always starts again from page 1.</summary>
    [RelayCommand]
    private Task LoadAsync(CancellationToken cancellationToken) => LoadFirstPageAsync(cancellationToken, showBusyIndicator: true);

    /// <summary>Pull-to-refresh. Same query as <see cref="LoadAsync"/>, without the full-page spinner.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;

        try
        {
            await LoadFirstPageAsync(cancellationToken, showBusyIndicator: false);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Appends the next page. Silently does nothing when everything matching the filter is
    /// already loaded, so the list can call this freely as it scrolls.
    /// </summary>
    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (!CanLoadMore)
        {
            return;
        }

        IsLoadingMore = true;

        int generation = _loadGeneration;

        try
        {
            await RunAsync(
                async token =>
                {
                    PagedResponse<RouteResponse> page = await _apiClient.GetRoutesAsync(
                        page: LoadedPage + 1,
                        pageSize: RoutePageSize,
                        status: SelectedStatusFilter.Value,
                        sort: SortNewestFirst,
                        cancellationToken: token);

                    // A refresh or filter change landed while this page was in flight: these
                    // rows, and the paging position they imply, belong to the previous query.
                    if (generation != _loadGeneration)
                    {
                        return;
                    }

                    foreach (RouteResponse route in page.Items)
                    {
                        Routes.Add(route);
                    }

                    LoadedPage = page.Page;
                    TotalCount = page.TotalCount;
                },
                cancellationToken,
                showBusyIndicator: false);
        }
        finally
        {
            IsLoadingMore = false;
            OnPropertyChanged(nameof(CanLoadMore));
        }
    }

    [RelayCommand]
    private void SelectRoute(RouteResponse? route)
    {
        if (route is not null)
        {
            RouteSelected?.Invoke(this, route.Id);
        }
    }

    private async Task LoadFirstPageAsync(CancellationToken cancellationToken, bool showBusyIndicator)
    {
        int generation = ++_loadGeneration;

        bool succeeded = await RunAsync(
            async token =>
            {
                PagedResponse<RouteResponse> page = await _apiClient.GetRoutesAsync(
                    page: 1,
                    pageSize: RoutePageSize,
                    status: SelectedStatusFilter.Value,
                    sort: SortNewestFirst,
                    cancellationToken: token);

                // Superseded by a newer first-page load (two refreshes racing): let the newer
                // one own the list rather than overwriting it with an older result.
                if (generation != _loadGeneration)
                {
                    return;
                }

                // Replaced only after the call succeeds: a failed refresh should leave the
                // routes already on screen alone rather than blanking the list behind an
                // error message.
                Routes.Clear();

                foreach (RouteResponse route in page.Items)
                {
                    Routes.Add(route);
                }

                LoadedPage = page.Page;
                TotalCount = page.TotalCount;
            },
            cancellationToken,
            showBusyIndicator);

        IsEmpty = succeeded && Routes.Count == 0;
        OnPropertyChanged(nameof(CanLoadMore));
    }
}
