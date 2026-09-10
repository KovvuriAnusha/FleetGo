namespace FleetGo.Mobile.Core.Biometrics;

/// <summary>
/// Persists exactly one bit: whether this driver has opted into biometric unlock on this
/// device. Deliberately not part of <c>ISecureTokenStore</c> - this is a UI preference, not
/// a secret, so it does not need Keychain/Keystore-grade protection, just something that
/// survives an app restart (the real implementation in FleetGo.Mobile uses
/// <c>Microsoft.Maui.Storage.Preferences</c>).
/// </summary>
public interface IBiometricPreferenceStore
{
    Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default);

    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}
