using FleetGo.Mobile.Core.Session;
using FleetGo.Mobile.Core.ViewModels;
using FleetGo.Shared.Contracts.Auth;

namespace FleetGo.Mobile.Tests;

/// <summary>Test double for <see cref="IAuthenticationService"/>, used to test <see cref="LoginViewModel"/> in isolation.</summary>
internal sealed class FakeAuthenticationService : IAuthenticationService
{
    private readonly Func<string, string, CancellationToken, Task<AuthResult>> _loginHandler;

    public FakeAuthenticationService(Func<string, string, CancellationToken, Task<AuthResult>> loginHandler)
    {
        _loginHandler = loginHandler;
    }

    public static FakeAuthenticationService ThatSucceeds() =>
        new((_, _, _) => Task.FromResult(AuthResult.Success));

    public static FakeAuthenticationService ThatFails(string errorMessage) =>
        new((_, _, _) => Task.FromResult(AuthResult.Failure(errorMessage)));

    public static FakeAuthenticationService ThatThrows(Exception exception) =>
        new((_, _, _) => Task.FromException<AuthResult>(exception));

    /// <summary>The (email, password) pair passed to the most recent <see cref="LoginAsync"/> call, if any.</summary>
    public (string Email, string Password)? LastLoginAttempt { get; private set; }

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
}

