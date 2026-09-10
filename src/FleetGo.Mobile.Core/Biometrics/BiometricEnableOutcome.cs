namespace FleetGo.Mobile.Core.Biometrics;

/// <summary>Result of asking <see cref="BiometricUnlockCoordinator.EnableAsync"/> to turn biometric unlock on.</summary>
public enum BiometricEnableOutcome
{
    /// <summary>The user confirmed with a biometric challenge and the preference is now on.</summary>
    Enabled,

    /// <summary>The device cannot offer biometrics right now (unsupported, not enrolled, or locked out).</summary>
    NotAvailable,

    /// <summary>The confirmation challenge was cancelled or failed - the preference was left unchanged.</summary>
    Cancelled,
}
