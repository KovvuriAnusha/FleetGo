using FleetGo.Mobile.Core.Session;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts.Auth;

namespace FleetGo.Mobile.Tests;

/// <summary>
/// Test double for <see cref="IAuthenticationService"/>, used to test <see cref="LoginViewModel"/>
/// and <see cref="OtpVerificationViewModel"/> in isolation. Login and OTP verification each
/// take their own configurable handler (defaulting to "succeed") so a test only has to
/// specify the behaviour it actually cares about.
/// </summary>
internal sealed class FakeAuthenticationService : IAuthenticationService
{
    private readonly Func<string, string, CancellationToken, Task<AuthResult>> _loginHandler;
    private readonly Func<string, string, CancellationToken, Task<AuthResult>> _verifyOtpHandler;
    private readonly Func<string, CancellationToken, Task> _requestOtpHandler;

    public FakeAuthenticationService(
        Func<string, string, CancellationToken, Task<AuthResult>>? loginHandler = null,
        Func<string, string, CancellationToken, Task<AuthResult>>? verifyOtpHandler = null,
        Func<string, CancellationToken, Task>? requestOtpHandler = null)
    {
        _loginHandler = loginHandler ?? ((_, _, _) => Task.FromResult(AuthResult.Success));
        _verifyOtpHandler = verifyOtpHandler ?? ((_, _, _) => Task.FromResult(AuthResult.Success));
        _requestOtpHandler = requestOtpHandler ?? ((_, _) => Task.CompletedTask);
    }

    public static FakeAuthenticationService ThatSucceeds() =>
        new(loginHandler: (_, _, _) => Task.FromResult(AuthResult.Success));

    public static FakeAuthenticationService ThatFails(string errorMessage) =>
        new(loginHandler: (_, _, _) => Task.FromResult(AuthResult.Failure(errorMessage)));

    public static FakeAuthenticationService ThatThrows(Exception exception) =>
        new(loginHandler: (_, _, _) => Task.FromException<AuthResult>(exception));

    public static FakeAuthenticationService ThatVerifiesOtpWith(Func<string, string, CancellationToken, Task<AuthResult>> handler) =>
        new(verifyOtpHandler: handler);

    public static FakeAuthenticationService ThatFailsOtpVerification(string errorMessage) =>
        new(verifyOtpHandler: (_, _, _) => Task.FromResult(AuthResult.Failure(errorMessage)));

    public static FakeAuthenticationService ThatThrowsOnOtpRequest(Exception exception) =>
        new(requestOtpHandler: (_, _) => Task.FromException(exception));

    /// <summary>The (email, password) pair passed to the most recent <see cref="LoginAsync"/> call, if any.</summary>
    public (string Email, string Password)? LastLoginAttempt { get; private set; }

    /// <summary>The (email, code) pair passed to the most recent <see cref="VerifyOtpAsync"/> call, if any.</summary>
    public (string Email, string Code)? LastOtpVerifyAttempt { get; private set; }

    /// <summary>The email passed to the most recent <see cref="RequestOtpAsync"/> call, if any.</summary>
    public string? LastOtpRequestEmail { get; private set; }

    public int RequestOtpCallCount { get; private set; }

    /// <summary>Backing value for <see cref="HasStoredSessionAsync"/> - set directly by a test rather than driven by real session state.</summary>
    public bool HasStoredSession { get; set; }

    public event EventHandler? AuthenticationStateChanged;

    public bool IsAuthenticated { get; private set; }

    public CurrentUserResponse? CurrentUser => null;

    public Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        LastLoginAttempt = (email, password);
        return _loginHandler(email, password, cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        IsAuthenticated = false;
        AuthenticationStateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> HasStoredSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(HasStoredSession);

    public Task RequestOtpAsync(string email, CancellationToken cancellationToken = default)
    {
        LastOtpRequestEmail = email;
        RequestOtpCallCount++;
        return _requestOtpHandler(email, cancellationToken);
    }

    public Task<AuthResult> VerifyOtpAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        LastOtpVerifyAttempt = (email, code);
        return _verifyOtpHandler(email, code, cancellationToken);
    }
}
