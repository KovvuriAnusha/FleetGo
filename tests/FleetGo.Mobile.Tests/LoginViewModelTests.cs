using FleetGo.Mobile.Core.ViewModels;

namespace FleetGo.Mobile.Tests;

public sealed class LoginViewModelTests
{
    [Fact]
    public async Task Login_Succeeds_ClearsPasswordAndRaisesLoginSucceeded()
    {
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        LoginViewModel viewModel = new(authService)
        {
            Email = "driver@example.com",
            Password = "correct-password",
        };

        bool raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(("driver@example.com", "correct-password"), authService.LastLoginAttempt);
    }

    [Fact]
    public async Task Login_Fails_SetsErrorMessageAndDoesNotRaiseLoginSucceeded()
    {
        const string expectedMessage = "The email or password is incorrect, or the account is not active.";
        FakeAuthenticationService authService = FakeAuthenticationService.ThatFails(expectedMessage);
        LoginViewModel viewModel = new(authService)
        {
            Email = "driver@example.com",
            Password = "wrong-password",
        };

        bool raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Equal(expectedMessage, viewModel.ErrorMessage);
        Assert.True(viewModel.HasErrorMessage);
        // The password is left as-is on failure - clearing it after a typo would be hostile.
        Assert.Equal("wrong-password", viewModel.Password);
    }

    [Fact]
    public async Task Login_RejectsAnObviouslyInvalidEmail_WithoutCallingTheAuthenticationService()
    {
        FakeAuthenticationService authService = FakeAuthenticationService.ThatSucceeds();
        LoginViewModel viewModel = new(authService)
        {
            Email = "not-an-email",
            Password = "whatever",
        };

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Null(authService.LastLoginAttempt);
        Assert.False(string.IsNullOrEmpty(viewModel.ErrorMessage));
    }

    [Fact]
    public async Task Login_TranslatesAnUnreachableApi_IntoAFriendlyMessage()
    {
        FakeAuthenticationService authService = FakeAuthenticationService.ThatThrows(new HttpRequestException("boom"));
        LoginViewModel viewModel = new(authService)
        {
            Email = "driver@example.com",
            Password = "correct-password",
        };

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Equal("Could not reach the API. Is it running?", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("driver@example.com", "")]
    [InlineData(" ", " ")]
    public void LoginCommand_CannotExecute_WhenEmailOrPasswordIsBlank(string email, string password)
    {
        LoginViewModel viewModel = new(FakeAuthenticationService.ThatSucceeds())
        {
            Email = email,
            Password = password,
        };

        Assert.False(viewModel.LoginCommand.CanExecute(null));
    }
}

