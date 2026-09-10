using FleetGo.Mobile.Core.Biometrics;

namespace FleetGo.Mobile.Tests;

/// <summary>In-memory stand-in for the on-device biometric-unlock preference.</summary>
internal sealed class FakeBiometricPreferenceStore : IBiometricPreferenceStore
{
    public bool Enabled { get; set; }

    public Task<bool> GetEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult(Enabled);

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        Enabled = enabled;
        return Task.CompletedTask;
    }
}
