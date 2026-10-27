using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Mobile.Core.Offline;
using FleetGo.Mobile.Core.Offline.Sync;
using FleetGo.Mobile.Core.Session;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// Backs the driver dashboard: who is signed in, and what today looks like.
/// <para>
/// There is no dashboard aggregation endpoint in Phase 4A and this does not pretend
/// otherwise - the summary is composed client-side from today's routes, and the driver's
/// identity comes from the session that is already loaded.
/// </para>
/// <para>
/// Since Phase 5 those routes come out of the device's own database rather than off the wire
/// (see <see cref="IFleetRepository"/>), so the dashboard opens instantly, works in a
/// basement, and says plainly - through the shared banner - how current what it is showing
/// actually is. Pull-to-refresh asks for a sync; it does not fetch a page itself.
/// </para>
/// </summary>
public sealed partial class DashboardViewModel : FleetPageViewModel
{
    private readonly IFleetRepository _repository;
    private readonly IAuthenticationService _authenticationService;
    private readonly ISyncTrigger _syncTrigger;
    private readonly TimeProvider _timeProvider;

    public DashboardViewModel(
        IFleetRepository repository,
        IAuthenticationService authenticationService,
        ISyncTrigger syncTrigger,
        SyncStatusViewModel sync,
        TimeProvider timeProvider)
        : base(sync)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(syncTrigger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _repository = repository;
        _authenticationService = authenticationService;
        _syncTrigger = syncTrigger;
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

    /// <summary>True once the load finished and there are genuinely no routes cached for today.</summary>
    [ObservableProperty]
    public partial bool HasNoRoutesToday { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    public bool HasDriverCode => !string.IsNullOrEmpty(DriverCode);

    public bool HasActiveRoute => ActiveRoute is not null;

    /// <summary>First load, and the retry action after a failure.</summary>
    [RelayCommand]
    private Task LoadAsync(CancellationToken cancellationToken) => LoadDashboardAsync(cancellationToken, showBusyIndicator: true);

    /// <summary>
    /// Pull-to-refresh. Asks the sync engine for a run and then re-reads the device - so a
    /// refresh with no signal still repaints from the cache instead of failing, and a refresh
    /// with signal also gets whatever was queued out of the door.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsRefreshing = true;

        try
        {
            await _syncTrigger.SyncNowAsync(SyncReason.Manual, cancellationToken);
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
                CachedData<IReadOnlyList<RouteResponse>> cached = await _repository.GetRoutesForDateAsync(today, token);

                TodayRoutes.Clear();

                foreach (RouteResponse route in cached.Value)
                {
                    TodayRoutes.Add(route);
                }

                HasPendingLocalChange = cached.HasPendingLocalChange;
                Summarise(cached.Value);
            },
            cancellationToken,
            showBusyIndicator);

        HasNoRoutesToday = succeeded && TodayRoutes.Count == 0;
    }

    private void ApplyCurrentUser(CurrentUserResponse? user)
    {
        // A dashboard that says "Welcome, " with nothing after it is worse than one that
        // just says "Driver" until the profile is there. This also covers resuming offline,
        // where the session restored but the profile call could not run.
        DriverName = user is null ? "Driver" : $"{user.FirstName} {user.LastName}".Trim();
        DriverCode = user?.DriverCode;
    }

    private void Summarise(IReadOnlyList<RouteResponse> routes)
    {
        PlannedCount = routes.Count(route => route.Status == RouteStatus.Planned);
        InProgressCount = routes.Count(route => route.Status == RouteStatus.InProgress);
        CompletedCount = routes.Count(route => route.Status == RouteStatus.Completed);
        // Cancelled routes are excluded: their stops are not work the driver still has to do,
        // and counting them made "12 stops" disagree with the Planned/Active/Done tiles above
        // it, which never counted cancelled routes either.
        StopsToday = routes
            .Where(route => route.Status != RouteStatus.Cancelled)
            .Sum(route => route.StopCount);

        ActiveRoute =
            routes.FirstOrDefault(route => route.Status == RouteStatus.InProgress)
            ?? routes.FirstOrDefault(route => route.Status == RouteStatus.Planned);
    }
}
