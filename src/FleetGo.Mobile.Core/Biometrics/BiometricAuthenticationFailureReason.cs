namespace FleetGo.Mobile.Core.Biometrics;

/// <summary>Why a call to <see cref="IBiometricAuthenticator.AuthenticateAsync"/> did not succeed.</summary>
public enum BiometricAuthenticationFailureReason
{
    /// <summary>The user dismissed the system prompt or chose a fallback option.</summary>
    Cancelled,

    /// <summary>Biometrics are not usable right now (removed since the last availability check, hardware error, etc.).</summary>
    NotAvailable,

    /// <summary>Too many recent failures - the platform itself is refusing further attempts for a cooldown period.</summary>
    LockedOut,

    /// <summary>The platform reported a failure this abstraction does not have a specific case for.</summary>
    Unknown,
}
