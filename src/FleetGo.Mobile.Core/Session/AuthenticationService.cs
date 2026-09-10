using System.Net;
using FleetGo.Shared.Contracts.Auth;
using FleetGo.Shared.Http;

namespace FleetGo.Mobile.Core.Session;

/// <summary>
/// Default <see cref="IAuthenticationService"/>. Also implements
/// <see cref="IAccessTokenProvider"/>: this is the one object that knows both "what is the
/// current access token" and "how to get a new one", so it is registered as both
/// interfaces from the same instance (see <c>MauiProgram</c>) rather than splitting that
/// knowledge across two classes.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService, IAccessTokenProvider
{
    /// <summary>
    /// Treat the access token as due for renewal this far before it actually expires, so a
    /// request that is about to be sent does not race a token that expires mid-flight.
    /// </summary>
    private static readonly TimeSpan RenewalBuffer = TimeSpan.FromSeconds(30);

    private readonly IFleetGoApiClient _apiClient;
    private readonly ISecureTokenStore _tokenStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private StoredTokens? _tokens;

    public AuthenticationService(IFleetGoApiClient apiClient, ISecureTokenStore tokenStore, TimeProvider timeProvider)
    {
        _apiClient = apiClient;
        _tokenStore = tokenStore;
        _timeProvider = timeProvider;
    }

    public event EventHandler? AuthenticationStateChanged;

    public bool IsAuthenticated => _tokens is not null;

    public CurrentUserResponse? CurrentUser { get; private set; }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        TokenResponse tokens;
        try
        {
            tokens = await _apiClient.LoginAsync(new LoginRequest(email, password), cancellationToken);
        }
        catch (FleetGoApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return AuthResult.Failure("The email or password is incorrect, or the account is not active.");
        }

        await StoreAndLoadProfileAsync(tokens, cancellationToken);
        return AuthResult.Success;
    }

    public async Task<bool> HasStoredSessionAsync(CancellationToken cancellationToken = default) =>
        _tokens is not null || await _tokenStore.LoadAsync(cancellationToken) is not null;

    public Task RequestOtpAsync(string email, CancellationToken cancellationToken = default) =>
        _apiClient.RequestOtpAsync(new RequestOtpRequest(email), cancellationToken);

    public async Task<AuthResult> VerifyOtpAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        TokenResponse tokens;
        try
        {
            tokens = await _apiClient.VerifyOtpAsync(new VerifyOtpRequest(email, code), cancellationToken);
        }
        catch (FleetGoApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            return AuthResult.Failure("The code is incorrect, expired, or has already been used.");
        }

        // Same success path a password login takes - one session, however it was reached.
        await StoreAndLoadProfileAsync(tokens, cancellationToken);
        return AuthResult.Success;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        StoredTokens? current = _tokens;

        if (current is not null)
        {
            try
            {
                await _apiClient.LogoutAsync(new LogoutRequest(current.RefreshToken), cancellationToken);
            }
            catch (Exception ex) when (ex is FleetGoApiException or HttpRequestException)
            {
                // Best-effort: the session still ends locally even if the API could not be
                // reached to revoke it server-side. The refresh token will simply expire.
            }
        }

        await ClearSessionAsync(cancellationToken);
    }

    public async Task<bool> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        StoredTokens? stored = await _tokenStore.LoadAsync(cancellationToken);

        if (stored is null)
        {
            return false;
        }

        _tokens = stored;

        if (stored.RefreshTokenExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            await ClearSessionAsync(cancellationToken);
            return false;
        }

        // Always refresh on restore, rather than trusting a possibly-stale access token as
        // is: this also rotates the refresh token immediately, so an app re-opened after a
        // long idle period does not resume on a refresh token close to its own expiry.
        bool refreshed;
        try
        {
            refreshed = await RefreshInternalAsync(stored.RefreshToken, cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Offline at launch: keep the stored session rather than forcing a sign-in the
            // user cannot complete anyway without connectivity. The next authenticated
            // call will retry the refresh through GetAccessTokenAsync.
            RaiseAuthenticationStateChanged();
            return true;
        }

        if (!refreshed)
        {
            return false;
        }

        await TryLoadCurrentUserAsync(cancellationToken);
        RaiseAuthenticationStateChanged();
        return true;
    }

    /// <inheritdoc cref="IAccessTokenProvider.GetAccessTokenAsync" />
    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        StoredTokens? current = _tokens;

        if (current is null)
        {
            return null;
        }

        if (current.AccessTokenExpiresAtUtc > _timeProvider.GetUtcNow() + RenewalBuffer)
        {
            return current.AccessToken;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check: another call may have refreshed while this one waited for the lock.
            current = _tokens;
            if (current is null)
            {
                return null;
            }

            if (current.AccessTokenExpiresAtUtc > _timeProvider.GetUtcNow() + RenewalBuffer)
            {
                return current.AccessToken;
            }

            bool refreshed = await RefreshInternalAsync(current.RefreshToken, cancellationToken).ConfigureAwait(false);
            return refreshed ? _tokens?.AccessToken : null;
        }
        catch (HttpRequestException)
        {
            // Offline: hand back the (possibly already-expired) access token rather than
            // silently sending the request unauthenticated. A truly expired token gets a
            // clear 401 from the API instead of a confusing failure with no explanation.
            return current.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<bool> RefreshInternalAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            TokenResponse response = await _apiClient
                .RefreshTokenAsync(new RefreshTokenRequest(refreshToken), cancellationToken)
                .ConfigureAwait(false);

            _tokens = new StoredTokens(
                response.AccessToken,
                response.AccessTokenExpiresAtUtc,
                response.RefreshToken,
                response.RefreshTokenExpiresAtUtc);

            await _tokenStore.SaveAsync(_tokens, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (FleetGoApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            // The refresh token is invalid, expired, or was already used - the session
            // cannot be salvaged, so end it locally too.
            await ClearSessionAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task StoreAndLoadProfileAsync(TokenResponse tokens, CancellationToken cancellationToken)
    {
        _tokens = new StoredTokens(
            tokens.AccessToken,
            tokens.AccessTokenExpiresAtUtc,
            tokens.RefreshToken,
            tokens.RefreshTokenExpiresAtUtc);

        await _tokenStore.SaveAsync(_tokens, cancellationToken);
        RaiseAuthenticationStateChanged();

        await TryLoadCurrentUserAsync(cancellationToken);
    }

    private async Task TryLoadCurrentUserAsync(CancellationToken cancellationToken)
    {
        try
        {
            CurrentUser = await _apiClient.GetCurrentUserAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is FleetGoApiException or HttpRequestException)
        {
            // Non-fatal: the session itself is valid even if this convenience profile
            // fetch failed. A caller that needs the profile can simply try again later.
            CurrentUser = null;
        }
    }

    private async Task ClearSessionAsync(CancellationToken cancellationToken)
    {
        _tokens = null;
        CurrentUser = null;
        await _tokenStore.ClearAsync(cancellationToken);
        RaiseAuthenticationStateChanged();
    }

    private void RaiseAuthenticationStateChanged() => AuthenticationStateChanged?.Invoke(this, EventArgs.Empty);
}

