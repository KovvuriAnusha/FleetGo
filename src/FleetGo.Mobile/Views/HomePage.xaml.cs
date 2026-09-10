using FleetGo.Mobile.ViewModels;

namespace FleetGo.Mobile.Views;

/// <summary>
/// Landing page. The code-behind stays deliberately empty apart from wiring the
/// view model supplied by DI - all behaviour lives in <see cref="HomeViewModel"/>.
/// </summary>
public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Check on arrival so the first thing a developer sees is whether the
        // backend is up, rather than an empty screen.
        if (_viewModel.CheckApiCommand.CanExecute(null))
        {
            _viewModel.CheckApiCommand.Execute(null);
        }
    }
}
