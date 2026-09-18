using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts.Fleet;

namespace FleetGo.Mobile.Views;

/// <summary>
/// One route and its stops. The route id arrives as a Shell query parameter rather than a
/// constructor argument, because Shell resolves the page from DI and then applies the query -
/// so the id is handed to the view model in <see cref="OnAppearing"/>, which also keeps the
/// view model free of any Shell attribute of its own.
/// </summary>
[QueryProperty(nameof(RouteId), RouteIdQueryKey)]
public partial class RouteDetailPage : ContentPage
{
    /// <summary>Shell route this page is registered under - see AppShell.xaml.cs.</summary>
    public const string RouteName = "route-detail";

    /// <summary>Query-string key carrying the route id.</summary>
    public const string RouteIdQueryKey = "routeId";

    private readonly RouteDetailViewModel _viewModel;
    private Guid _routeId;

    public RouteDetailPage(RouteDetailViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.StopSelected += OnStopSelectedById;
        _viewModel.SessionExpired += OnSessionExpired;
    }

    /// <summary>Set by Shell from the query string. A value that is not a Guid leaves the id empty, which the API answers with a 404 the view model already handles.</summary>
    public string RouteId
    {
        set => _routeId = Guid.TryParse(Uri.UnescapeDataString(value ?? string.Empty), out Guid parsed)
            ? parsed
            : Guid.Empty;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Reload on every appearance: coming back from a stop whose status changed should not
        // leave this route's stop list showing the old one.
        await _viewModel.LoadAsync(_routeId);
    }

    private void OnStopSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is StopResponse stop)
        {
            _viewModel.SelectStopCommand.Execute(stop);
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }
    }

    private async void OnStopSelectedById(object? sender, Guid stopId) =>
        await Shell.Current.GoToAsync($"{StopDetailPage.RouteName}?{StopDetailPage.StopIdQueryKey}={stopId}");

    private async void OnSessionExpired(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//login");
}
