using FleetGo.Mobile.Core.Session;
using FleetGo.Mobile.Core.ViewModels;

namespace FleetGo.Mobile.Views;

/// <summary>
/// Login page. Code-behind stays limited to what the view model cannot do itself:
/// subscribing to <see cref="LoginViewModel.LoginSucceeded"/> and navigating, and checking
/// for a restorable session on arrival - both are Shell/navigation concerns the view model
/// deliberately knows nothing about (see <c>FleetGo.Mobile.Core</c>'s project comments).
/// </summary>
public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;
    private readonly IAuthenticationService _authenticationService;

    public LoginPage(LoginViewModel viewModel, IAuthenticationService authenticationService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _authenticationService = authenticationService;
        BindingContext = viewModel;

        _viewModel.LoginSucceeded += OnLoginSucceeded;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Session restore on launch: if a stored refresh token is still good, skip
        // straight past the login form instead of making a signed-in driver sign in again
        // every time the app is closed and reopened.
        if (await _authenticationService.TryRestoreSessionAsync())
        {
            await GoToHomeAsync();
        }
    }

    private async void OnLoginSucceeded(object? sender, EventArgs e) => await GoToHomeAsync();

    private static Task GoToHomeAsync() => Shell.Current.GoToAsync("//home");
}

