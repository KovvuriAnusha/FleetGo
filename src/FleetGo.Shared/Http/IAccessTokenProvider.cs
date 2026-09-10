namespace FleetGo.Shared.Http;

/// <summary>
/// Supplies the access token to attach to outgoing requests.
/// <para>
/// Lives in <c>FleetGo.Shared</c> - alongside the HTTP pipeline that consumes it
/// (<see cref="BearerTokenHandler"/>) - rather than in the mobile app, so the typed
/// client stays capable of authenticated calls without knowing anything about
/// <c>SecureStorage</c>, Keychain, or how a token is actually kept. The mobile app
/// supplies the real implementation (backed by secure on-device storage); a test or
/// a future non-mobile client can supply its own.
/// </para>
/// </summary>
public interface IAccessTokenProvider
{
    /// <summary>
    /// Returns the current access token, refreshing it first if it is missing or
    /// close to expiry and a refresh token is available. Returns <see langword="null"/>
    /// when there is no signed-in session.
    /// </summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

