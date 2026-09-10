using FleetGo.Mobile.Core.Session;

namespace FleetGo.Mobile.Tests;

/// <summary>In-memory stand-in for on-device secure storage, so tests never touch the Keychain/Keystore.</summary>
internal sealed class FakeSecureTokenStore : ISecureTokenStore
{
    private StoredTokens? _tokens;

    public int SaveCount { get; private set; }

    public int ClearCount { get; private set; }

    public Task SaveAsync(StoredTokens tokens, CancellationToken cancellationToken = default)
    {
        _tokens = tokens;
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<StoredTokens?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_tokens);

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _tokens = null;
        ClearCount++;
        return Task.CompletedTask;
    }

    /// <summary>Seeds a stored session directly, bypassing SaveAsync, for restore-on-launch tests.</summary>
    public void Seed(StoredTokens tokens) => _tokens = tokens;
}

