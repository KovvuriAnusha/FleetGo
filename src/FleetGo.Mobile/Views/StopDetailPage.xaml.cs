using FleetGo.Mobile.Core.ViewModels;

namespace FleetGo.Mobile.Views;

/// <summary>
/// One stop: its customer, its status and its packages. The stop id arrives as a Shell query
/// parameter, exactly as the route id does on <see cref="RouteDetailPage"/>.
/// </summary>
[QueryProperty(nameof(StopId), StopIdQueryKey)]
public partial class StopDetailPage : ContentPage
{
    /// <summary>Shell route this page is registered under - see AppShell.xaml.cs.</summary>
    public const string RouteName = "stop-detail";

    /// <summary>Query-string key carrying the stop id.</summary>
    public const string StopIdQueryKey = "stopId";

    private readonly StopDetailViewModel _viewModel;
    private Guid _stopId;

    public StopDetailPage(StopDetailViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.SessionExpired += OnSessionExpired;
    }

    /// <summary>Set by Shell from the query string. A value that is not a Guid leaves the id empty, which the API answers with a 404 the view model already handles.</summary>
    public string StopId
    {
        set => _stopId = Guid.TryParse(Uri.UnescapeDataString(value ?? string.Empty), out Guid parsed)
            ? parsed
            : Guid.Empty;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadAsync(_stopId);
    }

    private async void OnSessionExpired(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//login");
}
