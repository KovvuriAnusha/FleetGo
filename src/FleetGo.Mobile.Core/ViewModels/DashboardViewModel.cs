using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Mobile.Core.Session;
using FleetGo.Shared.Contracts;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Contracts.Fleet;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// Backs the driver dashboard: who is signed in, and what today looks like.
/// <para>
/// There is no dashboard aggregation endpoint in Phase 4A and this does not pretend
/// otherwise - the summary is composed client-side from a single call for today's routes
/// (<c>GET /api/v1/fleet/routes?routeDate=...</c>), and the driver's identity comes from the
/// session that is already loaded. Nothing here needs an endpoint that does not exist.
/// </para>
/// </summary>
public sealed partial class DashboardViewModel : FleetPageViewModel
{
    /// <summary>
    /// One day's routes for one driver fit a single page many times over; the API caps a
    /// page at 100, which is the ceiling this summary is accurate up to.
    /// </summary>
    private const int TodayRoutePageSize = 100;

    private readonly IFleetGoApiClient _apiClient;
    private readonly IAuthenticationService _authenticationService;
    private readonly TimeProvider _timeProvider;

    public DashboardViewModel(
        IFleetGoApiClient apiClient,
        IAuthenticationService authenticationService,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(apiClient);
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _apiClient = apiClient;
        _authenticationService = authenticationService;
        _timeProvider = timeProvider;
    }

    /// <summary>Raised when the driver asks to see the full route list.</summary>
    public event EventHandler? ViewAllRoutesRequested;

    /// <summary>Raised when the driver picks one of today's routes.</summary>
    public event EventHandler<Guid>? RouteSelected;

    /// <summary>Today's routes, in route-number order.</summary>
    public ObservableCollection<RouteResponse> TodayRoutes { get; } = [];

    [ObservableProperty]
    public partial string DriverName { get; set; } = string.Empty;

    /// <summary>The driver's fleet code, or null for an account with no driver profile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDriverCode))]
    public partial string? DriverCode { get; set; }

    [ObservableProperty]
    public partial string TodayLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int PlannedCount { get; set; }

    [ObservableProperty]
    public partial int InProgressCount { get; set; }

    [ObservableProperty]
    public partial int CompletedCount { get; set; }

    /// <summary>Stops across all of today's routes - the number that actually tells a driver how big the day is.</summary>
    [ObservableProperty]
    public partial int StopsToday { get; set; }

    /// <summary>
    /// The route to start from: whichever is already in progress, otherwise the first one
    /// still planned. Null when today has neither.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveRoute))]
    public partial RouteResponse? ActiveRoute { get; set; }

    /// <summary>True once the load finished and there are genuinely no routes for today.</summary>
    [ObservableProperty]
    public partial bool HasNoRoutesToday { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    public bool HasDriverCode => !string.IsNullOrEmpty(DriverCode);

    public bool HasActiveRoute => ActiveRoute is not null;

    /// <summary>First load, and the retry action after a failure.</summary>
    [RelayCommand]
    private Task LoadAsync(CancellationToken cancellationToken) => LoadDashboardAsync(cancellationToken, showBusyIndicator: true);

    /// <summary>Pull-to-refresh: the same load without the full-page spinner.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;

        try
        {
            await LoadDashboardAsync(cancellationToken, showBusyIndicator: false);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private void ViewAllRoutes() => ViewAllRoutesRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SelectRoute(RouteResponse? route)
    {
        if (route is not null)
        {
            RouteSelected?.Invoke(this, route.Id);
        }
    }

    private async Task LoadDashboardAsync(CancellationToken cancellationToken, bool showBusyIndicator)
    {
        ApplyCurrentUser(_authenticationService.CurrentUser);

        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        TodayLabel = today.ToString("dddd d MMMM");

        bool succeeded = await RunAsync(
            async token =>
            {
                PagedResponse<RouteResponse> routes = await _apiClient.GetRoutesAsync(
                    page: 1,
                    pageSize: TodayRoutePageSize,
                    routeDate: today,
                    sort: "routeNumber",
                    cancellationToken: token);

                TodayRoutes.Clear();

                foreach (RouteResponse route in routes.Items)
                {
                    TodayRoutes.Add(route);
                }

                Summarise(routes.Items);
            },
            cancellationToken,
            showBusyIndicator);

        HasNoRoutesToday = succeeded && TodayRoutes.Count == 0;
    }

    private void ApplyCurrentUser(CurrentUserResponse? user)
    {
        // A dashboard that says "Welcome, " with nothing after it is worse than one that
        // just says "Driver" until the profile is there.
        DriverName = user is null ? "Driver" : $"{user.FirstName} {user.LastName}".Trim();
        DriverCode = user?.DriverCode;
    }

    private void Summarise(IReadOnlyList<RouteResponse> routes)
    {
        PlannedCount = routes.Count(route => route.Status == RouteStatus.Planned);
        InProgressCount = routes.Count(route => route.Status == RouteStatus.InProgress);
        CompletedCount = routes.Count(route => route.Status == RouteStatus.Completed);
        StopsToday = routes.Sum(route => route.StopCount);

        ActiveRoute =
            routes.FirstOrDefault(route => route.Status == RouteStatus.InProgress)
            ?? routes.FirstOrDefault(route => route.Status == RouteStatus.Planned);
    }
}
