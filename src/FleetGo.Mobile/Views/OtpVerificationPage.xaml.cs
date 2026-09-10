using FleetGo.Mobile.Core.ViewModels;

namespace FleetGo.Mobile.Views;

/// <summary>
/// Code-behind stays limited to what the view model cannot do itself: navigating on
/// success, and the once-a-second UI timer that lets the resend cooldown display and
/// button catch up (see <see cref="OtpVerificationViewModel.RefreshResendAvailability"/> -
/// a plain countdown like this has no natural "something changed" event to hang off).
/// </summary>
public partial class OtpVerificationPage : ContentPage
{
    private readonly OtpVerificationViewModel _viewModel;
    private IDispatcherTimer? _cooldownTimer;

    public OtpVerificationPage(OtpVerificationViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.VerificationSucceeded += OnVerificationSucceeded;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _cooldownTimer = Dispatcher.CreateTimer();
        _cooldownTimer.Interval = TimeSpan.FromSeconds(1);
        _cooldownTimer.Tick += (_, _) => _viewModel.RefreshResendAvailability();
        _cooldownTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _cooldownTimer?.Stop();
        _cooldownTimer = null;

        _viewModel.CancelAutofillListening();
    }

    private async void OnVerificationSucceeded(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//home");
}
