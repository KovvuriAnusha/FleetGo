using FleetGo.Mobile.Core.Session;
using FleetGo.Mobile.Core.ViewModels;

namespace FleetGo.Mobile.Tests;

public sealed class OtpVerificationViewModelTests
{
    [Fact]
    public async Task SendCode_Succeeds_ShowsTheCodeStageAndStartsTheCooldown()
    {
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        OtpVerificationViewModel viewModel = new(authService, new FakeOtpAutofillListener(null), TimeProvider.System)
        {
            Email = "driver@example.com",
        };

        await viewModel.SendCodeCommand.ExecuteAsync(null);

        Assert.Equal("driver@example.com", authService.LastOtpRequestEmail);
        Assert.True(viewModel.IsCodeStageVisible);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.CanResend); // cooldown just started
    }

    [Fact]
    public async Task SendCode_TranslatesAnUnreachableApi_IntoAFriendlyMessage()
    {
        FakeAuthenticationService authService = FakeAuthenticationService.ThatThrowsOnOtpRequest(new HttpRequestException("boom"));
        OtpVerificationViewModel viewModel = new(authService, new FakeOtpAutofillListener(null), TimeProvider.System)
        {
            Email = "driver@example.com",
        };

        await viewModel.SendCodeCommand.ExecuteAsync(null);

        Assert.Equal("Could not reach the API. Is it running?", viewModel.ErrorMessage);
        Assert.False(viewModel.IsCodeStageVisible);
    }

    [Fact]
    public void SendCodeCommand_CannotExecute_WhenEmailIsBlank()
    {
        OtpVerificationViewModel viewModel = new(
            FakeAuthenticationService.ThatSucceeds(), new FakeOtpAutofillListener(null), TimeProvider.System)
        {
            Email = "   ",
        };

        Assert.False(viewModel.SendCodeCommand.CanExecute(null));
    }

    [Fact]
    public async Task Verify_Succeeds_RaisesVerificationSucceededAndClearsTheCode()
    {
        FakeAuthenticationService authService = FakeAuthenticationService.ThatVerifiesOtpWith(
            (_, _, _) => Task.FromResult(AuthResult.Success));
        OtpVerificationViewModel viewModel = new(authService, new FakeOtpAutofillListener(null), TimeProvider.System)
        {
            Email = "driver@example.com",
            Code = "123456",
        };

        bool raised = false;
        viewModel.VerificationSucceeded += (_, _) => raised = true;

        await viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Equal(string.Empty, viewModel.Code);
        Assert.Equal(("driver@example.com", "123456"), authService.LastOtpVerifyAttempt);
    }

    [Fact]
    public async Task Verify_Fails_SetsErrorMessageAndDoesNotRaiseVerificationSucceeded()
    {
        const string expectedMessage = "The code is incorrect, expired, or has already been used.";
        FakeAuthenticationService authService = FakeAuthenticationService.ThatFailsOtpVerification(expectedMessage);
        OtpVerificationViewModel viewModel = new(authService, new FakeOtpAutofillListener(null), TimeProvider.System)
        {
            Email = "driver@example.com",
            Code = "000000",
        };

        bool raised = false;
        viewModel.VerificationSucceeded += (_, _) => raised = true;

        await viewModel.VerifyCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Equal(expectedMessage, viewModel.ErrorMessage);
        Assert.True(viewModel.HasErrorMessage);
        // Unlike a password, a rejected code is left in the field - retyping six digits
        // after a typo is friction a driver should not have to repeat.
        Assert.Equal("000000", viewModel.Code);
    }

    [Fact]
    public void VerifyCommand_CannotExecute_WhenCodeIsBlank()
    {
        OtpVerificationViewModel viewModel = new(
            FakeAuthenticationService.ThatSucceeds(), new FakeOtpAutofillListener(null), TimeProvider.System)
        {
            Email = "driver@example.com",
            Code = "",
        };

        Assert.False(viewModel.VerifyCommand.CanExecute(null));
    }

    [Fact]
    public async Task ResendCommand_CannotExecute_WhileTheCooldownIsActive()
    {
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        OtpVerificationViewModel viewModel = new(authService, new FakeOtpAutofillListener(null), TimeProvider.System)
        {
            Email = "driver@example.com",
        };

        await viewModel.SendCodeCommand.ExecuteAsync(null);

        Assert.False(viewModel.CanResend);
        Assert.True(viewModel.ResendCooldownSecondsRemaining > 0);
        Assert.False(viewModel.ResendCommand.CanExecute(null));
    }

    [Fact]
    public async Task ResendCommand_CanExecute_OnceTheCooldownHasElapsed()
    {
        FakeTimeProvider timeProvider = new(DateTimeOffset.UtcNow);
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        OtpVerificationViewModel viewModel = new(authService, new FakeOtpAutofillListener(null), timeProvider)
        {
            Email = "driver@example.com",
        };

        await viewModel.SendCodeCommand.ExecuteAsync(null);
        Assert.False(viewModel.CanResend);

        // Jump the clock forward past the client-side cooldown and let the view model
        // re-check - exactly what the page's periodic timer does in real use.
        timeProvider.Advance(TimeSpan.FromSeconds(31));
        viewModel.RefreshResendAvailability();

        Assert.Equal(0, viewModel.ResendCooldownSecondsRemaining);
        Assert.True(viewModel.CanResend);
        Assert.True(viewModel.ResendCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnAutofilledCode_PopulatesTheCodeField()
    {
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        OtpVerificationViewModel viewModel = new(authService, new FakeOtpAutofillListener("482913"), TimeProvider.System)
        {
            Email = "driver@example.com",
        };

        await viewModel.SendCodeCommand.ExecuteAsync(null);

        // The autofill listener resolves synchronously in this fake, but the view model
        // starts listening as fire-and-forget - give the scheduler one turn to complete it.
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal("482913", viewModel.Code);
    }
}
