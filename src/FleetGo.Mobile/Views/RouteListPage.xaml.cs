using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Views;

/// <summary>
/// The driver's route list. Code-behind covers the three things the view model cannot: Shell
/// navigation, turning a CollectionView selection into a command, and re-running the query
/// when the status filter changes (a Picker has no command to bind).
/// </summary>
public partial class RouteListPage : ContentPage
{
    /// <summary>Shell route this page is registered under - see AppShell.xaml.cs.</summary>
    public const string RouteName = "routes";

    private readonly RouteListViewModel _viewModel;

    public RouteListPage(RouteListViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.RouteSelected += OnRouteSelectedById;
        _viewModel.SessionExpired += OnSessionExpired;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Only load the first time: coming back from a route detail should keep the list and
        // the scroll position the driver left, not reset to page 1.
        if (!_viewModel.HasLoaded && _viewModel.LoadCommand.CanExecute(null))
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

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }
    }

    private void OnStatusFilterChanged(object? sender, EventArgs e)
    {
        // The Picker has already written the new value onto SelectedStatusFilter through its
        // binding; this just re-runs the query with it.
        if (_viewModel.RefreshCommand.CanExecute(null))
        {
            _viewModel.RefreshCommand.Execute(null);
        }
    }

    private async void OnRouteSelectedById(object? sender, Guid routeId) =>
        await Shell.Current.GoToAsync($"{RouteDetailPage.RouteName}?{RouteDetailPage.RouteIdQueryKey}={routeId}");

    private async void OnSessionExpired(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//login");
}
