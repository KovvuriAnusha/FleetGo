using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Views;

/// <summary>
/// Driver dashboard. Code-behind stays limited to the Shell navigation the view model
/// deliberately knows nothing about, plus turning a CollectionView selection into the view
/// model's command - the same division of labour as LoginPage and HomePage.
/// </summary>
public partial class DashboardPage : ContentPage
{
    /// <summary>Shell route this page is registered under - see AppShell.xaml.cs.</summary>
    public const string RouteName = "dashboard";

    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.SessionExpired += OnSessionExpired;
        _viewModel.ViewAllRoutesRequested += OnViewAllRoutesRequested;
        _viewModel.RouteSelected += OnRouteSelectedById;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Reload on every appearance, including coming back from route detail, so a route
        // whose status changed while the driver was away is not shown stale here.
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }

    private void OnRouteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is RouteResponse route)
        {
            _viewModel.SelectRouteCommand.Execute(route);
        }

        // Clear the selection so returning to this page does not leave a row highlighted.
        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }
    }

    private void OnOpenActiveRoute(object? sender, EventArgs e) =>
        _viewModel.SelectRouteCommand.Execute(_viewModel.ActiveRoute);

    private async void OnRouteSelectedById(object? sender, Guid routeId) =>
        await Shell.Current.GoToAsync($"{RouteDetailPage.RouteName}?{RouteDetailPage.RouteIdQueryKey}={routeId}");

    private async void OnViewAllRoutesRequested(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(RouteListPage.RouteName);

    private async void OnSessionExpired(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//login");
}
