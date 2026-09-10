namespace FleetGo.Mobile.Core.Biometrics;

/// <summary>
/// Everything the app needs to decide around biometric unlock, in one testable place: is it
/// enabled, can it run right now, should it run on this launch, and how a driver turns it on
/// or off. Kept separate from <c>IAuthenticationService</c> - biometrics gate a local UI
/// decision ("should we skip the login form"), they are never involved in actually proving
/// identity to the server (see docs/architecture.md, "Biometric unlock").
/// </summary>
public interface IBiometricUnlockCoordinator
{
    /// <summary>Whether the current driver has opted into biometric unlock on this device.</summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called once at app launch, before deciding whether to show the login form. See
    /// <see cref="BiometricUnlockOutcome"/> for what each result means for the caller.
    /// </summary>
    Task<BiometricUnlockOutcome> TryUnlockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns biometric unlock on for the current driver, after confirming with one live
    /// biometric challenge that it actually works on this device.
    /// </summary>
    Task<BiometricEnableOutcome> EnableAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns biometric unlock off. No biometric challenge is required to opt out.</summary>
    Task DisableAsync(CancellationToken cancellationToken = default);
}
