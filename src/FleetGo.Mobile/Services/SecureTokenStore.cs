using System.Globalization;
using FleetGo.Mobile.Core.Session;
using Microsoft.Maui.Storage;

namespace FleetGo.Mobile.Services;

/// <summary>
/// <see cref="ISecureTokenStore"/> backed by <see cref="ISecureStorage"/> - Android
/// Keystore or iOS/Mac Catalyst Keychain, depending on where the app is running.
/// <para>
/// Stored as four separate string entries rather than one serialised blob: it avoids
/// pulling System.Text.Json's reflection-based serialiser into a release build that is
/// trimmed (and AOT-compiled on iOS) for a type - <see cref="StoredTokens"/> - that never
/// needs a source-generated JSON context because it never crosses the wire.
/// </para>
/// </summary>
public sealed class SecureTokenStore : ISecureTokenStore
{
    private const string AccessTokenKey = "fleetgo.auth.access_token";
    private const string AccessTokenExpiresAtKey = "fleetgo.auth.access_token_expires_utc";
    private const string RefreshTokenKey = "fleetgo.auth.refresh_token";
    private const string RefreshTokenExpiresAtKey = "fleetgo.auth.refresh_token_expires_utc";

    private readonly ISecureStorage _secureStorage;

    public SecureTokenStore(ISecureStorage secureStorage)
    {
        _secureStorage = secureStorage;
    }

    public async Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default)
    {
        await _secureStorage.SetAsync(AccessTokenKey, tokens.AccessToken);
        await _secureStorage.SetAsync(AccessTokenExpiresAtKey, tokens.AccessTokenExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await _secureStorage.SetAsync(RefreshTokenKey, tokens.RefreshToken);
        await _secureStorage.SetAsync(RefreshTokenExpiresAtKey, tokens.RefreshTokenExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    public async Task<StoredTokens?> LoadAsync(CancellationToken cancellationToken = default)
    {
        string? accessToken = await _secureStorage.GetAsync(AccessTokenKey);
        string? accessTokenExpiresAt = await _secureStorage.GetAsync(AccessTokenExpiresAtKey);
        string? refreshToken = await _secureStorage.GetAsync(RefreshTokenKey);
        string? refreshTokenExpiresAt = await _secureStorage.GetAsync(RefreshTokenExpiresAtKey);

        if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(refreshToken) ||
            !DateTimeOffset.TryParse(accessTokenExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset accessExpiry) ||
            !DateTimeOffset.TryParse(refreshTokenExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset refreshExpiry))
        {
            // Missing or corrupted storage is treated the same as "no session" rather than
            // thrown - there is nothing a caller could do to recover a partial token set.
            return null;
        }

        return new StoredTokens(accessToken, accessExpiry, refreshToken, refreshExpiry);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _secureStorage.Remove(AccessTokenKey);
        _secureStorage.Remove(AccessTokenExpiresAtKey);
        _secureStorage.Remove(RefreshTokenKey);
        _secureStorage.Remove(RefreshTokenExpiresAtKey);
        return Task.CompletedTask;
    }
}

