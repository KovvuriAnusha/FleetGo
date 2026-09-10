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

        _viewModel.SignedOut += OnSignedOut;
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

    private async void OnSignedOut(object? sender, EventArgs e) => await Shell.Current.GoToAsync("//login");

    /// <summary>
    /// The Switch's IsToggled is bound one-way from <see cref="HomeViewModel.IsBiometricUnlockEnabled"/>,
    /// so this handler fires both when a driver flips the switch AND when the view model updates the
    /// property itself (initial load, sign-out, or a request that ended up not taking effect). Only the
    /// former should trigger a new enable/disable attempt - reacting to the latter as well would mean
    /// every programmatic update re-runs the command and, for "enable", prompts an unnecessary biometric
    /// challenge. The two cases are told apart by comparing the switch's new value against the view
    /// model's current value: they already match when this event was caused by the view model (the
    /// binding just copied that value onto the switch), and they differ when a driver's tap changed the
    /// switch ahead of the view model.
    /// </summary>
    private async void OnBiometricSwitchToggled(object? sender, ToggledEventArgs e)
    {
        if (e.Value == _viewModel.IsBiometricUnlockEnabled)
        {
            return;
        }

        await _viewModel.ToggleBiometricUnlockCommand.ExecuteAsync(e.Value);
    }
}

