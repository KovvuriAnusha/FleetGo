namespace FleetGo.Mobile.Core.Session;

/// <summary>
/// Persists the current session's tokens on-device, behind whatever the platform's most
/// secure storage is (Android Keystore, iOS/Mac Catalyst Keychain).
/// <para>
/// Defined here rather than implemented here: this project stays free of any MAUI
/// reference, so the real implementation - backed by <c>Microsoft.Maui.Storage.SecureStorage</c> -
/// lives in FleetGo.Mobile, the one project with a platform to run on. That also makes
/// <see cref="AuthenticationService"/> trivially testable with an in-memory fake.
/// </para>
/// </summary>
public interface ISecureTokenStore
{
    Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default);

    Task<StoredTokens?> LoadAsync(CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

