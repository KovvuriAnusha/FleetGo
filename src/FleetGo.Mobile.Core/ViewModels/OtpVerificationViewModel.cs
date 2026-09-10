using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetGo.Mobile.Core.Otp;
using FleetGo.Mobile.Core.Session;

namespace FleetGo.Mobile.Core.ViewModels;

/// <summary>
/// Backs the "sign in with a code" flow: enter an email, request a code, enter the code,
/// verify. Two stages on one view model rather than two pages/view models - the transition
/// between them is a single boolean (<see cref="IsCodeStageVisible"/>), and splitting it up
/// would mean passing the email between two view models for no real benefit.
/// </summary>
public sealed partial class OtpVerificationViewModel : ObservableObject
{
    /// <summary>
    /// Client-side mirror of the server's <c>Otp:ResendCooldownSeconds</c> default. The
    /// server enforces the real limit regardless (see <c>OtpService</c>) - this only drives
    /// when the "Resend code" button re-enables itself, so a driver is not left staring at
    /// an active button that would just silently do nothing if pressed early.
    /// </summary>
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(30);

    private readonly IAuthenticationService _authenticationService;
    private readonly IOtpAutofillListener _autofillListener;
    private readonly TimeProvider _timeProvider;

    private DateTimeOffset _cooldownExpiresAtUtc = DateTimeOffset.MinValue;
    private CancellationTokenSource? _autofillCts;

    public OtpVerificationViewModel(
        IAuthenticationService authenticationService,
        IOtpAutofillListener autofillListener,
        TimeProvider timeProvider)
    {
        _authenticationService = authenticationService;
        _autofillListener = autofillListener;
        _timeProvider = timeProvider;
    }

    /// <summary>Raised once a code has been verified and a session established. The page handles navigation.</summary>
    public event EventHandler? VerificationSucceeded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand))]
    public partial string Email { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    public partial string Code { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(VerifyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResendCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>False until a code has been requested at least once - the code entry UI has nothing to show before that.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotCodeStageVisible))]
    public partial bool IsCodeStageVisible { get; set; }

    /// <summary>Convenience for XAML - the email-entry stage is visible exactly when the code stage is not.</summary>
    public bool IsNotCodeStageVisible => !IsCodeStageVisible;

    public bool IsNotBusy => !IsBusy;

    public bool HasErrorMessage => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Seconds until "Resend code" becomes available again. The page's own timer calls <see cref="RefreshResendAvailability"/> to keep this current.</summary>
    public int ResendCooldownSecondsRemaining =>
        Math.Max(0, (int)Math.Ceiling((_cooldownExpiresAtUtc - _timeProvider.GetUtcNow()).TotalSeconds));

    public bool CanResend => !IsBusy && ResendCooldownSecondsRemaining <= 0;

    /// <summary>Label for the resend button - shows the remaining cooldown once one is running, so the button never looks simply broken.</summary>
    public string ResendButtonText =>
        CanResend ? "Resend code" : $"Resend code ({ResendCooldownSecondsRemaining}s)";

    [RelayCommand(CanExecute = nameof(CanSendCode))]
    private Task SendCodeAsync(CancellationToken cancellationToken) => RequestCodeAsync(cancellationToken);

    private bool CanSendCode() => !IsBusy && !string.IsNullOrWhiteSpace(Email);

    [RelayCommand(CanExecute = nameof(CanResend))]
    private Task ResendAsync(CancellationToken cancellationToken) => RequestCodeAsync(cancellationToken);

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            _autofillCts?.Cancel();

            AuthResult result = await _authenticationService.VerifyOtpAsync(Email, Code, cancellationToken);

            if (result.Succeeded)
            {
                Code = string.Empty;
                VerificationSucceeded?.Invoke(this, EventArgs.Empty);
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
        catch (HttpRequestException)
        {
            ErrorMessage = "Could not reach the API. Is it running?";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanVerify() => !IsBusy && !string.IsNullOrWhiteSpace(Code);

    private async Task RequestCodeAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;

        try
        {
            await _authenticationService.RequestOtpAsync(Email, cancellationToken);

            IsCodeStageVisible = true;
            _cooldownExpiresAtUtc = _timeProvider.GetUtcNow() + ResendCooldown;
            BeginListeningForAutofill();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the user navigates away mid-call - not an error.
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Could not reach the API. Is it running?";
        }
        finally
        {
            IsBusy = false;
            RefreshResendAvailability();
        }
    }

    /// <summary>Called by the page on a short repeating timer so the cooldown display and "Resend" button catch up once time has passed.</summary>
    public void RefreshResendAvailability()
    {
        OnPropertyChanged(nameof(ResendCooldownSecondsRemaining));
        OnPropertyChanged(nameof(ResendButtonText));
        ResendCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Stops listening for an autofilled code - called when the page disappears.</summary>
    public void CancelAutofillListening() => _autofillCts?.Cancel();

    private void BeginListeningForAutofill()
    {
        _autofillCts?.Cancel();
        CancellationTokenSource cts = new();
        _autofillCts = cts;

        // Fire-and-forget by design: this races against the user typing the code manually,
        // and either one finishing means there is nothing further for this task to do -
        // there is no result for a caller to await.
        _ = ListenForAutofillAsync(cts.Token);
    }

    private async Task ListenForAutofillAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? code = await _autofillListener.ListenForCodeAsync(cancellationToken);

            if (!string.IsNullOrEmpty(code))
            {
                Code = code;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected: verification succeeded, or the page went away, before a code arrived.
        }
    }
}
