using FleetGo.Mobile.Core.Biometrics;
using Microsoft.Maui.Storage;

namespace FleetGo.Mobile.Services;

/// <summary>
/// <see cref="IBiometricPreferenceStore"/> backed by <see cref="Preferences"/> - plain app
/// settings storage, not Keychain/Keystore. That is deliberate: this stores one boolean
/// opt-in flag, not a secret, so it does not need (and should not claim) secure-storage
/// guarantees. The actual session tokens biometric unlock gates access to are already in
/// <see cref="SecureTokenStore"/>; this class never sees them.
/// </summary>
public sealed class BiometricPreferenceStore : IBiometricPreferenceStore
{
    private const string EnabledKey = "fleetgo.biometric_unlock_enabled";

    private readonly IPreferences _preferences;

    public BiometricPreferenceStore(IPreferences preferences)
    {
        _preferences = preferences;
    }

    public Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_preferences.Get(EnabledKey, false));

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        _preferences.Set(EnabledKey, enabled);
        return Task.CompletedTask;
    }
}
