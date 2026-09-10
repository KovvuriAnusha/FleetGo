namespace FleetGo.Mobile.Core.Biometrics;

/// <summary>Result of <see cref="BiometricUnlockCoordinator.TryUnlockAsync"/>, driving what the launch screen shows next.</summary>
public enum BiometricUnlockOutcome
{
    /// <summary>Biometrics succeeded and the stored session was restored - proceed straight to the app.</summary>
    Unlocked,

    /// <summary>The driver has not opted into biometric unlock - fall through to the normal launch flow.</summary>
    NotEnabled,

    /// <summary>There is no stored session to unlock (never signed in, or already signed out) - show the login form.</summary>
    NoStoredSession,

    /// <summary>Biometrics are enabled in preference but not usable right now - fall back to the login form.</summary>
    NotAvailable,

    /// <summary>The user dismissed the biometric prompt - fall back to the login form without treating this as an error.</summary>
    Cancelled,

    /// <summary>The biometric challenge itself failed (not merely cancelled) - fall back to the login form.</summary>
    Failed,
}
