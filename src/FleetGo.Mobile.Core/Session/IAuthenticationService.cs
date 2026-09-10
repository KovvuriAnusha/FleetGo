using FleetGo.Shared.Contracts.Auth;

namespace FleetGo.Mobile.Core.Session;

/// <summary>
/// The mobile app's session: signing in, signing out, restoring a session found in
/// secure storage on launch, and supplying the current access token to the HTTP pipeline
/// (see <c>FleetGo.Shared.Http.IAccessTokenProvider</c>, which this also implements).
/// </summary>
public interface IAuthenticationService
{
    /// <summary>Raised whenever <see cref="IsAuthenticated"/> changes - a page can use this to react without polling.</summary>
    event EventHandler? AuthenticationStateChanged;

    /// <summary>Whether a session is currently signed in (a token pair is stored, whether or not it happens to be expired right now).</summary>
    bool IsAuthenticated { get; }

    /// <summary>The signed-in user's profile, once <see cref="LoginAsync"/> or <see cref="TryRestoreSessionAsync"/> has succeeded.</summary>
    CurrentUserResponse? CurrentUser { get; }

    /// <summary>
    /// Validates credentials against the API and, on success, stores the returned tokens
    /// and loads the current user's profile.
    /// </summary>
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes the stored refresh token with the API (best-effort - a failed revocation
    /// call, e.g. because the device is offline, does not stop the local session from
    /// ending) and clears local storage.
    /// </summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called once at app startup. Looks for a stored session and, if the refresh token on
    /// it is still valid, refreshes it and loads the current user's profile - the app can
    /// then skip straight past the login page. Returns <see langword="false"/> (and clears
    /// storage) when there is nothing to restore or the stored refresh token no longer works.
    /// </summary>
    Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default);
}

