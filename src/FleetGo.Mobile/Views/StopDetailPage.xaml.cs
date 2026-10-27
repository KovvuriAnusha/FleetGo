using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts.Fleet;

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

    // The four status actions. Each one hands the matching StopStatusAction to the view
    // model's command; the view model owns what actually happens, which is why these are one
    // line each and there is no logic here to get wrong.
    private void OnMarkArrived(object? sender, EventArgs e) => SetStatus(StopStatus.Arrived);

    private void OnMarkDelivered(object? sender, EventArgs e) => SetStatus(StopStatus.Completed);

    private void OnMarkFailed(object? sender, EventArgs e) => SetStatus(StopStatus.Failed);

    private void OnMarkSkipped(object? sender, EventArgs e) => SetStatus(StopStatus.Skipped);

    private void SetStatus(StopStatus status)
    {
        StopStatusAction? action = _viewModel.StatusActions.FirstOrDefault(candidate => candidate.Value == status);

        if (action is not null && _viewModel.SetStatusCommand.CanExecute(action))
        {
            _viewModel.SetStatusCommand.Execute(action);
        }
    }
}
