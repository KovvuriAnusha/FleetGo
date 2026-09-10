namespace FleetGo.Mobile.Core.Biometrics;

/// <summary>
/// Thin abstraction over whatever the platform's biometric API actually is (AndroidX
/// Biometric on Android, LocalAuthentication on iOS/Mac Catalyst). Defined here, in the
/// platform-agnostic core project, so <see cref="BiometricUnlockCoordinator"/> and its
/// view models can be unit-tested with a fake rather than needing real hardware - the real
/// implementations live in FleetGo.Mobile's per-platform folders (see
/// docs/architecture.md, "Biometric unlock").
/// <para>
/// Deliberately narrow: this never reads, stores, or has any access to the biometric data
/// itself (a fingerprint image, a face template) - that never leaves the OS's own secure
/// hardware. It only ever gets a yes/no answer back, exactly like asking the OS "did the
/// currently-enrolled owner of this device just prove who they are?"
/// </para>
/// </summary>
public interface IBiometricAuthenticator
{
    /// <summary>Checks whether biometric authentication can be offered right now, without prompting the user.</summary>
    Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows the platform's own biometric prompt and waits for the outcome.
    /// </summary>
    /// <param name="reason">
    /// User-facing text explaining why the app is asking - shown by the platform UI
    /// alongside the standard Face ID/fingerprint chrome (e.g. "Unlock FleetGo").
    /// </param>
    Task<BiometricAuthenticationResult> AuthenticateAsync(string reason, CancellationToken cancellationToken = default);
}
