using FleetGo.Mobile.Core.Biometrics;
using FleetGo.Mobile.Core.Session;
using FleetGo.Mobile.Core.ViewModels;

namespace FleetGo.Mobile.Views;

/// <summary>
/// Login page. Code-behind stays limited to what the view model cannot do itself:
/// subscribing to <see cref="LoginViewModel.LoginSucceeded"/> and navigating, and checking
/// for a restorable session (now via biometrics first, then a plain token restore) on
/// arrival - both are Shell/navigation concerns the view model deliberately knows nothing
/// about (see <c>FleetGo.Mobile.Core</c>'s project comments).
/// </summary>
public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;
    private readonly IAuthenticationService _authenticationService;
    private readonly IBiometricUnlockCoordinator _biometricUnlockCoordinator;

    public LoginPage(
        LoginViewModel viewModel,
        IAuthenticationService authenticationService,
        IBiometricUnlockCoordinator biometricUnlockCoordinator)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _authenticationService = authenticationService;
        _biometricUnlockCoordinator = biometricUnlockCoordinator;
        BindingContext = viewModel;

        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        BiometricUnlockOutcome biometricOutcome = await _biometricUnlockCoordinator.TryUnlockAsync();

        if (biometricOutcome == BiometricUnlockOutcome.Unlocked)
        {
            await GoToHomeAsync();
            return;
        }

        // NotEnabled/NoStoredSession mean biometrics were never meant to gate this launch -
        // fall through to the same plain session-restore check as before biometrics existed.
        // NotAvailable/Cancelled/Failed, on the other hand, mean biometrics WERE supposed to
        // gate this session but did not succeed - staying on the login form here (rather
        // than restoring anyway) is what makes opting into biometric unlock actually mean
        // something, instead of a toggle that a cancelled prompt could always bypass.
        if (biometricOutcome is BiometricUnlockOutcome.NotEnabled or BiometricUnlockOutcome.NoStoredSession)
        {
            if (await _authenticationService.TryRestoreSessionAsync())
            {
                await GoToHomeAsync();
            }
        }
    }

    private async void OnLoginSucceeded(object? sender, EventArgs e) => await GoToHomeAsync();

    private async void OnUseOtpInstead(object? sender, EventArgs e) => await Shell.Current.GoToAsync("otp-verify");

    private static Task GoToHomeAsync() => Shell.Current.GoToAsync("//home");
}
