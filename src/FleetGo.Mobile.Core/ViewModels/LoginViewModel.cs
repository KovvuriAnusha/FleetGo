using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Mobile.Core.Session;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// Backs the login page. Kept intentionally simple for Phase 2: one form, one command,
/// one error message - no "remember me", no social sign-in, no password reset yet.
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    // Deliberately permissive (checks for "something@something.something") rather than a
    // strict RFC 5322 pattern: the API is the actual source of truth on whether an email
    // is valid and known. This only needs to catch obvious typos before spending a round
    // trip on them.
    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IAuthenticationService _authenticationService;

    public LoginViewModel(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    /// <summary>Raised after a successful sign-in. The page handles navigation - this view model knows nothing about Shell.</summary>
    public event EventHandler? LoginSucceeded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    public partial string? ErrorMessage { get; set; }

    public bool IsNotBusy => !IsBusy;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;

        if (!EmailPattern.IsMatch(Email))
        {
            ErrorMessage = "Enter a valid email address.";
            return;
        }

        IsBusy = true;

        try
        {
            AuthResult result = await _authenticationService.LoginAsync(Email, Password, cancellationToken);

            if (result.Succeeded)
            {
                Password = string.Empty;
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = result.ErrorMessage;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the user navigates away mid-call - not an error.
        }
        catch (TaskCanceledException)
        {
            // HttpClient reports its own timeout as a cancellation.
            ErrorMessage = "The sign-in request timed out. Check your connection and try again.";
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Could not reach the API. Is it running?";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLogin() =>
        !IsBusy && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

